using System.Text;
using HidSharp;

// HidProbe - HID protocol probing tool for ASUS ROG Delta II dongle (VID 0B05, PID 1AFA).
//
// Usage:
//   hidprobe list                          enumerate all HID interfaces of the dongle
//   hidprobe listen [seconds] [pathPart]   passively read input reports on matching interfaces
//   hidprobe feature <reportIdHex>         read a feature report from matching interfaces
//   hidprobe query <hexBytes> [pathPart]   write output report, then read input reports for 2s
//   hidprobe race <hexRacePacket>          RACE request on COL02: write [06][len][packet],
//                                          then poll GetInputReport(0x07) like Airoha tools do

Console.OutputEncoding = Encoding.UTF8;

const int Vid = 0x0B05;
const int Pid = 0x1AFA;

if (args.Length == 0)
{
    Console.WriteLine("usage: hidprobe <list|listen|feature|query> ...");
    return 1;
}

var devices = DeviceList.Local.GetHidDevices(Vid, Pid).ToList();

switch (args[0].ToLowerInvariant())
{
    case "list":
        return List(devices);
    case "listen":
    {
        int seconds = args.Length > 1 ? int.Parse(args[1]) : 60;
        string? filter = args.Length > 2 ? args[2] : null;
        return Listen(devices, seconds, filter);
    }
    case "feature":
    {
        if (args.Length < 2) { Console.WriteLine("usage: hidprobe feature <reportIdHex>"); return 1; }
        byte reportId = Convert.ToByte(args[1], 16);
        return Feature(devices, reportId);
    }
    case "query":
    {
        if (args.Length < 2) { Console.WriteLine("usage: hidprobe query <hexBytes> [pathPart]"); return 1; }
        byte[] payload = Convert.FromHexString(args[1].Replace(" ", ""));
        string? qFilter = args.Length > 2 ? args[2] : null;
        return Query(devices, payload, qFilter);
    }
    case "race":
    {
        if (args.Length < 2) { Console.WriteLine("usage: hidprobe race <hexRacePacket>"); return 1; }
        byte[] packet = Convert.FromHexString(args[1].Replace(" ", ""));
        return Race(devices, packet);
    }
    case "racelisten":
    {
        int rlSeconds = args.Length > 1 ? int.Parse(args[1]) : 60;
        byte rlReportId = args.Length > 2 ? Convert.ToByte(args[2], 16) : (byte)0x07;
        string? rlFilter = args.Length > 3 ? args[3] : "col02";
        return RaceListen(devices, rlSeconds, rlReportId, rlFilter);
    }
    case "racescan":
    {
        // racescan <startHex> <endHex> - query a range of RACE cmd ids (type 0x5A, no payload)
        if (args.Length < 3) { Console.WriteLine("usage: hidprobe racescan <startHex> <endHex>"); return 1; }
        ushort start = Convert.ToUInt16(args[1], 16), stop = Convert.ToUInt16(args[2], 16);
        return RaceScan(devices, start, stop);
    }
    default:
        Console.WriteLine($"unknown command: {args[0]}");
        return 1;
}

static int List(List<HidDevice> devices)
{
    Console.WriteLine($"Found {devices.Count} HID interface(s) for VID_{Vid:X4}&PID_{Pid:X4}:");
    foreach (var d in devices)
    {
        Console.WriteLine("----");
        Console.WriteLine($"  Path        : {d.DevicePath}");
        Console.WriteLine($"  Name        : {SafeGet(d.GetFriendlyName)}");
        Console.WriteLine($"  Manufacturer: {SafeGet(d.GetManufacturer)}");
        Console.WriteLine($"  Product     : {SafeGet(d.GetProductName)}");
        Console.WriteLine($"  Serial      : {SafeGet(d.GetSerialNumber)}");
        Console.WriteLine($"  MaxIn/Out/Ft: {d.GetMaxInputReportLength()}/{d.GetMaxOutputReportLength()}/{d.GetMaxFeatureReportLength()}");
        Console.WriteLine($"  CanOpen     : {d.TryOpen(out _)}");
        try
        {
            var rd = d.GetRawReportDescriptor();
            Console.WriteLine($"  Descriptor  : {Convert.ToHexString(rd)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  Descriptor  : <error: {ex.Message}>");
        }
    }
    return 0;
}

static int Listen(List<HidDevice> devices, int seconds, string? pathFilter)
{
    var streams = new List<(HidDevice dev, HidStream stream, byte[] buf)>();
    foreach (var d in devices)
    {
        if (pathFilter != null && !d.DevicePath.Contains(pathFilter, StringComparison.OrdinalIgnoreCase))
            continue;
        if (!d.TryOpen(out var s)) { Console.WriteLine($"[skip] cannot open {d.DevicePath}"); continue; }
        s.ReadTimeout = 250;
        streams.Add((d, s, new byte[Math.Max(d.GetMaxInputReportLength(), 64)]));
        Console.WriteLine($"[open] {d.DevicePath} (maxIn={d.GetMaxInputReportLength()})");
    }

    if (streams.Count == 0) { Console.WriteLine("No interfaces opened."); return 1; }

    Console.WriteLine($"Listening for {seconds}s... (toggle headset / plug charger / watch Armoury Crate)");
    var end = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < end)
    {
        foreach (var (dev, stream, buf) in streams)
        {
            try
            {
                int n = stream.Read(buf, 0, buf.Length);
                if (n > 0)
                {
                    string tag = Tag(dev.DevicePath);
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {Convert.ToHexString(buf, 0, n)}");
                }
            }
            catch (TimeoutException) { }
            catch (Exception ex) { Console.WriteLine($"[error] {dev.DevicePath}: {ex.Message}"); }
        }
    }
    foreach (var (_, s, _) in streams) s.Dispose();
    return 0;
}

static int Feature(List<HidDevice> devices, byte reportId)
{
    foreach (var d in devices)
    {
        if (d.GetMaxFeatureReportLength() <= 0) continue;
        if (!d.TryOpen(out var s)) continue;
        Console.WriteLine($"[open] {d.DevicePath} (maxFeature={d.GetMaxFeatureReportLength()})");
        var buf = new byte[d.GetMaxFeatureReportLength()];
        buf[0] = reportId;
        try
        {
            s.GetFeature(buf);
            Console.WriteLine($"  feature 0x{reportId:X2}: {Convert.ToHexString(buf)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  feature 0x{reportId:X2}: <error: {ex.Message}>");
        }
        s.Dispose();
    }
    return 0;
}

static int Query(List<HidDevice> devices, byte[] payload, string? pathFilter = null)
{
    foreach (var d in devices)
    {
        if (d.GetMaxOutputReportLength() <= 0) continue;
        if (pathFilter != null && !d.DevicePath.Contains(pathFilter, StringComparison.OrdinalIgnoreCase)) continue;
        if (!d.TryOpen(out var s)) continue;
        Console.WriteLine($"[open] {d.DevicePath}");
        var outBuf = new byte[d.GetMaxOutputReportLength()];
        Array.Copy(payload, outBuf, Math.Min(payload.Length, outBuf.Length));
        try
        {
            s.Write(outBuf);
            Console.WriteLine($"  sent: {Convert.ToHexString(outBuf)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  write error: {ex.Message}");
            s.Dispose();
            continue;
        }
        s.ReadTimeout = 2000;
        var inBuf = new byte[Math.Max(d.GetMaxInputReportLength(), 64)];
        try
        {
            int n = s.Read(inBuf, 0, inBuf.Length);
            Console.WriteLine($"  recv: {Convert.ToHexString(inBuf, 0, n)}");
        }
        catch (TimeoutException) { Console.WriteLine("  recv: <timeout>"); }
        catch (Exception ex) { Console.WriteLine($"  read error: {ex.Message}"); }
        s.Dispose();
    }
    return 0;
}

static string Tag(string path)
{
    // extract "&ColNN" from the device path for compact tagging
    var i = path.LastIndexOf("col", StringComparison.OrdinalIgnoreCase);
    return i >= 0 ? path.Substring(i).TrimEnd('\\', '}') : "dev";
}

static string SafeGet(Func<string> get)
{
    try { return get(); } catch { return "<n/a>"; }
}

// ---- RACE transport (mirrors auracast-research/race-toolkit USBHIDTransport) ----

static int Race(List<HidDevice> devices, byte[] packet)
{
    var d = devices.FirstOrDefault(x => x.DevicePath.Contains("col02", StringComparison.OrdinalIgnoreCase));
    if (d == null) { Console.WriteLine("COL02 interface not found"); return 1; }
    if (!d.TryOpen(out var s)) { Console.WriteLine("cannot open device"); return 1; }
    using (s)
    {
        // flush any pending input reports
        s.ReadTimeout = 100;
        var flush = new byte[Math.Max(d.GetMaxInputReportLength(), 64)];
        try { while (s.Read(flush, 0, flush.Length) > 0) { } } catch (TimeoutException) { }

        // send: [0x06][len_lo][len_hi][race packet]
        var outBuf = new byte[d.GetMaxOutputReportLength()];
        outBuf[0] = 0x06;
        outBuf[1] = (byte)(packet.Length & 0xFF);
        outBuf[2] = (byte)(packet.Length >> 8);
        Array.Copy(packet, 0, outBuf, 3, packet.Length);
        s.Write(outBuf);
        Console.WriteLine($"sent: {Convert.ToHexString(outBuf, 0, 3 + packet.Length)}");
    }

    // poll GetInputReport(0x07) on a second handle to the same device
    using var handle = Native.OpenDevice(d.DevicePath);
    if (handle.IsInvalid) { Console.WriteLine("CreateFile failed"); return 1; }

    var inBuf = new byte[64];
    for (int attempt = 0; attempt < 40; attempt++)
    {
        Array.Clear(inBuf);
        inBuf[0] = 0x07;
        if (!Native.HidD_GetInputReport(handle, inBuf, inBuf.Length))
        {
            Console.WriteLine($"GetInputReport failed (err {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            return 1;
        }
        int length = inBuf[1] | (inBuf[2] << 8);
        if (length == 0) { Thread.Sleep(25); continue; }

        int shown = Math.Min(3 + length, inBuf.Length);
        Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} recv[{length}]: {Convert.ToHexString(inBuf, 0, shown)}");
        if (inBuf[0] == 0x07)
        {
            // got the RACE response; drain remaining empty reports and stop
            return 0;
        }
    }
    Console.WriteLine("no response (only empty reports)");
    return 1;
}

// ---- continuous RACE indication listener ----

static int RaceListen(List<HidDevice> devices, int seconds, byte reportId = 0x07, string pathPart = "col02")
{
    var d = devices.FirstOrDefault(x => x.DevicePath.Contains(pathPart, StringComparison.OrdinalIgnoreCase));
    if (d == null) { Console.WriteLine($"{pathPart} interface not found"); return 1; }

    using var handle = Native.OpenDevice(d.DevicePath);
    if (handle.IsInvalid) { Console.WriteLine("CreateFile failed"); return 1; }

    Console.WriteLine($"RACE-listening for {seconds}s on GetInputReport(0x{reportId:X2}) [{pathPart}]... (plug/unplug charger, toggle headset)");
    var inBuf = new byte[64];
    var end = DateTime.UtcNow.AddSeconds(seconds);
    string? last = null;
    while (DateTime.UtcNow < end)
    {
        Array.Clear(inBuf);
        inBuf[0] = reportId;
        if (!Native.HidD_GetInputReport(handle, inBuf, inBuf.Length))
        {
            Console.WriteLine($"GetInputReport failed (err {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
            return 1;
        }
        int length = inBuf[1] | (inBuf[2] << 8);
        if (length == 0) { Thread.Sleep(50); continue; }

        int shown = Math.Min(3 + length, inBuf.Length);
        string hex = Convert.ToHexString(inBuf, 0, shown);
        if (hex != last)   // only print when content changes
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} recv[{length}]: {hex}");
            last = hex;
        }
        else
        {
            Thread.Sleep(50);
        }
    }
    return 0;
}

// ---- RACE cmd id scanner ----

static int RaceScan(List<HidDevice> devices, ushort start, ushort stop)
{
    var d = devices.FirstOrDefault(x => x.DevicePath.Contains("col02", StringComparison.OrdinalIgnoreCase));
    if (d == null) { Console.WriteLine("COL02 interface not found"); return 1; }
    if (!d.TryOpen(out var s)) { Console.WriteLine("cannot open device"); return 1; }
    using var handle = Native.OpenDevice(d.DevicePath);
    if (handle.IsInvalid) { Console.WriteLine("CreateFile failed"); return 1; }

    using (s)
    {
        for (uint cmd = start; cmd <= stop; cmd++)
        {
            // drain stale reports
            var flush = new byte[64];
            flush[0] = 0x07;
            while (Native.HidD_GetInputReport(handle, flush, flush.Length) && (flush[1] | (flush[2] << 8)) > 0)
            {
                Console.WriteLine($"  (stale) {Convert.ToHexString(flush, 0, Math.Min(3 + (flush[1] | (flush[2] << 8)), 64))}");
                Array.Clear(flush); flush[0] = 0x07;
            }

            // build RACE packet: 05 5A 02 00 <cmd_lo> <cmd_hi>
            byte[] packet = { 0x05, 0x5A, 0x02, 0x00, (byte)(cmd & 0xFF), (byte)(cmd >> 8) };
            var outBuf = new byte[d.GetMaxOutputReportLength()];
            outBuf[0] = 0x06;
            outBuf[1] = (byte)packet.Length;
            outBuf[2] = 0;
            Array.Copy(packet, 0, outBuf, 3, packet.Length);
            try { s.Write(outBuf); } catch (Exception ex) { Console.WriteLine($"cmd {cmd:X4}: write error {ex.Message}"); continue; }

            // collect response (up to 400ms)
            var inBuf = new byte[64];
            string? got = null;
            for (int i = 0; i < 16; i++)
            {
                Array.Clear(inBuf); inBuf[0] = 0x07;
                if (!Native.HidD_GetInputReport(handle, inBuf, inBuf.Length)) break;
                int length = inBuf[1] | (inBuf[2] << 8);
                if (length == 0) { Thread.Sleep(25); continue; }
                got = Convert.ToHexString(inBuf, 0, Math.Min(3 + length, 64));
                break;
            }
            Console.WriteLine($"cmd {cmd:X4}: {(got ?? "<no response>")}");
        }
    }
    return 0;
}

static class Native
{    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [System.Runtime.InteropServices.DllImport("hid.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool HidD_GetInputReport(
        Microsoft.Win32.SafeHandles.SafeFileHandle HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

    public static Microsoft.Win32.SafeHandles.SafeFileHandle OpenDevice(string path)
        => CreateFile(path, 0x80000000 | 0x40000000 /* GENERIC_READ|GENERIC_WRITE */,
            0x1 | 0x2 /* FILE_SHARE_READ|WRITE */, IntPtr.Zero, 3 /* OPEN_EXISTING */, 0, IntPtr.Zero);
}
