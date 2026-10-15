using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Services.Network;

namespace UltimateX.Services.Benchmark;

public sealed record CompleteBenchmarkOptions(bool IncludeSpeedtest = false, bool IncludeStability = true, string? DiskRoot = null);

public sealed record CompleteStep(string Name, CheckStatus Status, string Detail);

public sealed record CompleteBenchmarkResult(
    PerformanceResult Performance,
    IReadOnlyList<CompleteStep> Steps,
    BenchmarkRun? Cpu,
    BenchmarkRun? Gpu,
    BenchmarkRun? Ram,
    BenchmarkRun? Disk,
    NetworkBenchmarkResult? Network,
    MemTestResult? Stability,
    BenchmarkRun Summary);

/// <summary>
/// START COMPLETE BENCHMARK (v1.2 §9): CPU → GPU → RAM → SSD/HDD → Network → Cooling → Stability,
/// danach ULTIMATE X PERFORMANCE SCORE mit Einstufung. Fällt ein Teil aus (z. B. kein Direct3D), wird er als N/A
/// geführt und fließt nicht in die Gesamtwertung ein.
/// </summary>
public sealed class CompleteBenchmarkService(BenchmarkService bench, NetworkBenchmarkService network, MemoryTestService memTest)
{
    public async Task<CompleteBenchmarkResult> RunAsync(CompleteBenchmarkOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var steps = new List<CompleteStep>();
        BenchmarkRun? cpu = null, gpu = null, ram = null, disk = null;
        NetworkBenchmarkResult? net = null;
        MemTestResult? stability = null;

        IProgress<BenchmarkProgress> Sub(string phase, double from, double to)
            => new SyncProgress<BenchmarkProgress>(p => progress?.Report(new BenchmarkProgress($"{phase}: {p.Step}", from + (to - from) * p.Percent / 100)));

        async Task<T?> Step<T>(string name, Func<Task<T>> work, Func<T, string> detail) where T : class
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var r = await work().ConfigureAwait(false);
                steps.Add(new CompleteStep(name, CheckStatus.Pass, detail(r)));
                return r;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or TimeoutException
                                           or DllNotFoundException or EntryPointNotFoundException or HttpRequestException or OutOfMemoryException
                                           or System.Runtime.InteropServices.COMException)
            {
                AppLog.Warn("CompleteBenchmark", $"{name} nicht möglich", ex.Message);
                steps.Add(new CompleteStep(name, CheckStatus.NotAvailable, "Nicht möglich: " + ex.Message));
                return null;
            }
        }

        cpu = await Step("CPU", () => Task.Run(() => bench.RunCpu(new CpuBenchmarkOptions(2, 30), Sub("CPU", 0, 25), ct), ct),
            r => $"{r.Metric(BenchmarkService.MetricSingle):N0} / {r.Metric(BenchmarkService.MetricMulti):N0} Punkte (1 Kern / alle Kerne)").ConfigureAwait(false);
        gpu = await Step("GPU", () => Task.Run(() => GpuBenchmarkService.Run(bench, options.IncludeStability ? 20 : 0, Sub("GPU", 25, 45), ct), ct),
            r => $"{r.Score:N0} GFLOPS").ConfigureAwait(false);
        ram = await Step("RAM", () => Task.Run(() => bench.RunMemory(Sub("RAM", 45, 52), ct), ct),
            r => $"{r.Metric(BenchmarkService.MetricRamRead):0.0} GB/s lesen · {r.Metric(BenchmarkService.MetricRamLatency):0.0} ns").ConfigureAwait(false);
        // v1.3: Gewertet wird immer das Windows-Systemlaufwerk (DiskRoot nur für Sonderfälle; ein anderes Laufwerk fließt nicht in den Score ein).
        var sysRoot = SystemDiskProbe.SystemRoot;
        var root = options.DiskRoot ?? sysRoot;
        var diskIsSystem = DiskRunIdentity.Letter(root) == DiskRunIdentity.Letter(sysRoot);
        disk = await Step("SSD/HDD", () => Task.Run(() => bench.RunDisk(root, DiskTestMode.Quick, Sub("Datenträger", 52, 65), ct), ct),
            r => $"{root} · {r.Metric(BenchmarkService.MetricDiskSeqRead):N0} MB/s seq. · {r.Metric(BenchmarkService.MetricDisk4kRead):N0} IOPS 4K"
                 + (diskIsSystem ? " · Systemlaufwerk" : " · nicht das Systemlaufwerk – fließt nicht in den Score ein")).ConfigureAwait(false);
        net = await Step("Network", () => network.RunAsync(options.IncludeSpeedtest, Sub("Netzwerk", 65, 80), ct),
            r => $"Network Health Score {r.Health.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}").ConfigureAwait(false);

        // Kühlung: Höchsttemperaturen und Drosselung aus den Lasttests von CPU und GPU.
        var cpuT = cpu?.Telemetry;
        var gpuT = gpu?.Telemetry;
        var throttled = cpuT?.CpuThrottled == true || gpuT?.GpuThrottled == true;
        var cooling = PerformanceScore.CoolingScore(cpuT?.CpuTempMax, gpuT?.GpuHotspotMax ?? gpuT?.GpuTempMax, throttled);
        steps.Add(new CompleteStep("Cooling", cooling is null ? CheckStatus.NotAvailable : ScoreMath.StatusOf(cooling),
            cooling is null ? "Keine Temperatursensoren verfügbar (siehe Seite „Sensoren“)."
                : $"CPU max. {cpuT?.CpuTempMax?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"} °C · GPU max. {gpuT?.GpuTempMax?.ToString("0", System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"} °C · Drosselung: {(throttled ? "erkannt" : "nicht erkannt")}"));

        if (options.IncludeStability)
        {
            progress?.Report(new BenchmarkProgress("Stability: RAM-Schnelltest", 82));
            stability = await Step("Stability", () => Task.Run(() => memTest.Run(MemTestMode.Quick,
                new SyncProgress<MemTestProgress>(p => progress?.Report(new BenchmarkProgress($"Stability: RAM {p.Phase} ({MemoryPatterns.Name(p.Pattern)})", 82 + 17 * p.Percent / 100))), ct), ct),
                r => $"RAM-Schnelltest: {r.Status.Label()} ({r.ErrorCount} Fehler)").ConfigureAwait(false);
            var gpuStable = gpu?.Metric("Stabilität: Abweichungen");
            if (stability is not null && (stability.Status == CheckStatus.Fail || gpuStable > 0))
                steps[^1] = steps[^1] with { Status = CheckStatus.Fail };
        }

        var scores = new ComponentScores(
            PerformanceScore.CpuScore(cpu?.Metric(BenchmarkService.MetricSingle), cpu?.Metric(BenchmarkService.MetricMulti)),
            PerformanceScore.GpuScore(gpu?.Metric("GPU Compute (FP32)"), gpu?.Metric("Speicherbandbreite")),
            PerformanceScore.RamScore(ram?.Metric(BenchmarkService.MetricRamRead), ram?.Metric(BenchmarkService.MetricRamLatency)),
            diskIsSystem ? PerformanceScore.StorageScore(disk?.Metric(BenchmarkService.MetricDiskSeqRead), disk?.Metric(BenchmarkService.MetricDisk4kRead)) : null,
            net?.Health.Score,
            cooling);
        var perf = PerformanceScore.Combine(scores);

        var metrics = new List<BenchmarkMetric>();
        void Add(string name, int? v) { if (v is { } x) metrics.Add(new BenchmarkMetric(name, x, "/100")); }
        Add("TOTAL", perf.Total);
        Add("CPU", scores.Cpu);
        Add("GPU", scores.Gpu);
        Add("RAM", scores.Ram);
        Add("SSD", scores.Storage);
        Add("Network", scores.Network);
        Add("Cooling", scores.Cooling);
        var summary = bench.Record(new BenchmarkRun(BenchmarkService.KindComplete, DateTime.Now, perf.Total ?? 0, metrics,
            $"ULTIMATE X PERFORMANCE SCORE · {perf.TierText} · Skala geschätzt (100 ≈ aktuelle Spitzenklasse)"));
        progress?.Report(new BenchmarkProgress("Fertig", 100));
        return new CompleteBenchmarkResult(perf, steps, cpu, gpu, ram, disk, net, stability, summary);
    }

    /// <summary>Teilwertungen aus dem Verlauf (jeweils letzter Lauf je Art) – für Bottleneck-/Upgrade-Analyse ohne neuen Test.</summary>
    public static ComponentScores ScoresFromHistory(IReadOnlyList<BenchmarkRun> history) => ScoresFromHistory(history, SystemDiskProbe.SystemRoot);

    /// <summary>
    /// Wie <see cref="ScoresFromHistory(IReadOnlyList{BenchmarkRun})"/>; die SSD-Wertung stammt immer vom letzten Lauf des
    /// Systemlaufwerks <paramref name="systemRoot"/> (nicht vom zuletzt getesteten Datenträger).
    /// </summary>
    public static ComponentScores ScoresFromHistory(IReadOnlyList<BenchmarkRun> history, string systemRoot)
    {
        BenchmarkRun? Last(string kind) => history.Where(r => r.Kind == kind).OrderByDescending(r => r.Time).FirstOrDefault();
        var cpu = Last(BenchmarkService.KindCpu);
        var gpu = Last(BenchmarkService.KindGpu);
        var ram = Last(BenchmarkService.KindMemory);
        var disk = LastDiskRun(history, systemRoot);
        var net = Last(BenchmarkService.KindNetwork);
        var complete = Last(BenchmarkService.KindComplete);
        return new ComponentScores(
            PerformanceScore.CpuScore(cpu?.Metric(BenchmarkService.MetricSingle), cpu?.Metric(BenchmarkService.MetricMulti)),
            PerformanceScore.GpuScore(gpu?.Metric("GPU Compute (FP32)"), gpu?.Metric("Speicherbandbreite")),
            PerformanceScore.RamScore(ram?.Metric(BenchmarkService.MetricRamRead), ram?.Metric(BenchmarkService.MetricRamLatency)),
            PerformanceScore.StorageScore(disk?.Metric(BenchmarkService.MetricDiskSeqRead), disk?.Metric(BenchmarkService.MetricDisk4kRead)),
            net?.Metric(NetworkBenchmarkService.MetricHealthScore) is { } ns ? (int)Math.Round(ns) : null,
            complete?.Metric("Cooling") is { } c ? (int)c : null);
    }

    /// <summary>
    /// Letzter Datenträger-Lauf eines Laufwerks. Läufe mit Kennung (<see cref="DiskRunIdentity.MetricDriveId"/>) werden über die
    /// Kennung zugeordnet; ältere Läufe ohne Kennung nur, wenn ihre Notiz mit dem Laufwerk beginnt („C:\ · Testdatei …“).
    /// </summary>
    public static BenchmarkRun? LastDiskRun(IReadOnlyList<BenchmarkRun> history, string root)
        => history.Where(r => r.Kind == BenchmarkService.KindDisk && DiskRunIdentity.IsDrive(r.Metric(DiskRunIdentity.MetricDriveId), r.Note, root))
                  .OrderByDescending(r => r.Time).FirstOrDefault();

    /// <summary>Anzeigename des Laufwerks eines Datenträger-Laufs („D:\“) oder null.</summary>
    public static string? DriveOf(BenchmarkRun run) => DiskRunIdentity.DriveText(run.Metric(DiskRunIdentity.MetricDriveId), run.Note);
}

/// <summary>
/// Leitet Fortschritt synchron weiter. Ein <see cref="Progress{T}"/> im Worker-Thread würde seine Rückrufe in den Threadpool
/// stellen – späte Meldungen kämen dann nach dem Ende des Laufs (und ungeordnet) in der Oberfläche an.
/// </summary>
internal sealed class SyncProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
