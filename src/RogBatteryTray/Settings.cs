using Microsoft.Win32;

namespace RogBatteryTray;

/// <summary>Small persisted settings store (HKCU\Software\RogBatteryTray).</summary>
public static class Settings
{
    private const string KeyPath = @"SOFTWARE\RogBatteryTray";

    public static bool AutoSwitchAudio
    {
        get => Get("AutoSwitchAudio", "1") == "1";
        set => Set("AutoSwitchAudio", value ? "1" : "0");
    }

    /// <summary>Default render endpoint id remembered before switching to the headset.</summary>
    public static string? PrevRenderId
    {
        get => Get("PrevRenderId", null);
        set => Set("PrevRenderId", value);
    }

    /// <summary>Default capture endpoint id remembered before switching to the headset.</summary>
    public static string? PrevCaptureId
    {
        get => Get("PrevCaptureId", null);
        set => Set("PrevCaptureId", value);
    }

    private static string? Get(string name, string? fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) as string ?? fallback;
    }

    private static void Set(string name, string? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (value == null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value);
    }
}
