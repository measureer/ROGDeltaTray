using System.Text.Json;

namespace RogBatteryTray;

/// <summary>
/// Records battery samples to %APPDATA%\RogBatteryTray\history.json and estimates
/// drain rate / remaining runtime. Size is bounded: a sample is appended only when
/// the percentage changes (or every 30 min as a heartbeat), samples older than 7 days
/// are pruned, and the file is capped at 2000 entries (~60 KB).
/// </summary>
public sealed class BatteryHistory
{
    private sealed record Sample(DateTime Utc, int Percent);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RogBatteryTray", "history.json");

    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EstimateWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinEstimateSpan = TimeSpan.FromMinutes(30);
    private const int MaxSamples = 2000;

    private readonly List<Sample> _samples;

    public BatteryHistory()
    {
        _samples = Load();
    }

    /// <summary>Record a reading; writes to disk only when the sample list changed.</summary>
    public void Add(int percent)
    {
        var now = DateTime.UtcNow;
        var last = _samples.Count > 0 ? _samples[^1] : null;
        if (last != null && last.Percent == percent && now - last.Utc < HeartbeatInterval)
            return;

        _samples.Add(new Sample(now, percent));
        Prune(now);
        Save();
    }

    /// <summary>
    /// Estimate remaining runtime from the last 6 hours of samples.
    /// Returns null when there is not enough data or the battery is charging/flat.
    /// </summary>
    public TimeSpan? EstimateRemaining(int currentPercent)
    {
        var cutoff = DateTime.UtcNow - EstimateWindow;
        var window = _samples.Where(s => s.Utc >= cutoff).ToList();
        if (window.Count < 2)
            return null;

        var first = window[0];
        var last = window[^1];
        double hours = (last.Utc - first.Utc).TotalHours;
        if (hours < MinEstimateSpan.TotalHours)
            return null;

        double dropPerHour = (first.Percent - last.Percent) / hours;
        if (dropPerHour <= 0.5)   // charging or nearly idle: estimate not meaningful
            return null;

        return TimeSpan.FromHours(currentPercent / dropPerHour);
    }

    private void Prune(DateTime now)
    {
        var cutoff = now - Retention;
        _samples.RemoveAll(s => s.Utc < cutoff);
        if (_samples.Count > MaxSamples)
            _samples.RemoveRange(0, _samples.Count - MaxSamples);
    }

    private static List<Sample> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var samples = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(FilePath));
                if (samples != null)
                    return samples;
            }
        }
        catch { /* corrupt or unreadable history: start fresh */ }
        return new List<Sample>();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_samples));
        }
        catch { /* history is best-effort; never break polling over it */ }
    }
}
