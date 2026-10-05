namespace RogBatteryTray.Tests;

using Xunit;

public class BatteryHistoryTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BatteryHistory.Sample Sample(double hoursAgo, int percent, bool charging = false) =>
        new(Now - TimeSpan.FromHours(hoursAgo), percent, charging);

    private static BatteryHistory HistoryWith(params BatteryHistory.Sample[] samples) =>
        new(TempPath(), samples.OrderBy(s => s.Utc).ToList());

    private static string TempPath() => Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");

    private static List<BatteryHistory.Sample> LoadSamples(string path) =>
        System.Text.Json.JsonSerializer.Deserialize<List<BatteryHistory.Sample>>(File.ReadAllText(path))!;

    [Fact]
    public void EstimateRemaining_SteadyDrain_MatchesSlope()
    {
        // 10%/hour drain over the last 2 hours → 70% left means ~7 hours remaining.
        var h = HistoryWith(
            Sample(2, 90),
            Sample(1, 80),
            Sample(0, 70));

        var remaining = h.EstimateRemaining(70, Now);

        Assert.NotNull(remaining);
        Assert.Equal(7.0, remaining!.Value.TotalHours, precision: 1);
    }

    [Fact]
    public void EstimateRemaining_IgnoresSamplesBeforeLastCharge()
    {
        // Drained to 50, charged back to 100 three hours ago, then 100→90 discharging.
        // Only the post-charge run (10%/hour) should drive the estimate.
        var h = HistoryWith(
            Sample(5, 80),
            Sample(4, 50),
            Sample(3.5, 60, charging: true),
            Sample(3, 100, charging: true),
            Sample(1, 100),
            Sample(0, 90));

        var remaining = h.EstimateRemaining(90, Now);

        Assert.NotNull(remaining);
        Assert.Equal(9.0, remaining!.Value.TotalHours, precision: 1);
    }

    [Fact]
    public void EstimateRemaining_TooLittleData_ReturnsNull()
    {
        var h = HistoryWith(Sample(0.05, 90), Sample(0, 89));   // 3-minute span

        Assert.Null(h.EstimateRemaining(89, Now));
    }

    [Fact]
    public void EstimateRemaining_FlatBattery_ReturnsNull()
    {
        var h = HistoryWith(
            Sample(2, 80),
            Sample(1, 80),
            Sample(0, 79));   // under the 0.5%/hour meaningfulness floor

        Assert.Null(h.EstimateRemaining(79, Now));
    }

    [Fact]
    public void EstimateRemaining_IgnoresSamplesOutsideWindow()
    {
        // Old samples (outside the 6h window) must not affect the estimate.
        var h = HistoryWith(
            Sample(20, 100),
            Sample(10, 50),
            Sample(2, 90),
            Sample(1, 80),
            Sample(0, 70));

        var remaining = h.EstimateRemaining(70, Now);

        Assert.NotNull(remaining);
        Assert.Equal(7.0, remaining!.Value.TotalHours, precision: 1);
    }

    [Fact]
    public void EstimateTimeToFull_SteadyCharge_MatchesSlope()
    {
        // Charging run: 20%/hour, currently at 60% → ~2 hours to full.
        var h = HistoryWith(
            Sample(4, 50),
            Sample(2, 20, charging: true),
            Sample(1, 40, charging: true),
            Sample(0, 60, charging: true));

        var full = h.EstimateTimeToFull(60);

        Assert.NotNull(full);
        Assert.Equal(2.0, full!.Value.TotalHours, precision: 1);
    }

    [Fact]
    public void EstimateTimeToFull_NotCharging_ReturnsNull()
    {
        var h = HistoryWith(Sample(1, 50), Sample(0, 49));

        Assert.Null(h.EstimateTimeToFull(49));
    }

    [Fact]
    public void Add_SaveLoad_RoundTrips()
    {
        string path = TempPath();
        try
        {
            var h = new BatteryHistory(path, new List<BatteryHistory.Sample>());
            h.Add(87, charging: false);
            h.Add(86, charging: true);

            var samples = LoadSamples(path);
            Assert.Equal(2, samples.Count);
            Assert.Equal(87, samples[0].Percent);
            Assert.False(samples[0].Charging);
            Assert.Equal(86, samples[1].Percent);
            Assert.True(samples[1].Charging);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }

    [Fact]
    public void Add_SamePercentWithinHeartbeat_DoesNotAppend()
    {
        string path = TempPath();
        try
        {
            var h = new BatteryHistory(path, new List<BatteryHistory.Sample>());
            h.Add(87, charging: false);
            h.Add(87, charging: false);   // within 30 min heartbeat — skipped
            h.Add(86, charging: false);   // changed — appended

            Assert.Equal(2, h.SampleCount);
            Assert.Equal(86, LoadSamples(path)[^1].Percent);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp");
        }
    }
}
