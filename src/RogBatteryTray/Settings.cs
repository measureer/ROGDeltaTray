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

    /// <summary>Battery percent at/below which the icon turns red and alerts fire.</summary>
    public static int LowBatteryThreshold
    {
        get => int.TryParse(Get("LowBatteryThreshold", "20"), out int v) ? Math.Clamp(v, 1, 100) : 20;
        set => Set("LowBatteryThreshold", value.ToString());
    }

    /// <summary>Show a balloon when the headset connects / disconnects.</summary>
    public static bool NotifyOnConnection
    {
        get => Get("NotifyOnConnection", "1") == "1";
        set => Set("NotifyOnConnection", value ? "1" : "0");
    }

    /// <summary>Fixed render endpoint to switch back to; null means "auto-remember".</summary>
    public static string? FixedRenderId
    {
        get => Get("FixedRenderId", null);
        set => Set("FixedRenderId", value);
    }

    /// <summary>Fixed capture endpoint to switch back to; null means "auto-remember".</summary>
    public static string? FixedCaptureId
    {
        get => Get("FixedCaptureId", null);
        set => Set("FixedCaptureId", value);
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

    /// <summary>Default communications render endpoint id remembered before switching.</summary>
    public static string? PrevRenderCommId
    {
        get => Get("PrevRenderCommId", null);
        set => Set("PrevRenderCommId", value);
    }

    /// <summary>Default communications capture endpoint id remembered before switching.</summary>
    public static string? PrevCaptureCommId
    {
        get => Get("PrevCaptureCommId", null);
        set => Set("PrevCaptureCommId", value);
    }

    /// <summary>UI language: "auto" (follow Windows), "zh", or "en".</summary>
    public static string Language
    {
        get => Get("Language", "auto") ?? "auto";
        set => Set("Language", value);
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
