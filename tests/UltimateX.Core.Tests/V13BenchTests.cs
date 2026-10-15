using UltimateX.Core.Analysis;
using UltimateX.Core.Security;
using UltimateX.Services.Benchmark;

namespace UltimateX.Core.Tests;

// ======================================================================== v1.3 Benchmark-Kalibrierung

/// <summary>
/// Kalibrierung mit echten Messwerten (i9-13900K, RTX 4080, 64 GB DDR5-6000 Dual Channel, NVMe-Systemplatte).
/// Ziel: 100 ≈ Spitzenklasse 2026, 13900K ≈ 80–85, RTX 4080 ≈ 80, RAM (54 GB/s, 106 ns) ≈ 60–70, NVMe Gen4 ≈ 90.
/// </summary>
public class V13CalibrationTests
{
    [Fact]
    public void Cpu_13900K_is_high_end_but_not_saturated()
    {
        var cpu = PerformanceScore.CpuScore(77_079, 1_565_122);
        Assert.NotNull(cpu);
        Assert.InRange(cpu!.Value, 80, 85);
    }

    [Fact]
    public void Gpu_RTX4080_scores_about_80_and_RTX5090_class_is_100()
    {
        Assert.InRange(PerformanceScore.GpuScore(49_328, 614)!.Value, 78, 83);
        Assert.Equal(100, PerformanceScore.GpuScore(ScoreReferences.GpuGflops, ScoreReferences.GpuBandwidth));
        Assert.Equal(100, PerformanceScore.GpuScore(110_000, 1_600)); // oberhalb der Referenz: Sättigung
    }

    [Fact]
    public void Ram_ddr5_6000_with_106ns_scores_60_to_70()
        => Assert.InRange(PerformanceScore.RamScore(53.8, 106)!.Value, 60, 70);

    [Fact]
    public void Nvme_gen4_scores_about_90()
    {
        Assert.InRange(PerformanceScore.StorageScore(7_000, 16_000)!.Value, 87, 93);
        Assert.InRange(PerformanceScore.StorageScore(7_000, null)!.Value, 87, 93);
    }

    [Fact]
    public void Slow_secondary_drive_scores_clearly_below_nvme()
    {
        var d = PerformanceScore.StorageScore(400, 9_595)!.Value;
        Assert.InRange(d, 40, 65);
        Assert.True(d < PerformanceScore.StorageScore(7_000, 16_000));
    }

    [Fact]
    public void Saturation_only_above_the_reference()
    {
        Assert.Equal(100, PerformanceScore.CpuScore(ScoreReferences.CpuSingle * 1.5, ScoreReferences.CpuMulti * 1.5));
        Assert.True(PerformanceScore.CpuScore(ScoreReferences.CpuSingle * 0.9, ScoreReferences.CpuMulti * 0.9) < 100);
        Assert.True(PerformanceScore.RamScore(ScoreReferences.RamReadGbs * 0.9, ScoreReferences.RamLatencyNs) < 100);
        Assert.True(PerformanceScore.StorageScore(ScoreReferences.DiskSeqReadMbs * 0.9, ScoreReferences.Disk4kReadIops) < 100);
    }

    [Fact]
    public void Lower_latency_and_more_bandwidth_never_score_lower()
    {
        Assert.True(PerformanceScore.RamScore(53.8, 80) > PerformanceScore.RamScore(53.8, 106));
        Assert.True(PerformanceScore.RamScore(70, 106) > PerformanceScore.RamScore(53.8, 106));
        Assert.True(PerformanceScore.CpuScore(80_000, 1_600_000) > PerformanceScore.CpuScore(40_000, 400_000));
    }

    [Fact]
    public void Total_for_reference_pc_is_high_end_gaming()
    {
        var s = new ComponentScores(PerformanceScore.CpuScore(77_079, 1_565_122), PerformanceScore.GpuScore(49_328, 614),
            PerformanceScore.RamScore(53.8, 106), PerformanceScore.StorageScore(7_000, 16_000), null, null);
        var r = PerformanceScore.Combine(s);
        Assert.Equal(PerformanceTier.HighEndGaming, r.Tier);
    }
}

// ======================================================================== v1.3 Ursachen je Komponente

public class V13CauseTests
{
    private static readonly SystemFacts Everything = new()
    {
        Scores = new ComponentScores(50, 90, null, null, null, null),
        GpuThrottled = true,
        GpuMaxTempC = 88,
        CpuThrottled = true,
        CpuMaxTempC = 95,
        PowerPlanIsSaver = true,
        PcieCurrentWidth = 8,
        PcieMaxWidth = 16,
        GpuDriverAgeDays = 400,
        HeavyBackgroundProcesses = ["chrome"],
        RamBelowRatedSpeed = true,
        RamSingleChannel = true,
    };

    [Fact]
    public void Ram_causes_never_mention_pcie_or_gpu_topics()
    {
        var r = BenchmarkCauses.For(BenchComponent.Ram, Everything);
        Assert.True(r.Detected);
        Assert.Equal(new[] { BenchmarkCauses.XmpOff, BenchmarkCauses.SingleChannel, BenchmarkCauses.Background, BenchmarkCauses.PowerPlan }, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.Pcie, BenchmarkCauses.Checklist(BenchComponent.Ram));
    }

    [Fact]
    public void Gpu_causes_include_pcie_driver_and_cpu_limit()
    {
        var r = BenchmarkCauses.For(BenchComponent.Gpu, Everything);
        Assert.Contains(BenchmarkCauses.Pcie, r.Items);
        Assert.Contains(BenchmarkCauses.Driver, r.Items);
        Assert.Contains(BenchmarkCauses.CpuBottleneck, r.Items);
        Assert.Contains(BenchmarkCauses.Temperature, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.XmpOff, r.Items);
    }

    [Fact]
    public void Cpu_causes_only_cpu_topics()
    {
        var r = BenchmarkCauses.For(BenchComponent.Cpu, Everything);
        Assert.Contains(BenchmarkCauses.Temperature, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.Pcie, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.Driver, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.PowerLimit, r.Items); // 95 °C → Temperatur, nicht Power Limit
    }

    [Fact]
    public void Storage_causes_use_drive_facts()
    {
        var r = BenchmarkCauses.For(BenchComponent.Storage, new SystemFacts(), new StorageCauseFacts(false, "SATA", 5, 75));
        Assert.Equal(new[] { BenchmarkCauses.WrongDrive, BenchmarkCauses.SataInsteadOfNvme, BenchmarkCauses.DriveFull, BenchmarkCauses.DriveTemperature }, r.Items);
        Assert.DoesNotContain(BenchmarkCauses.Pcie, r.Items);
    }

    [Fact]
    public void Without_findings_the_component_checklist_is_returned()
    {
        foreach (var c in Enum.GetValues<BenchComponent>())
        {
            var r = BenchmarkCauses.For(c, new SystemFacts());
            Assert.False(r.Detected);
            Assert.Equal(BenchmarkCauses.Checklist(c), r.Items);
        }
        Assert.Contains(BenchmarkCauses.Pcie, BenchmarkCauses.Checklist(BenchComponent.Gpu));
        Assert.DoesNotContain(BenchmarkCauses.Pcie, BenchmarkCauses.Checklist(BenchComponent.Storage));
        Assert.DoesNotContain(BenchmarkCauses.Pcie, BenchmarkCauses.Checklist(BenchComponent.Cpu));
    }
}

// ======================================================================== v1.3 Systemlaufwerk und Erwartungswerte

public class V13StorageTests
{
    [Theory]
    [InlineData(3, 4, 3000d)]
    [InlineData(4, 4, 6500d)]
    [InlineData(5, 4, 10000d)]
    [InlineData(4, 2, 3250d)]
    [InlineData(4, null, 6500d)]
    public void Nvme_expectation_by_generation(int gen, int? width, double expected)
        => Assert.Equal((double?)expected, StorageExpectation.ExpectedSeqReadMbs("NVMe", "SSD", gen, width));

    [Fact]
    public void Nvme_without_unambiguous_generation_is_na()
    {
        Assert.Null(StorageExpectation.ExpectedSeqReadMbs("NVMe", "SSD", null));
        Assert.Null(StorageExpectation.ExpectedSeqReadMbs("NVMe", "SSD", 2));
        Assert.Equal((double?)500, StorageExpectation.ExpectedSeqReadMbs("SATA", "SSD", null));
        Assert.Equal((double?)150, StorageExpectation.ExpectedSeqReadMbs("SATA", "HDD", null));
        Assert.Null(StorageExpectation.ExpectedSeqReadMbs("USB", "SSD", null));
    }

    [Fact]
    public void Nvme_link_must_be_unambiguous()
    {
        var (gen, width) = StorageExpectation.UnambiguousNvmeLink([(4, 4)], 1);
        Assert.Equal(4, gen);
        Assert.Equal(4, width);

        (gen, width) = StorageExpectation.UnambiguousNvmeLink([(4, 4), (4, 2)], 2);
        Assert.Equal(4, gen);
        Assert.Null(width);

        Assert.Null(StorageExpectation.UnambiguousNvmeLink([(4, 4), (3, 4)], 2).Gen); // gemischt
        Assert.Null(StorageExpectation.UnambiguousNvmeLink([(4, 4)], 2).Gen);         // Zuordnung unklar
        Assert.Null(StorageExpectation.UnambiguousNvmeLink([], 1).Gen);
    }

    [Fact]
    public void Drive_identity_roundtrip_and_legacy_notes()
    {
        Assert.Equal((double?)3, DiskRunIdentity.Encode("C:\\"));
        Assert.Equal((double?)4, DiskRunIdentity.Encode("d:"));
        Assert.Null(DiskRunIdentity.Encode("\\\\server\\share"));
        Assert.Equal((char?)'D', DiskRunIdentity.Decode(4));
        Assert.Null(DiskRunIdentity.Decode(27));
        Assert.Null(DiskRunIdentity.Decode(3.5));

        Assert.True(DiskRunIdentity.IsDrive(3, null, "C:\\"));
        Assert.False(DiskRunIdentity.IsDrive(4, "C:\\ · Testdatei", "C:\\")); // Kennung hat Vorrang
        Assert.True(DiskRunIdentity.IsDrive(null, "C:\\ · Testdatei 256 MB · ohne Windows-Cache", "C:\\"));
        Assert.False(DiskRunIdentity.IsDrive(null, "D:\\ · Testdatei 256 MB", "C:\\"));
        Assert.False(DiskRunIdentity.IsDrive(null, null, "C:\\"));
        Assert.Equal("D:\\", DiskRunIdentity.DriveText(null, "D:\\ · Testdatei"));
    }

    private static BenchmarkRun DiskRun(DateTime time, double seq, double iops, string note, string? taggedRoot = null)
    {
        var metrics = new List<BenchmarkMetric>
        {
            new(BenchmarkService.MetricDiskSeqRead, seq, "MB/s"),
            new(BenchmarkService.MetricDisk4kRead, iops, "IOPS"),
        };
        if (DiskRunIdentity.Encode(taggedRoot) is { } id) metrics.Add(new(DiskRunIdentity.MetricDriveId, id, ""));
        return new BenchmarkRun(BenchmarkService.KindDisk, time, 0, metrics, note);
    }

    [Fact]
    public void Scores_from_history_rate_the_system_drive_not_the_last_tested_drive()
    {
        var t = new DateTime(2026, 10, 1, 12, 0, 0);
        var history = new List<BenchmarkRun>
        {
            DiskRun(t.AddDays(2), 400, 9_595, "D:\\ · Testdatei 1 GB · ohne Windows-Cache"),       // zuletzt getestet, aber nicht C:
            DiskRun(t, 7_000, 16_000, "C:\\ · Testdatei 1 GB · ohne Windows-Cache"),              // älterer Lauf ohne Kennung
        };
        var s = CompleteBenchmarkService.ScoresFromHistory(history, "C:\\");
        Assert.Equal(PerformanceScore.StorageScore(7_000, 16_000), s.Storage);

        // Neuerer Lauf mit Kennung für C: hat Vorrang; ein mit D: gekennzeichneter Lauf zählt nie für C:.
        history.Add(DiskRun(t.AddDays(1), 6_000, 15_000, "C:\\ · Testdatei", "C:\\"));
        history.Add(DiskRun(t.AddDays(3), 300, 5_000, "C:\\ · irreführende Notiz", "D:\\"));
        s = CompleteBenchmarkService.ScoresFromHistory(history, "C:\\");
        Assert.Equal(PerformanceScore.StorageScore(6_000, 15_000), s.Storage);
        Assert.Equal(DiskRunIdentity.Letter("C:\\"), DiskRunIdentity.Letter(CompleteBenchmarkService.DriveOf(CompleteBenchmarkService.LastDiskRun(history, "C:\\")!)));
    }

    [Fact]
    public void Without_a_system_drive_run_storage_is_na()
    {
        var history = new List<BenchmarkRun> { DiskRun(DateTime.Now, 400, 9_595, "D:\\ · Testdatei 256 MB") };
        Assert.Null(CompleteBenchmarkService.ScoresFromHistory(history, "C:\\").Storage);
    }

    [Fact]
    public void Geekbench_runs_never_enter_the_ultimate_x_score()
    {
        var history = new List<BenchmarkRun>
        {
            new(Geekbench.Kind, DateTime.Now, 22_000,
                [new(Geekbench.MetricSingle, 3_000, "Punkte"), new(Geekbench.MetricMulti, 22_000, "Punkte"), new(Geekbench.MetricVersion, 6, "")]),
        };
        Assert.Equal(ComponentScores.Empty, CompleteBenchmarkService.ScoresFromHistory(history, "C:\\"));
    }
}

// ======================================================================== v1.3 Geekbench

public class V13GeekbenchTests
{
    [Fact]
    public void Valid_entry_with_thousands_separators()
    {
        Assert.True(Geekbench.TryParse(6, "3.012", "22 456", "", null, out var e, out _));
        Assert.Equal(new GeekbenchEntry(6, 3012, 22456, null, null), e);

        Assert.True(Geekbench.TryParse(6, "3012", "22456", "301,234", "vulkan", out e, out _));
        Assert.Equal((int?)301234, e!.GpuScore);
        Assert.Equal("Vulkan", e.GpuApi);
    }

    [Theory]
    [InlineData(7, "3000", "20000", null, null)]   // unbekannte Version
    [InlineData(6, "", "20000", null, null)]       // Single fehlt
    [InlineData(6, "3000", "abc", null, null)]     // Multi ungültig
    [InlineData(6, "-5", "20000", null, null)]     // negativ
    [InlineData(6, "3000", "2000", null, null)]    // Multi < Single
    [InlineData(6, "3000", "20000", "1234", null)] // GPU ohne API
    [InlineData(6, "3000", "20000", "1234", "CUDA")]
    [InlineData(6, "3000", "99999999", null, null)] // unplausibel groß
    public void Invalid_entries_are_rejected(int version, string single, string multi, string? gpu, string? api)
    {
        Assert.False(Geekbench.TryParse(version, single, multi, gpu, api, out var e, out var error));
        Assert.Null(e);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Change_percent()
    {
        Assert.Equal(10, Geekbench.ChangePercent(110, 100)!.Value, 6);
        Assert.Null(Geekbench.ChangePercent(110, null));
        Assert.Null(Geekbench.ChangePercent(110, 0));
    }

    [Fact]
    public void Launch_command_uses_fixed_program_files_path_without_parameters()
    {
        var spec = CommandCatalog.Build(AllowedCommand.GeekbenchLaunch); // darf auch ohne Installation nicht werfen
        Assert.Contains(spec.FilePath, CommandCatalog.GeekbenchCandidates);
        Assert.Empty(spec.Arguments);
        Assert.False(spec.RequiresElevation);
        Assert.True(spec.IsInteractive);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.All(CommandCatalog.GeekbenchCandidates, p => Assert.StartsWith(Path.Combine(pf, "Geekbench "), p, StringComparison.OrdinalIgnoreCase));
        Assert.Equal((int?)6, CommandCatalog.GeekbenchVersionOf(CommandCatalog.GeekbenchCandidates[0]));
        Assert.Equal((int?)5, CommandCatalog.GeekbenchVersionOf(CommandCatalog.GeekbenchCandidates[1]));
        Assert.Null(CommandCatalog.GeekbenchVersionOf(@"C:\Users\x\Downloads\Geekbench 6.exe"));
    }

    [Theory]
    [InlineData("--upload")]
    [InlineData("C:\\evil.exe")]
    [InlineData("")]
    public void Launch_command_rejects_any_parameter(string parameter)
        => Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.GeekbenchLaunch, parameter));
}
