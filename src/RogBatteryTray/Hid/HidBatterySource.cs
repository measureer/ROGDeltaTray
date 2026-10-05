using HidSharp;

namespace RogBatteryTray.Hid;

/// <summary>
/// Reads battery level from a supported 2.4GHz dongle (see <see cref="SupportedDevices"/>).
///
/// Protocol (reverse engineered, see docs/protocol.md):
/// talk to the vendor-defined HID collection (usage page 0xFF00, report id 0xCC),
/// send query {0xCC, 0x12, 0x07} (same ASUS peripheral protocol family as ROG mice
/// and headsets in g-helper), response layout:
/// [0]=0xCC [1]=0x12 [2]=0x07 [3..4]=0 [5]=sleep timer [6]=percent [7]=low-batt threshold
/// [8]=low-batt voice prompt flag. Charging state comes from a separate {0xCC, 0x12, 0x08}
/// query ([5]==1 means charging), as in g-helper's AsusHeadset.
/// </summary>
public sealed class HidBatterySource : IBatterySource
{
    public const int Vid = SupportedDevices.Vid;

    private const byte ReportId = 0xCC;
    private const int VendorUsagePage = 0xFF00;
    private const int ResponsePercentOffset = 6;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private readonly object _sync = new();
    private HidStream? _stream;
    private bool? _lastDonglePresent;

    public event EventHandler? DeviceChanged;

    public HidBatterySource()
    {
        DeviceList.Local.Changed += OnDeviceListChanged;
    }

    public BatteryState Read()
    {
        lock (_sync)
        {
            // A single query can fail transiently (2.4GHz link hiccup, stale report),
            // so retry a few times before reporting the headset as disconnected.
            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var state = TryReadOnce();
                if (state != null)
                    return state;
                if (!IsDonglePresent())
                {
                    Close();
                    return BatteryState.Disconnected;   // receiver unplugged — no point retrying
                }
                if (attempt < MaxAttempts)
                    Thread.Sleep(RetryDelay);
            }
            // The receiver is still there, but the headset itself isn't answering.
            return IsDonglePresent() ? BatteryState.DongleOnly : BatteryState.Disconnected;
        }
    }

    /// <summary>One query attempt; null means "could not get a valid reading".</summary>
    private BatteryState? TryReadOnce()
    {
        try
        {
            var (response, nak) = WriteForResponse(0x12, 0x07);
            if (nak)
                return BatteryState.HeadsetOffState;   // definitive answer — caller skips retries
            if (response == null)
                return null;

            int percent = response[ResponsePercentOffset];
            if (percent is <= 0 or > 100)
                return null;   // dongle present but headset off/asleep

            bool charging = false;
            var (chargeResponse, _) = WriteForResponse(0x12, 0x08);
            if (chargeResponse != null)
                charging = chargeResponse[5] == 1;

            return new BatteryState(percent, charging, Connected: true, DonglePresent: true, HeadsetOff: false);
        }
        catch
        {
            Close();
            return null;
        }
    }

    /// <summary>
    /// Writes a command and reads until its echo comes back, g-helper style:
    /// the input queue is drained first (stale reports would otherwise be mistaken
    /// for the response), async event packets pushed by the dongle are skipped,
    /// and the firmware NAK (FF AA) is reported separately — for the battery query
    /// a NAK is the dongle's definitive "headset is off" answer.
    /// </summary>
    private (byte[]? Response, bool Nak) WriteForResponse(byte cmd1, byte cmd2)
    {
        var stream = EnsureOpen();
        if (stream == null)
            return (null, false);

        Drain(stream);

        var outBuf = new byte[64];
        outBuf[0] = ReportId;
        outBuf[1] = cmd1;
        outBuf[2] = cmd2;
        stream.Write(outBuf);

        var inBuf = new byte[64];
        for (int i = 0; i < 4; i++)
        {
            int n = stream.Read(inBuf, 0, inBuf.Length);
            if (n < 3 || inBuf[0] != ReportId)
                continue;
            if (inBuf[1] == 0xFF && inBuf[2] == 0xAA)
                return (null, true);   // firmware NAK
            if (inBuf[1] == cmd1 && inBuf[2] == cmd2)
            {
                if (inBuf[5] == 0xFF && inBuf[6] == 0xAA)
                    return (null, true);   // NAK payload (headset off)
                return (inBuf, false);
            }
            // Non-matching echo: an async event report — keep reading.
        }
        return (null, false);
    }

    /// <summary>Flush pending input reports so the next read returns a fresh response.</summary>
    private static void Drain(HidStream stream)
    {
        int saved = stream.ReadTimeout;
        stream.ReadTimeout = 1;
        try
        {
            var buf = new byte[64];
            while (true)
            {
                try { stream.Read(buf, 0, buf.Length); }
                catch { break; }
            }
        }
        finally
        {
            stream.ReadTimeout = saved;
        }
    }

    /// <summary>Any supported receiver is plugged in (any of its HID collections is enumerated).</summary>
    private bool IsDonglePresent()
    {
        try { return SupportedDevices.Pids.Any(pid => DeviceList.Local.GetHidDevices(Vid, pid).Any()); }
        catch { return false; }
    }

    /// <summary>
    /// Finds the vendor control interface by its protocol features — output report
    /// length and the 0xFF00 usage page in the report descriptor — instead of
    /// matching the Windows-generated "col04" path suffix.
    /// </summary>
    private HidDevice? FindVendorDevice()
    {
        try
        {
            foreach (int pid in SupportedDevices.Pids)
            foreach (var device in DeviceList.Local.GetHidDevices(Vid, pid))
            {
                try
                {
                    if (device.GetMaxOutputReportLength() < 64)
                        continue;
                    bool hasVendorPage = device.GetReportDescriptor().DeviceItems
                        .Any(item => item.Usages.GetAllValues()
                            .Any(usage => (usage >> 16) == VendorUsagePage));
                    if (hasVendorPage)
                        return device;
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    private HidStream? EnsureOpen()
    {
        if (_stream != null)
            return _stream;

        var device = FindVendorDevice();
        if (device == null || !device.TryOpen(out var stream))
            return null;

        stream.ReadTimeout = 2000;
        stream.WriteTimeout = 2000;
        _stream = stream;
        return stream;
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        bool present;
        lock (_sync)
        {
            present = IsDonglePresent();
            if (present == _lastDonglePresent)
                return;   // some other HID device came or went — not ours
            _lastDonglePresent = present;
            if (!present)
                Close();   // the stream is dead anyway
        }
        DeviceChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose()
    {
        DeviceList.Local.Changed -= OnDeviceListChanged;
        lock (_sync)
            Close();
    }
}
