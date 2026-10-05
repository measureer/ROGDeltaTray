using System.Text.Json;

namespace RogBatteryTray;

/// <summary>
/// Records battery samples to %APPDATA%\RogBatteryTray\history.json and estimates
/// drain rate / remaining runtime / time-to-full. Size is bounded: a sample is
/// appended only when the percentage changes (or every 30 min as a heartbeat),
/// samples older than 7 days are pruned, and the file is capped at 2000 entries (~60 KB).
/// </summary>
public sealed class BatteryHistory
{
    private sealed record Sample(DateTime Utc, int Percent, bool Charging);

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RogBatteryTray", "history.json");

    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EstimateWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinEstimateSpan = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinChargeEstimateSpan = TimeSpan.FromMinutes(5);
    private const int MaxSamples = 2000;

    private readonly List<Sample> _samples;

    public BatteryHistory()
    {
        _samples = Load();
    }

    /// <summary>Record a reading; writes to disk only when the sample list changed.</summary>
    public void Add(int percent, bool charging)
    {
        var now = DateTime.UtcNow;
        var last = _samples.Count > 0 ? _samples[^1] : null;
        if (last != null && last.Percent == percent && last.Charging == charging
            && now - last.Utc < HeartbeatInterval)
            return;

        _samples.Add(new Sample(now, percent, charging));
        Prune(now);
        Save();
    }

    /// <summary>
    /// Estimate remaining runtime from the last 6 hours of samples.
    /// Returns null when there is not enough data or the battery is charging/flat.
    /// Samples from before the last charge session are ignored, so a charge inside
    /// the window doesn't flatten the measured drain rate.
    /// </summary>
    public TimeSpan? EstimateRemaining(int currentPercent)
    {
        var cutoff = DateTime.UtcNow - EstimateWindow;
        var window = _samples.Where(s => s.Utc >= cutoff).ToList();

        int lastCharge = window.FindLastIndex(s => s.Charging);
        if (lastCharge >= 0)
            window = window[(lastCharge + 1)..];
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

    /// <summary>
    /// Estimate time until fully charged from the trailing run of charging samples.
    /// Returns null when there is not enough charging data yet.
    /// </summary>
    public TimeSpan? EstimateTimeToFull(int currentPercent)
    {
        int i = _samples.Count - 1;
        while (i >= 0 && _samples[i].Charging)
            i--;
        var run = _samples[(i + 1)..];
        if (run.Count < 2)
            return null;

        var first = run[0];
        var last = run[^1];
        double hours = (last.Utc - first.Utc).TotalHours;
        if (hours < MinChargeEstimateSpan.TotalHours)
            return null;

        double risePerHour = (last.Percent - first.Percent) / hours;
        if (risePerHour <= 0.5)
            return null;

        return TimeSpan.FromHours((100 - currentPercent) / risePerHour);
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
