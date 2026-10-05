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
    internal sealed record Sample(DateTime Utc, int Percent, bool Charging);

    internal static readonly string DefaultFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RogBatteryTray", "history.json");

    private static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan EstimateWindow = TimeSpan.FromHours(6);
    private static readonly TimeSpan MinEstimateSpan = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MinChargeEstimateSpan = TimeSpan.FromMinutes(5);
    private const int MaxSamples = 2000;

    private readonly string _filePath;
    private readonly List<Sample> _samples;

    internal int SampleCount => _samples.Count;

    public BatteryHistory() : this(DefaultFilePath, Load(DefaultFilePath))
    {
    }

    internal BatteryHistory(string filePath, List<Sample> samples)
    {
        _filePath = filePath;
        _samples = samples;
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
    public TimeSpan? EstimateRemaining(int currentPercent) =>
        EstimateRemaining(currentPercent, DateTime.UtcNow);

    internal TimeSpan? EstimateRemaining(int currentPercent, DateTime now)
    {
        var cutoff = now - EstimateWindow;
        var window = _samples.Where(s => s.Utc >= cutoff).ToList();

        int lastCharge = window.FindLastIndex(s => s.Charging);
        if (lastCharge >= 0)
            window = window[(lastCharge + 1)..];
        if (window.Count < 2)
            return null;

        double spanHours = (window[^1].Utc - window[0].Utc).TotalHours;
        if (spanHours < MinEstimateSpan.TotalHours)
            return null;

        // Least-squares slope over the whole window: endpoint-only differencing is
        // too sensitive to the ±1% quantization noise of individual samples.
        double dropPerHour = -SlopePerHour(window);
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

        double spanHours = (run[^1].Utc - run[0].Utc).TotalHours;
        if (spanHours < MinChargeEstimateSpan.TotalHours)
            return null;

        double risePerHour = SlopePerHour(run);
        if (risePerHour <= 0.5)
            return null;

        return TimeSpan.FromHours((100 - currentPercent) / risePerHour);
    }

    /// <summary>Least-squares trend of percent per hour over the samples.</summary>
    private static double SlopePerHour(List<Sample> samples)
    {
        double t0 = samples[0].Utc.Ticks;
        var hours = samples.Select(s => (s.Utc.Ticks - t0) / (double)TimeSpan.TicksPerHour).ToArray();
        double tMean = hours.Average();
        double pMean = samples.Average(s => (double)s.Percent);

        double num = 0, den = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            double dt = hours[i] - tMean;
            num += dt * (samples[i].Percent - pMean);
            den += dt * dt;
        }
        return den > 0 ? num / den : 0;
    }

    private void Prune(DateTime now)
    {
        var cutoff = now - Retention;
        _samples.RemoveAll(s => s.Utc < cutoff);
        if (_samples.Count > MaxSamples)
            _samples.RemoveRange(0, _samples.Count - MaxSamples);
    }

    private static List<Sample> Load(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var samples = JsonSerializer.Deserialize<List<Sample>>(File.ReadAllText(filePath));
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
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            // Write to a temp file and swap atomically, so a crash mid-write can't
            // truncate the previous good history.
            string tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_samples));
            File.Move(tmp, _filePath, overwrite: true);
        }
        catch { /* history is best-effort; never break polling over it */ }
    }
}
