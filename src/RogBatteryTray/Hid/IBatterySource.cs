namespace RogBatteryTray.Hid;

/// <summary>Snapshot of the headset battery state.</summary>
/// <param name="DonglePresent">The 2.4GHz receiver itself is plugged in.</param>
/// <param name="Connected">The headset is powered on and answering queries.</param>
/// <param name="HeadsetOff">The dongle explicitly NAKed the query — a definitive
/// "headset is off" answer, not a transient link hiccup.</param>
public sealed record BatteryState(int? Percent, bool Charging, bool Connected, bool DonglePresent, bool HeadsetOff)
{
    public static readonly BatteryState Disconnected = new(null, false, false, false, false);
    public static readonly BatteryState DongleOnly = new(null, false, false, true, false);
    public static readonly BatteryState HeadsetOffState = new(null, false, false, true, true);
}

/// <summary>Source of battery readings (HID dongle, ...).</summary>
public interface IBatterySource : IDisposable
{
    BatteryState Read();

    /// <summary>Raised (on a background thread) when a HID device arrives or is removed.</summary>
    event EventHandler? DeviceChanged;
}
