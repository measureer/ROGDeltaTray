namespace RogBatteryTray.Hid;

/// <summary>The 2.4GHz receivers this app can talk to (ASUS vendor id + product ids).</summary>
public static class SupportedDevices
{
    public const int Vid = 0x0B05;

    /// <summary>
    /// 0x1AFA = ROG Delta II (verified); 0x1AD3 = ROG Cetra SpeedNova (same protocol
    /// family per g-helper, not yet confirmed on real hardware).
    /// </summary>
    public static readonly int[] Pids = { 0x1AFA, 0x1AD3 };

    /// <summary>Fragments matched against audio endpoint instance ids (e.g. "VID_0B05&amp;PID_1AFA").</summary>
    public static IEnumerable<string> InstanceIdFragments =>
        Pids.Select(pid => $"VID_{Vid:X4}&PID_{pid:X4}");

    /// <summary>Name substrings used as a fallback when an endpoint's instance id can't be read.</summary>
    public static readonly string[] NameParts = { "ROG DELTA II", "ROG CETRA" };
}
