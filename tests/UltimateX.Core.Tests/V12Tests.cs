using System.Text;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Security;
using UltimateX.Services.Reports.Pdf;

namespace UltimateX.Core.Tests;

// ======================================================================== Scores (v1.2 §9)

public class ScoreTests
{
    [Fact]
    public void LogScale_reference_is_100_and_reference_divided_by_range_is_0()
    {
        Assert.Equal(100, ScoreMath.LogScale(1000, 1000, 20));
        Assert.Equal(0, ScoreMath.LogScale(50, 1000, 20));
        Assert.Equal(50, ScoreMath.LogScale(1000 / Math.Sqrt(20), 1000, 20));
        Assert.Equal(100, ScoreMath.LogScale(5000, 1000, 20)); // begrenzt
        Assert.Equal(0, ScoreMath.LogScale(0, 1000, 20));
    }

    [Fact]
    public void LogScaleInverse_lower_is_better()
    {
        Assert.Equal(100, ScoreMath.LogScaleInverse(60, 60, 4));
        Assert.True(ScoreMath.LogScaleInverse(120, 60, 4) < ScoreMath.LogScaleInverse(80, 60, 4));
    }

    [Fact]
    public void Linear_works_in_both_directions()
    {
        Assert.Equal(50, ScoreMath.Linear(85, 100, 70));
        Assert.Equal(100, ScoreMath.Linear(60, 100, 70));
        Assert.Equal(0, ScoreMath.Linear(110, 100, 70));
        Assert.Equal(50, ScoreMath.Linear(50, 0, 100));
    }

    [Fact]
    public void WeightedAverage_ignores_missing_values()
    {
        Assert.Equal(60, ScoreMath.WeightedAverage([(null, 10), (80, 1), (40, 1)]));
        Assert.Null(ScoreMath.WeightedAverage([(null, 1), (null, 2)]));
    }

    [Theory]
    [InlineData(0, PerformanceTier.EntryLevel)]
    [InlineData(34, PerformanceTier.EntryLevel)]
    [InlineData(35, PerformanceTier.Standard)]
    [InlineData(54, PerformanceTier.Standard)]
    [InlineData(55, PerformanceTier.Gaming)]
    [InlineData(72, PerformanceTier.HighEndGaming)]
    [InlineData(87, PerformanceTier.Enthusiast)]
    [InlineData(100, PerformanceTier.Enthusiast)]
    public void Tiers(int total, PerformanceTier tier) => Assert.Equal(tier, ScoreMath.TierOf(total));

    [Fact]
    public void Component_scores_null_without_measurement()
    {
        Assert.Null(PerformanceScore.CpuScore(null, 40000));
        Assert.Null(PerformanceScore.GpuScore(null, 500));
        Assert.Null(PerformanceScore.RamScore(0, 60));
        Assert.Null(PerformanceScore.StorageScore(null, null));
        Assert.Null(PerformanceScore.CoolingScore(null, null, false));
    }

    [Fact]
    public void Component_scores_at_reference_are_100()
    {
        Assert.Equal(100, PerformanceScore.CpuScore(ScoreReferences.CpuSingle, ScoreReferences.CpuMulti));
        Assert.Equal(100, PerformanceScore.GpuScore(ScoreReferences.GpuGflops, ScoreReferences.GpuBandwidth));
        Assert.Equal(100, PerformanceScore.RamScore(ScoreReferences.RamReadGbs, ScoreReferences.RamLatencyNs));
        Assert.Equal(100, PerformanceScore.StorageScore(ScoreReferences.DiskSeqReadMbs, ScoreReferences.Disk4kReadIops));
    }

    [Fact]
    public void Cooling_is_capped_when_throttling()
    {
        Assert.Equal(50, PerformanceScore.CoolingScore(85, null, false));
        Assert.Equal(100, PerformanceScore.CoolingScore(60, 60, false));
        Assert.Equal(50, PerformanceScore.CoolingScore(60, 60, true));
    }

    [Fact]
    public void Combine_needs_cpu_and_gpu_for_a_tier()
    {
        var noGpu = PerformanceScore.Combine(new ComponentScores(80, null, 80, 80, null, 80));
        Assert.Equal(80, noGpu.Total);
        Assert.Null(noGpu.Tier);
        Assert.Equal("N/A", noGpu.TierText);

        var full = PerformanceScore.Combine(new ComponentScores(80, 80, 80, 80, 80, 80));
        Assert.Equal(80, full.Total);
        Assert.Equal(PerformanceTier.HighEndGaming, full.Tier);

        var empty = PerformanceScore.Combine(ComponentScores.Empty);
        Assert.Null(empty.Total);
    }

    [Fact]
    public void Faster_hardware_never_scores_lower()
    {
        Assert.True(PerformanceScore.GpuScore(40000, 900) > PerformanceScore.GpuScore(10000, 300));
        Assert.True(PerformanceScore.StorageScore(3500, 15000) > PerformanceScore.StorageScore(550, 8000));
    }
}

// ======================================================================== Hardware-Erkennung (§10, §13)

public class HardwareDatabaseTests
{
    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 SUPER", "rtx 4070 super")]
    [InlineData("NVIDIA GeForce RTX 4070", "rtx 4070")]
    [InlineData("NVIDIA GeForce RTX 4090", "rtx 4090")]
    [InlineData("AMD Radeon RX 7800 XT", "rx 7800 xt")]
    [InlineData("NVIDIA GeForce GTX 1060 6GB", "gtx 1060")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "arc a770")]
    public void FindGpu_prefers_longest_key(string windowsName, string key)
        => Assert.Equal(key, HardwareDatabase.FindGpu(windowsName)?.Key);

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070 Laptop GPU")]
    [InlineData("Microsoft Basic Display Adapter")]
    [InlineData("AMD Radeon(TM) Graphics")]
    [InlineData("")]
    [InlineData(null)]
    public void FindGpu_unknown_or_mobile_is_null(string? name) => Assert.Null(HardwareDatabase.FindGpu(name));

    [Fact]
    public void Gpu_keys_are_unique_and_values_plausible()
    {
        Assert.Equal(HardwareDatabase.Gpus.Count, HardwareDatabase.Gpus.Select(g => g.Key).Distinct().Count());
        Assert.All(HardwareDatabase.Gpus, g =>
        {
            Assert.True(g.Fp32Tflops > 0);
            Assert.True(g.GameIndex > 0);
            Assert.InRange(g.ExpectedComputeEfficiency, 0.3, 0.9);
        });
    }

    [Fact]
    public void EstimateCpu_intel_generations()
    {
        var desktop = HardwareDatabase.EstimateCpu("12th Gen Intel(R) Core(TM) i7-12700K");
        Assert.True(desktop.Recognized);
        Assert.Equal(12, desktop.Generation);
        Assert.Equal("Intel Core", desktop.Family);

        var mobile = HardwareDatabase.EstimateCpu("11th Gen Intel(R) Core(TM) i5-1135G7 @ 2.40GHz");
        Assert.Equal(11, mobile.Generation);

        var old = HardwareDatabase.EstimateCpu("Intel(R) Core(TM) i7-8700K CPU @ 3.70GHz");
        Assert.Equal(8, old.Generation);
        Assert.True(desktop.SingleIndex > old.SingleIndex);
    }

    [Fact]
    public void EstimateCpu_ryzen_x3d_is_faster_in_games()
    {
        var x3d = HardwareDatabase.EstimateCpu("AMD Ryzen 7 7800X3D 8-Core Processor");
        var x = HardwareDatabase.EstimateCpu("AMD Ryzen 7 7700X 8-Core Processor");
        Assert.True(x3d.Recognized);
        Assert.Equal(7, x3d.Generation);
        Assert.True(x3d.GameIndex > x.GameIndex);
    }

    [Fact]
    public void EstimateCpu_known_cores_override_estimate()
        => Assert.Equal(10, HardwareDatabase.EstimateCpu("Intel(R) Core(TM) i5-13400F", 10).Cores);

    [Fact]
    public void EstimateCpu_unknown_is_not_recognized()
    {
        var u = HardwareDatabase.EstimateCpu("Fantasy CPU 9000");
        Assert.False(u.Recognized);
        Assert.Equal(0, u.GameIndex);
        Assert.False(HardwareDatabase.EstimateCpu(null).Recognized);
    }
}

// ======================================================================== RAM (§4, §5)

public class MemoryAnalysisTests
{
    private static RamModuleData M(string slot, string? bank, int rated, int configured, string? part = null, int type = 34)
        => new(slot, bank, 16UL * 1024 * 1024 * 1024, rated, configured, "Vendor", part, null, type);

    [Theory]
    [InlineData("F5-6000J3038F16G", 6000)]
    [InlineData("KF560C36BBEK2-32", 6000)]
    [InlineData("KF548C38BBK2-32", 4800)]
    [InlineData("KF436C17BB/16", 3600)]
    [InlineData("KVR32N22S8/16", 3200)]
    [InlineData("CMK32GX5M2B5600C36", 5600)]
    public void Rated_speed_from_part_number(string part, int mts) => Assert.Equal(mts, MemoryAnalysis.RatedSpeedFromPartNumber(part));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("M378A2K43EB1-CWE")]
    public void Rated_speed_from_unknown_part_is_null(string? part) => Assert.Null(MemoryAnalysis.RatedSpeedFromPartNumber(part));

    [Fact]
    public void Channels_from_slot_names()
    {
        var dual = MemoryAnalysis.EstimateChannels([M("DIMM_A1", "BANK 0", 4800, 4800), M("DIMM_B1", "BANK 1", 4800, 4800)]);
        Assert.Equal(ChannelMode.Dual, dual.Mode);
        Assert.False(dual.Estimated);

        var single = MemoryAnalysis.EstimateChannels([M("ChannelA-DIMM1", "BANK 0", 3200, 3200), M("ChannelA-DIMM2", "BANK 1", 3200, 3200)]);
        Assert.Equal(ChannelMode.Single, single.Mode);
    }

    [Fact]
    public void Channels_are_estimated_without_usable_names()
    {
        var est = MemoryAnalysis.EstimateChannels([M("DIMM 0", null, 3200, 3200), M("DIMM 1", null, 3200, 3200)]);
        Assert.Equal(ChannelMode.Dual, est.Mode);
        Assert.True(est.Estimated);
        Assert.Contains("geschätzt", est.Text, StringComparison.Ordinal);
        Assert.Equal(ChannelMode.Unknown, MemoryAnalysis.EstimateChannels([]).Mode);
    }

    [Fact]
    public void Bandwidth_is_channels_times_speed_times_8_bytes()
    {
        Assert.Equal(96.0, MemoryAnalysis.TheoreticalBandwidthGbs(2, 6000)!.Value, 3);
        Assert.Null(MemoryAnalysis.TheoreticalBandwidthGbs(null, 6000));
        Assert.Null(MemoryAnalysis.TheoreticalBandwidthGbs(2, 0));
    }

    [Fact]
    public void Ram_below_xmp_speed_is_a_warning()
    {
        var r = MemoryAnalysis.CheckSpeed([M("DIMM_A1", null, 4800, 4800, "F5-6000J3038F16G"), M("DIMM_B1", null, 4800, 4800, "F5-6000J3038F16G")]);
        Assert.Equal(CheckStatus.Warning, r.Status);
        Assert.Equal(6000, r.RatedMTs);
        Assert.True(r.RatedFromPartNumber);
        Assert.Contains("nicht aktiv", r.XmpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Ram_with_active_xmp_passes()
    {
        var r = MemoryAnalysis.CheckSpeed([M("DIMM_A1", null, 4800, 6000, "F5-6000J3038F16G")]);
        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.Contains("aktiv", r.XmpText, StringComparison.Ordinal);
    }

    [Fact]
    public void Ram_without_reported_speed_is_not_available()
        => Assert.Equal(CheckStatus.NotAvailable, MemoryAnalysis.CheckSpeed([M("DIMM_A1", null, 0, 0)]).Status);
}

// ======================================================================== RAM-Stabilitätstest (§5)

public class MemoryPatternTests
{
    [Fact]
    public void Plans_scale_with_mode()
    {
        var q = MemTestPlan.For(MemTestMode.Quick);
        var s = MemTestPlan.For(MemTestMode.Standard);
        var e = MemTestPlan.For(MemTestMode.Extended);
        Assert.True(q.MemoryFraction < s.MemoryFraction && s.MemoryFraction < e.MemoryFraction);
        Assert.True(q.Passes < s.Passes && s.Passes < e.Passes);
        Assert.Equal(Enum.GetValues<MemoryPattern>().Length, s.Patterns.Count);
        Assert.True(e.MemoryFraction <= 0.75); // nie den gesamten freien Speicher belegen
    }

    [Theory]
    [InlineData(MemoryPattern.SolidZeros)]
    [InlineData(MemoryPattern.SolidOnes)]
    [InlineData(MemoryPattern.Checkerboard)]
    [InlineData(MemoryPattern.InverseCheckerboard)]
    [InlineData(MemoryPattern.WalkingOnes)]
    [InlineData(MemoryPattern.WalkingZeros)]
    [InlineData(MemoryPattern.AddressInAddress)]
    [InlineData(MemoryPattern.Random)]
    public void Fill_then_verify_finds_no_error(MemoryPattern p)
    {
        var block = new ulong[4096];
        MemoryPatterns.Fill(block, p, 1000, 42);
        var errors = new List<MemoryError>();
        Assert.Equal(0, MemoryPatterns.Verify(block, p, 1000, 42, 1, i => (ulong)i * 8, errors, 10));
        Assert.Empty(errors);
    }

    [Fact]
    public void Flipped_bit_is_detected_with_address()
    {
        var block = new ulong[1024];
        MemoryPatterns.Fill(block, MemoryPattern.Random, 0, 7);
        block[100] ^= 1UL << 13;
        var errors = new List<MemoryError>();
        Assert.Equal(1, MemoryPatterns.Verify(block, MemoryPattern.Random, 0, 7, 2, i => 0x1000UL + (ulong)i * 8, errors, 10));
        var e = Assert.Single(errors);
        Assert.Equal(0x1000UL + 800, e.Address);
        Assert.Equal(1, e.FlippedBits);
        Assert.Equal(2, e.Pass);
    }

    [Fact]
    public void Verify_limits_stored_errors()
    {
        var block = new ulong[100];
        var errors = new List<MemoryError>();
        Assert.Equal(100, MemoryPatterns.Verify(block, MemoryPattern.SolidOnes, 0, 0, 1, i => (ulong)i, errors, 5));
        Assert.Equal(5, errors.Count);
    }

    [Fact]
    public void Evaluation_rules()
    {
        Assert.Equal(CheckStatus.Pass, MemoryPatterns.Evaluate(0, [], true));
        Assert.Equal(CheckStatus.NotAvailable, MemoryPatterns.Evaluate(0, [], false));
        var single = new MemoryError(MemoryPattern.Random, 1, 0, 0b10, 0b00);
        Assert.Equal(CheckStatus.Warning, MemoryPatterns.Evaluate(1, [single], true));
        var multi = new MemoryError(MemoryPattern.Random, 1, 0, 0b11, 0b00);
        Assert.Equal(CheckStatus.Fail, MemoryPatterns.Evaluate(1, [multi], true));
        Assert.Equal(CheckStatus.Fail, MemoryPatterns.Evaluate(2, [single, single], true));
    }

    [Fact]
    public void SplitMix_is_deterministic_and_spread()
    {
        Assert.Equal(MemoryPatterns.SplitMix(123), MemoryPatterns.SplitMix(123));
        Assert.NotEqual(MemoryPatterns.SplitMix(123), MemoryPatterns.SplitMix(124));
    }
}

// ======================================================================== Laufwerke, Netzwerk, Windows (§7, §8, §36)

public class HealthEvaluatorTests
{
    private static DriveHealthInput Drive(DataState health = DataState.Ok, int? temp = 40, int? wear = 5, long? errors = 0, string media = "SSD")
        => new("Samsung SSD 990 PRO", media, health, temp, null, 1000, 200, wear, errors, 0);

    [Fact]
    public void Healthy_drive_is_good() => Assert.Equal(DriveVerdict.Good, DriveHealthEvaluator.Evaluate(Drive()).Verdict);

    [Fact]
    public void Drive_without_any_data_is_not_available()
    {
        var r = DriveHealthEvaluator.Evaluate(new DriveHealthInput("X", "SSD", DataState.NotAvailable, null, null, null, null, null, null, null));
        Assert.Equal(DriveVerdict.NotAvailable, r.Verdict);
        Assert.Equal("N/A", r.Label);
    }

    [Fact]
    public void Wear_and_errors_raise_the_verdict()
    {
        Assert.Equal(DriveVerdict.Warning, DriveHealthEvaluator.Evaluate(Drive(wear: 75)).Verdict);
        Assert.Equal(DriveVerdict.Critical, DriveHealthEvaluator.Evaluate(Drive(wear: 95)).Verdict);
        Assert.Equal(DriveVerdict.Warning, DriveHealthEvaluator.Evaluate(Drive(errors: 1)).Verdict);
        Assert.Equal(DriveVerdict.Critical, DriveHealthEvaluator.Evaluate(Drive(errors: 50)).Verdict);
        Assert.Equal(DriveVerdict.Critical, DriveHealthEvaluator.Evaluate(Drive(health: DataState.Problem)).Verdict);
    }

    [Fact]
    public void Worst_drive_verdict_is_never_hidden_by_na()
    {
        Assert.Equal(DriveVerdict.Critical, DriveHealthEvaluator.Worst([DriveVerdict.Critical, DriveVerdict.NotAvailable]));
        Assert.Equal(DriveVerdict.Warning, DriveHealthEvaluator.Worst([DriveVerdict.Good, DriveVerdict.Warning, DriveVerdict.NotAvailable]));
        Assert.Equal(DriveVerdict.Good, DriveHealthEvaluator.Worst([DriveVerdict.NotAvailable, DriveVerdict.Good]));
        Assert.Equal(DriveVerdict.NotAvailable, DriveHealthEvaluator.Worst([]));
    }

    [Fact]
    public void Temperature_limit_depends_on_media()
    {
        Assert.Equal(DriveVerdict.Warning, DriveHealthEvaluator.Evaluate(Drive(temp: 72)).Verdict);
        Assert.Equal(DriveVerdict.Warning, DriveHealthEvaluator.Evaluate(Drive(temp: 58, media: "HDD")).Verdict);
        Assert.Equal(DriveVerdict.Good, DriveHealthEvaluator.Evaluate(Drive(temp: 58)).Verdict);
    }

    [Fact]
    public void Network_without_measurements_has_no_score()
    {
        var r = NetworkHealth.Evaluate(new NetworkMeasurements(null, null, null, null, null, null, null, null, null, null, null));
        Assert.Null(r.Score);
        Assert.Contains(r.Findings, f => f.Area == "Download" && f.Status == CheckStatus.NotAvailable);
    }

    [Fact]
    public void Perfect_network_scores_100_and_missing_ipv4_caps_it()
    {
        var perfect = new NetworkMeasurements(1000, 500, 5, 1, 0, 10, 1, 1000, null, true, true);
        Assert.Equal(100, NetworkHealth.Evaluate(perfect).Score);
        Assert.Equal(10, NetworkHealth.Evaluate(perfect with { Ipv4Ok = false }).Score);
    }

    [Fact]
    public void Bad_ping_lowers_network_score()
    {
        var good = new NetworkMeasurements(100, 40, 10, 2, 0, 20, 2, 1000, 80, true, true);
        Assert.True(NetworkHealth.Evaluate(good with { PingMs = 90, PacketLossPercent = 4 }).Score < NetworkHealth.Evaluate(good).Score);
    }

    [Fact]
    public void Windows_score_without_data_is_100_and_lists_na()
    {
        var (score, findings) = WindowsHealthScore.Calculate(new WindowsHealthInputs(null, null, null, null, null, null, null, null, null));
        Assert.Equal(100, score);
        Assert.All(findings, f => Assert.Equal(CheckStatus.NotAvailable, f.Status));
    }

    [Fact]
    public void Windows_score_deductions()
    {
        var (score, findings) = WindowsHealthScore.Calculate(new WindowsHealthInputs(true, 0, 2, 0, 0, true, 10, true, 0));
        Assert.Equal(75, score); // Neustart −5, 2 Bluescreens −20
        Assert.Contains(findings, f => f.Status == CheckStatus.Fail);
        var (worst, _) = WindowsHealthScore.Calculate(new WindowsHealthInputs(true, 3, 9, 9, 9, false, 400, false, 2));
        Assert.Equal(0, worst);
    }
}

// ======================================================================== Can My PC Run It / FPS (§13, §14)

public class GameCompatibilityTests
{
    private static readonly PcProfile HighEnd = new("AMD Ryzen 7 7800X3D 8-Core Processor", 8, "NVIDIA GeForce RTX 4090", null, 32, 500, true, 12, 22631, true);

    private static GameInfo Game(string name) => GameCompatibility.Find(name) ?? throw new InvalidOperationException(name + " fehlt");

    [Fact]
    public void Database_is_embedded_with_known_hardware()
    {
        Assert.True(GameCompatibility.BuiltInGames.Count >= 40);
        Assert.NotEqual("–", GameCompatibility.DatabaseVersion);
        // Jedes eingebaute Spiel nennt mindestens eine bekannte empfohlene GPU – sonst wäre keine Schätzung möglich.
        Assert.All(GameCompatibility.BuiltInGames, g => Assert.NotNull(GameCompatibility.RequiredGpuIndex(g.Recommended.Gpu)));
    }

    [Theory]
    [InlineData("Cyberpunk 2077", "Cyberpunk 2077")]
    [InlineData("cyberpunk", "Cyberpunk 2077")]
    [InlineData("CS2", "Counter-Strike 2")]
    [InlineData("baldurs gate", "Baldur's Gate 3")]
    public void Find_by_name_alias_and_part(string query, string expected) => Assert.Equal(expected, GameCompatibility.Find(query)?.Name);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("Ein Spiel das es nicht gibt 12345")]
    public void Find_unknown_is_null(string? q) => Assert.Null(GameCompatibility.Find(q));

    [Fact]
    public void High_end_pc_meets_recommendations()
    {
        var r = GameCompatibility.Evaluate(Game("Cyberpunk 2077"), HighEnd);
        Assert.Equal(CheckStatus.Pass, r.Status);
        Assert.Contains(r.Checks, c => c.Area == "GPU" && c.Status == CheckStatus.Pass);
    }

    [Fact]
    public void Missing_minimum_is_not_recommended()
    {
        var r = GameCompatibility.Evaluate(Game("Cyberpunk 2077"), HighEnd with { Is64Bit = false });
        Assert.Equal(CompatibilityRating.NotRecommended, r.Rating);
        var hdd = GameCompatibility.Evaluate(Game("Cyberpunk 2077"), HighEnd with { SystemDriveIsSsd = false });
        Assert.Equal(CompatibilityRating.NotRecommended, hdd.Rating); // SSD ist Mindestanforderung
    }

    [Fact]
    public void Unknown_gpu_gives_no_rating_and_no_fps()
    {
        var pc = HighEnd with { GpuName = "Microsoft Basic Display Adapter" };
        Assert.Equal(CompatibilityRating.Unknown, GameCompatibility.Evaluate(Game("Cyberpunk 2077"), pc).Rating);
        var fps = GameCompatibility.EstimateFps(Game("Cyberpunk 2077"), pc, Resolution.P1080, Quality.High, false, Upscaler.None);
        Assert.Null(fps.FpsLow);
        Assert.Equal("N/A", fps.Text);
    }

    [Fact]
    public void Fps_is_labelled_estimated_and_drops_with_resolution()
    {
        var g = Game("Cyberpunk 2077");
        var p1080 = GameCompatibility.EstimateFps(g, HighEnd, Resolution.P1080, Quality.High, false, Upscaler.None);
        var p2160 = GameCompatibility.EstimateFps(g, HighEnd, Resolution.P2160, Quality.High, false, Upscaler.None);
        Assert.NotNull(p1080.FpsLow);
        Assert.Contains("ESTIMATED", p1080.Text, StringComparison.Ordinal);
        Assert.True(p1080.FpsHigh >= p1080.FpsLow);
        Assert.True(p2160.FpsHigh <= p1080.FpsHigh);
    }

    [Fact]
    public void Unsupported_features_give_no_fps()
    {
        Assert.Null(GameCompatibility.EstimateFps(Game("Baldur's Gate 3"), HighEnd, Resolution.P1080, Quality.High, true, Upscaler.None).FpsLow);
        Assert.Null(GameCompatibility.EstimateFps(Game("Elden Ring"), HighEnd, Resolution.P1080, Quality.High, false, Upscaler.Dlss).FpsLow);
        var amd = HighEnd with { GpuName = "AMD Radeon RX 7800 XT" };
        Assert.Null(GameCompatibility.EstimateFps(Game("Cyberpunk 2077"), amd, Resolution.P1080, Quality.High, false, Upscaler.Dlss).FpsLow);
    }

    [Fact]
    public void Targets_follow_the_lower_bound()
    {
        var e = new FpsEstimate(Resolution.P1080, Quality.High, false, Upscaler.None, 70, 100, null);
        var t = GameCompatibility.Targets(e);
        Assert.True(t.Single(x => x.Target == 60).Reached);
        Assert.False(t.Single(x => x.Target == 120).Reached);
        Assert.All(GameCompatibility.Targets(e with { FpsLow = null, FpsHigh = null }), x => Assert.False(x.Reached));
    }
}

// ======================================================================== Bottleneck, Upgrade, Gaming Check (§11, §12, §18)

public class AdvisorTests
{
    private static readonly SystemFacts Good = new()
    {
        Scores = new ComponentScores(80, 82, 75, 85, 90, 85),
        CpuName = "AMD Ryzen 7 7800X3D 8-Core Processor",
        CpuCores = 8,
        GpuName = "NVIDIA GeForce RTX 4070 SUPER",
        RamGb = 32,
        RamUsedPercent = 40,
        VramGb = 12,
        RamBelowRatedSpeed = false,
        RamSingleChannel = false,
        SystemDriveIsSsd = true,
        CpuThrottled = false,
        GpuThrottled = false,
        CpuMaxTempC = 75,
        GpuMaxTempC = 70,
        PowerPlanIsSaver = false,
        PowerPlan = "Ausbalanciert",
        PcieCurrentWidth = 16,
        PcieMaxWidth = 16,
        GpuDriverAgeDays = 30,
        GameModeOn = true,
        HardwareSchedulingOn = true,
        ResizableBarOn = true,
        PingMs = 15,
        SecureBootOn = true,
        TpmReady = true,
    };

    [Fact]
    public void Balanced_system_has_no_bottleneck_and_needs_no_upgrade()
    {
        var b = BottleneckAnalyzer.Analyze(Good);
        Assert.DoesNotContain(b.Findings, f => f.Status is CheckStatus.Warning or CheckStatus.Fail);
        Assert.Equal("Kein deutlicher Engpass erkannt.", b.Summary);
        var u = UpgradeAdvisor.Advise(Good);
        Assert.Empty(u.Upgrades);
        Assert.Empty(u.FreeFixes);
        Assert.Equal("NO UPGRADE CURRENTLY REQUIRED", u.Headline);
    }

    [Fact]
    public void Strong_gpu_with_weak_cpu_is_cpu_limit()
    {
        var f = Good with { Scores = Good.Scores with { Cpu = 40, Gpu = 80 } };
        var b = BottleneckAnalyzer.Analyze(f);
        Assert.Contains(b.Findings, x => x.Area == "CPU Limit" && x.Status == CheckStatus.Warning);
        Assert.StartsWith("Wahrscheinlichster Engpass", b.Summary, StringComparison.Ordinal);
        Assert.Contains(UpgradeAdvisor.Advise(f).Upgrades, x => x.Component == "CPU");
    }

    [Fact]
    public void Hdd_is_the_best_upgrade()
    {
        var u = UpgradeAdvisor.Advise(Good with { SystemDriveIsSsd = false });
        Assert.Equal("Storage", u.Upgrades[0].Component);
        Assert.Equal("Best Upgrade: Storage", u.Headline);
    }

    [Fact]
    public void Configuration_problems_are_free_fixes_first()
    {
        var u = UpgradeAdvisor.Advise(Good with { RamBelowRatedSpeed = true, RamSingleChannel = true, PowerPlanIsSaver = true });
        Assert.Equal(3, u.FreeFixes.Count);
        Assert.Empty(u.Upgrades); // XMP/Kanäle/Energieprofil sind keine Kaufempfehlung
    }

    [Fact]
    public void Without_benchmark_the_advisor_asks_for_one()
    {
        var u = UpgradeAdvisor.Advise(new SystemFacts());
        Assert.Empty(u.Upgrades);
        Assert.Contains("Complete Benchmark", u.Headline, StringComparison.Ordinal);
        Assert.Contains("Complete Benchmark", BottleneckAnalyzer.Analyze(new SystemFacts()).Findings[0].Advice, StringComparison.Ordinal);
    }

    [Fact]
    public void Gaming_check_flags_hdd_and_unknown_values_as_na()
    {
        var good = GamingCheck.Evaluate(Good);
        Assert.NotNull(good.Score);
        Assert.InRange(good.Score!.Value, 0, 100);
        var hdd = GamingCheck.Evaluate(Good with { SystemDriveIsSsd = false });
        Assert.Contains(hdd.Findings, f => f.Area == "SSD" && f.Status == CheckStatus.Fail);
        Assert.True(hdd.Score < good.Score);
        var empty = GamingCheck.Evaluate(new SystemFacts());
        Assert.Contains(empty.Findings, f => f.Area == "RAM" && f.Status == CheckStatus.NotAvailable);
    }
}

// ======================================================================== New PC Setup – Quellen, Hash, Signatur (§19–§31)

public class SoftwareCatalogTests
{
    private static readonly string[] MozillaHosts = ["download.mozilla.org", "ftp.mozilla.org"];

    [Fact]
    public void Catalog_entries_are_complete_and_ids_pass_the_command_whitelist()
    {
        Assert.Equal(SoftwareCatalog.All.Count, SoftwareCatalog.All.Select(p => p.Key).Distinct().Count());
        foreach (var p in SoftwareCatalog.All.Append(SoftwareCatalog.ClamAv))
        {
            if (p.Source == PackageSource.Winget)
            {
                Assert.NotEmpty(p.OfficialHosts);
                CommandCatalog.Build(AllowedCommand.WingetShow, p.PackageId);
                CommandCatalog.Build(AllowedCommand.WingetInstall, p.PackageId);
            }
            else CommandCatalog.Build(AllowedCommand.WingetInstallStore, p.PackageId);
            Assert.NotEmpty(p.DetectNames);
        }
    }

    [Fact]
    public void Profiles()
    {
        Assert.Empty(SoftwareCatalog.ForProfile(SetupProfile.Custom));
        var basic = SoftwareCatalog.ForProfile(SetupProfile.Basic).Select(p => p.Key).ToList();
        Assert.Contains("firefox", basic);
        Assert.Contains("7zip", basic);
        var gaming = SoftwareCatalog.ForProfile(SetupProfile.Gaming).Select(p => p.Key).ToList();
        Assert.Contains("steam", gaming);
        Assert.True(basic.All(gaming.Contains));
        Assert.True(SoftwareCatalog.ProfileIncludesOffice(SetupProfile.Office));
        Assert.False(SoftwareCatalog.ProfileIncludesOffice(SetupProfile.Gaming));
    }

    [Fact]
    public void Recommendations_skip_installed_programs()
    {
        var (kind, keys, _) = SoftwareCatalog.Recommend(new SystemFacts { GpuName = "NVIDIA GeForce RTX 4070" }, ["steam"]);
        Assert.Equal("Gaming PC detected", kind);
        Assert.DoesNotContain("steam", keys);
        Assert.Contains("discord", keys);
    }

    [Theory]
    [InlineData("https://download.mozilla.org/?product=firefox-latest", true)]
    [InlineData("https://ftp.mozilla.org/pub/firefox/setup.exe", true)]
    [InlineData("http://download.mozilla.org/setup.exe", false)]
    [InlineData("https://evil-mozilla.org/setup.exe", false)]
    [InlineData("https://download.mozilla.org.evil.example/setup.exe", false)]
    [InlineData("https://user:pw@download.mozilla.org/setup.exe", false)]
    [InlineData("ftp://download.mozilla.org/setup.exe", false)]
    [InlineData("not a url", false)]
    [InlineData(null, false)]
    public void Download_url_validation(string? url, bool ok) => Assert.Equal(ok, SoftwareCatalog.ValidateDownloadUrl(url, MozillaHosts).Ok);

    [Fact]
    public void Filehosters_are_blocked_even_if_listed()
    {
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://www.mediafire.com/file/x.exe", ["mediafire.com"]).Ok);
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://bit.ly/abc", ["bit.ly"]).Ok);
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://download.mozilla.org/x", []).Ok);
    }

    [Fact]
    public void Github_is_only_allowed_for_the_official_repository()
    {
        string[] hosts = ["github.com/microsoft/PowerToys/"];
        Assert.True(SoftwareCatalog.ValidateDownloadUrl("https://github.com/microsoft/PowerToys/releases/download/v0.85.1/PowerToysSetup-0.85.1-x64.exe", hosts).Ok);
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://github.com/evil/PowerToys/releases/download/v1/setup.exe", hosts).Ok);
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://github.com/microsoft/PowerToys/../../evil/x/setup.exe", hosts).Ok);
        Assert.False(SoftwareCatalog.ValidateDownloadUrl("https://evil.github.com/microsoft/PowerToys/setup.exe", hosts).Ok);
        Assert.DoesNotContain(SoftwareCatalog.All.Append(SoftwareCatalog.ClamAv).SelectMany(p => p.OfficialHosts), h => h == "github.com");
    }

    [Fact]
    public void Host_matching_is_exact_or_subdomain()
    {
        Assert.True(SoftwareCatalog.HostMatches("adobe.com", "adobe.com"));
        Assert.True(SoftwareCatalog.HostMatches("ardownload2.adobe.com", "adobe.com"));
        Assert.False(SoftwareCatalog.HostMatches("evil-adobe.com", "adobe.com"));
        Assert.False(SoftwareCatalog.HostMatches("adobe.com.evil.example", "adobe.com"));
    }

    [Fact]
    public void Sha256_validation_and_comparison()
    {
        var h = new string('a', 64);
        Assert.True(SoftwareCatalog.IsValidSha256(h));
        Assert.False(SoftwareCatalog.IsValidSha256(new string('a', 63)));
        Assert.False(SoftwareCatalog.IsValidSha256(new string('g', 64)));
        Assert.False(SoftwareCatalog.IsValidSha256(null));
        Assert.True(SoftwareCatalog.HashesMatch(h, h.ToUpperInvariant()));
        Assert.False(SoftwareCatalog.HashesMatch(h, new string('b', 64)));
        Assert.False(SoftwareCatalog.HashesMatch(null, null));
    }

    [Fact]
    public void Publisher_matching()
    {
        Assert.True(SoftwareCatalog.PublisherMatches("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond", "Microsoft Corporation"));
        Assert.False(SoftwareCatalog.PublisherMatches("CN=Evil Corp", "Microsoft Corporation"));
        Assert.False(SoftwareCatalog.PublisherMatches(null, "Microsoft Corporation"));
        Assert.False(SoftwareCatalog.PublisherMatches("CN=Microsoft Corporation", ""));
    }

    [Fact]
    public void Parse_winget_show_english()
    {
        const string output = """
            Found Mozilla Firefox (en-US) [Mozilla.Firefox]
            Version: 131.0.3
            Publisher: Mozilla
            Installer:
              Installer Type: nullsoft
              Installer Url: https://download-installer.cdn.mozilla.net/pub/firefox/releases/131.0.3/win64/en-US/Firefox%20Setup%20131.0.3.exe
              Installer SHA256: 0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef
            """;
        var m = SoftwareCatalog.ParseWingetShow(output);
        Assert.Equal("131.0.3", m.Version);
        Assert.Equal("Mozilla", m.Publisher);
        Assert.StartsWith("https://download-installer.cdn.mozilla.net/", m.InstallerUrl, StringComparison.Ordinal);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", m.Sha256);
    }

    [Fact]
    public void Parse_winget_show_german_and_empty()
    {
        const string output = "Gefunden 7-Zip [7zip.7zip]\r\nVersion: 24.08\r\nHerausgeber: Igor Pavlov\r\nInstallationsprogramm:\r\n  Installer-URL: https://www.7-zip.org/a/7z2408-x64.exe\r\n  Installer-SHA256: ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789\r\n";
        var m = SoftwareCatalog.ParseWingetShow(output);
        Assert.Equal("24.08", m.Version);
        Assert.Equal("Igor Pavlov", m.Publisher);
        Assert.Equal("https://www.7-zip.org/a/7z2408-x64.exe", m.InstallerUrl);
        Assert.NotNull(m.Sha256);

        var none = SoftwareCatalog.ParseWingetShow("Kein Paket gefunden.");
        Assert.Null(none.InstallerUrl);
        Assert.Null(none.Sha256);
    }

    [Fact]
    public void Detect_installed_programs()
    {
        var found = SoftwareCatalog.DetectInstalled([("Mozilla Firefox (x64 de)", "131.0"), ("7-Zip 24.08 (x64)", "24.08"), ("Irgendwas", null)]);
        Assert.Equal("131.0", found["firefox"]);
        Assert.True(found.ContainsKey("7zip"));
        Assert.False(found.ContainsKey("vlc"));
    }

    [Fact]
    public void Search_url_is_https_and_fully_encoded()
    {
        var u = SearchQueries.SearchUrl("RTX 4070 & \"best\" games?");
        Assert.Equal("https", u.Scheme);
        Assert.Equal("www.google.com", u.Host);
        Assert.DoesNotContain("&", u.Query[1..].Replace("q=", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("%26", u.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public void Search_queries_use_short_hardware_names()
    {
        Assert.Equal("i7-12700K", SearchQueries.ShortCpu("12th Gen Intel(R) Core(TM) i7-12700K").Replace("12th Gen ", "", StringComparison.Ordinal));
        Assert.Equal("RTX 4070", SearchQueries.ShortGpu("NVIDIA GeForce RTX 4070"));
        var q = SearchQueries.GameQueries("AMD Ryzen 7 7800X3D 8-Core Processor", "NVIDIA GeForce RTX 4070", 31.8, Resolution.P1440);
        Assert.All(q, x => Assert.DoesNotContain("  ", x, StringComparison.Ordinal));
        Assert.Contains(q, x => x.Contains("32GB", StringComparison.Ordinal) && x.Contains("1440p", StringComparison.Ordinal));
    }
}

// ======================================================================== ClamAV-Befehle (§27)

public class ClamAvCommandTests
{
    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData("//server/share")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData(@"/\server\share")]
    [InlineData("relative\\path")]
    [InlineData("C:relative")]
    [InlineData("-r")]
    [InlineData("C:\\x\"; calc")]
    [InlineData("")]
    public void Scan_target_rejects_network_relative_and_option_paths(string path)
        => Assert.Throws<ArgumentException>(() => CommandCatalog.ScanTarget(path));

    [Fact]
    public void Scan_commands_exclude_own_quarantine_and_signatures()
    {
        var temp = Path.GetTempPath();
        foreach (var cmd in new[] { AllowedCommand.ClamAvScan, AllowedCommand.ClamAvScanQuarantine })
        {
            var spec = CommandCatalog.Build(cmd, temp);
            Assert.Contains(CommandCatalog.ClamAvExcludeOwnData, spec.Arguments);
            Assert.Equal(Path.GetFullPath(temp), spec.Arguments[^1]); // Ziel immer als letztes, eigenes Argument
        }
    }
}

// ======================================================================== Snapshot (§38)

public class SnapshotComparerTests
{
    private static SystemSnapshotData Snap(Dictionary<string, Dictionary<string, string>> s)
        => new(DateTime.Now, "t", "1.2.0", s.ToDictionary(k => k.Key, k => (IReadOnlyDictionary<string, string>)k.Value));

    [Fact]
    public void Identical_snapshots_have_no_changes()
    {
        var a = Snap(new() { ["Treiber"] = new() { ["GPU"] = "1.0" } });
        Assert.Empty(SnapshotComparer.Compare(a, a).Changes);
    }

    [Fact]
    public void Added_removed_and_changed_are_detected()
    {
        var before = Snap(new()
        {
            ["Treiber"] = new() { ["GPU"] = "31.0.15.1", ["Audio"] = "6.0" },
            ["Programme"] = new() { ["7-Zip"] = "23.01" },
            ["Alt"] = new() { ["X"] = "1" },
        });
        var after = Snap(new()
        {
            ["Treiber"] = new() { ["GPU"] = "32.0.15.6", ["Audio"] = "6.0" },
            ["Programme"] = new() { ["7-Zip"] = "23.01", ["Firefox"] = "131" },
            ["Neu"] = new() { ["Y"] = "2" },
        });
        var c = SnapshotComparer.Compare(before, after);
        Assert.Equal(2, c.Added);
        Assert.Equal(1, c.Removed);
        Assert.Equal(1, c.Modified);
        var gpu = Assert.Single(c.Changes, x => x.Kind == ChangeKind.Changed);
        Assert.Equal(("Treiber", "GPU", "31.0.15.1", "32.0.15.6"), (gpu.Section, gpu.Key, gpu.Before, gpu.After));
        Assert.Equal(1, c.PerSection["Programme"]);
        Assert.False(c.PerSection.ContainsKey("Treiber") && c.PerSection["Treiber"] > 1);
    }
}

// ======================================================================== PDF-Bericht mit Bewertung (§40)

public class ReportAnalysisTests
{
    private static string ContentText(byte[] pdf)
    {
        var raw = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        var idx = 0;
        while ((idx = raw.IndexOf(">>\nstream\n", idx, StringComparison.Ordinal)) >= 0)
        {
            idx += 10;
            var end = raw.IndexOf("\nendstream", idx, StringComparison.Ordinal);
            using var z = new System.IO.Compression.ZLibStream(new MemoryStream(pdf.AsSpan(idx, end - idx).ToArray()), System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.Latin1);
            sb.Append(r.ReadToEnd());
            idx = end;
        }
        return sb.ToString();
    }

    private static PcReportData Data(ReportAnalysis? a) => new()
    {
        ReportId = "UX-TEST-0002",
        Created = new DateTime(2026, 10, 14, 9, 0, 0),
        AppVersion = "1.2.0",
        UserName = "maxmuster",
        Analysis = a,
    };

    [Fact]
    public void Analysis_section_lists_scores_problems_and_recommendations()
    {
        var a = new ReportAnalysis(
            PerformanceScore.Combine(new ComponentScores(70, 74, 66, 80, 90, 85)),
            68, 90, "PASS · 0 Fehler", DataState.Ok,
            [("Samsung SSD 990 PRO", "GOOD", DataState.Ok)],
            [new Finding("RAM-Takt", CheckStatus.Warning, "unter Nenntakt", "XMP aktivieren")],
            ["XMP/EXPO-Profil im BIOS aktivieren"]);
        var content = ContentText(PdfWriter.Write(PcReportBuilder.Build(Data(a), new PcReportOptions())));
        Assert.Contains("GAMING SCORE", content, StringComparison.Ordinal); // Beschriftungen in Großbuchstaben
        Assert.Contains("68/100", content, StringComparison.Ordinal);
        Assert.Contains("Samsung SSD 990 PRO", content, StringComparison.Ordinal);
        Assert.Contains("XMP/EXPO-Profil im BIOS aktivieren", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Analysis_section_without_data_and_toggle()
    {
        var withSection = PcReportBuilder.Build(Data(null), new PcReportOptions());
        var without = PcReportBuilder.Build(Data(null), new PcReportOptions { Analysis = false });
        Assert.Contains("Keine Bewertungsdaten vorhanden.", ContentText(PdfWriter.Write(withSection)), StringComparison.Ordinal);
        Assert.True(without.Pages.Count < withSection.Pages.Count);
    }
}

// ======================================================================== Übersetzungen der neuen Seiten

public sealed class V12LocalizationTests : IDisposable
{
    public V12LocalizationTests()
    {
        using var s = typeof(Loc).Assembly.GetManifestResourceStream("UltimateX.Core.Localization.en.json")
                      ?? throw new InvalidOperationException("en.json ist nicht eingebettet.");
        Loc.InitializeForTests(s);
    }

    public void Dispose() => Loc.ResetForTests();

    [Theory]
    [InlineData("Bitte zuerst Programme auswählen.", "Please select programs first.")]
    [InlineData("Keine Unterschiede.", "No differences.")]
    [InlineData("Kein deutlicher Engpass erkannt.", "No clear bottleneck detected.")]
    [InlineData("Energiespar-Profil", "Power saver")]
    [InlineData("3 neu · 1 entfernt · 2 geändert", "3 new · 1 removed · 2 changed")]
    public void New_texts_are_translated(string de, string en) => Assert.Equal(en, Loc.T(de));

    [Fact]
    public void Analyzer_texts_are_translated()
    {
        foreach (var f in BottleneckAnalyzer.Analyze(new SystemFacts { RamGb = 4, SystemDriveIsSsd = false }).Findings)
        {
            if (f.Advice is { } advice) Assert.NotEqual(advice, Loc.T(advice));
        }
    }
}
