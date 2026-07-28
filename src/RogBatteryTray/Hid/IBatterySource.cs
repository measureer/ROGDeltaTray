namespace RogBatteryTray.Hid;

/// <summary>Snapshot of the headset battery state.</summary>
public sealed record BatteryState(int? Percent, bool Charging, bool Connected)
{
    public static readonly BatteryState Disconnected = new(null, false, false);
}

/// <summary>Source of battery readings (HID dongle, ...).</summary>
public interface IBatterySource : IDisposable
{
    BatteryState Read();
}
