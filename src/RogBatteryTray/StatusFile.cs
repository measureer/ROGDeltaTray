using System.Text.Json;
using RogBatteryTray.Hid;

namespace RogBatteryTray;

/// <summary>
/// Writes the latest headset state to %APPDATA%\RogBatteryTray\status.json so external
/// tools (Rainmeter, Stream Deck, AutoHotkey, scripts, ...) can read the battery level
/// without talking HID themselves. Writes are skipped when nothing changed.
/// </summary>
public static class StatusFile
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RogBatteryTray", "status.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private static string? _lastSignature;

    public static void Write(BatteryState state, TimeSpan? estimatedRemaining)
    {
        int? remainingMinutes = estimatedRemaining.HasValue
            ? (int?)Math.Round(estimatedRemaining.Value.TotalMinutes)
            : null;

        // UpdatedAt changes on every call, so dedupe on the actual state fields.
        string signature = $"{state.Connected}|{state.Percent}|{state.Charging}" +
                           $"|{state.DonglePresent}|{state.HeadsetOff}|{remainingMinutes}";
        if (signature == _lastSignature)
            return;

        var payload = new
        {
            state.Connected,
            state.Percent,
            state.Charging,
            state.DonglePresent,
            state.HeadsetOff,
            EstimatedRemainingMinutes = remainingMinutes,
            UpdatedAt = DateTime.UtcNow.ToString("o"),
        };
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(payload, JsonOptions));
            _lastSignature = signature;
        }
        catch { /* status export is best-effort; never break polling over it */ }
    }
}
