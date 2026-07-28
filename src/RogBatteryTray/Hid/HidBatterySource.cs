using HidSharp;

namespace RogBatteryTray.Hid;

/// <summary>
/// Reads battery level from the ROG Delta II 2.4GHz dongle (VID 0B05 / PID 1AFA).
///
/// Protocol (reverse engineered, see docs/protocol.md):
/// talk to the vendor-defined HID collection (usage page 0xFF00, "col04", report id 0xCC),
/// send query {0xCC, 0x12, 0x07} (same ASUS peripheral protocol family as ROG mice in g-helper),
/// response layout: [0]=0xCC [1]=0x12 [2]=0x07 [3..4]=0 [5]=0x05 [6]=percent [7]=low-batt threshold [8]=0x01.
/// </summary>
public sealed class HidBatterySource : IBatterySource
{
    public const int Vid = 0x0B05;
    public const int Pid = 0x1AFA;

    private const byte ReportId = 0xCC;
    private const int ResponsePercentOffset = 6;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(300);

    private HidStream? _stream;

    public BatteryState Read()
    {
        // A single query can fail transiently (2.4GHz link hiccup, stale report),
        // so retry a few times before reporting the headset as disconnected.
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var state = TryReadOnce();
            if (state != null)
                return state;
            if (attempt < MaxAttempts)
                Thread.Sleep(RetryDelay);
        }
        return BatteryState.Disconnected;
    }

    /// <summary>One query attempt; null means "could not get a valid reading".</summary>
    private BatteryState? TryReadOnce()
    {
        try
        {
            var stream = EnsureOpen();
            if (stream == null)
                return null;

            var outBuf = new byte[64];
            outBuf[0] = ReportId;
            outBuf[1] = 0x12;
            outBuf[2] = 0x07;
            stream.Write(outBuf);

            var inBuf = new byte[64];
            int n = stream.Read(inBuf, 0, inBuf.Length);
            if (n < ResponsePercentOffset + 1 || inBuf[0] != ReportId || inBuf[1] != 0x12 || inBuf[2] != 0x07)
                return null;

            int percent = inBuf[ResponsePercentOffset];
            if (percent is <= 0 or > 100)
                return null;   // dongle present but headset off/asleep

            return new BatteryState(percent, Charging: false, Connected: true);
        }
        catch
        {
            Close();
            return null;
        }
    }

    private HidStream? EnsureOpen()
    {
        if (_stream != null)
            return _stream;

        var device = DeviceList.Local
            .GetHidDevices(Vid, Pid)
            .FirstOrDefault(d => d.DevicePath.Contains("col04", StringComparison.OrdinalIgnoreCase));

        if (device == null || !device.TryOpen(out var stream))
            return null;

        stream.ReadTimeout = 2000;
        stream.WriteTimeout = 2000;
        _stream = stream;
        return stream;
    }

    private void Close()
    {
        _stream?.Dispose();
        _stream = null;
    }

    public void Dispose() => Close();
}
