using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Eine Benchmark-Kachel (CPU, GPU, RAM, Datenträger, Netzwerk) mit letztem Ergebnis und Vergleich zum vorherigen Lauf.</summary>
public sealed partial class BenchmarkCard(string kind, string title, string glyph, string description, string unit = "Punkte") : ObservableObject
{
    public string Kind { get; } = kind;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string Description { get; } = description;
    public string Unit { get; } = unit;

    [ObservableProperty] private string _score = "–";
    [ObservableProperty] private string? _comparison;
    [ObservableProperty] private string? _when;
    [ObservableProperty] private IReadOnlyList<BenchmarkMetric> _metrics = [];
    [ObservableProperty] private string? _note;

    /// <summary>v1.3: Hinweis, welcher Lauf in den Performance Score eingeht (z. B. beim Datenträger das Systemlaufwerk).</summary>
    [ObservableProperty] private string? _scoreHint;

    /// <summary>Interne Kennzahlen, die nicht als Messwert angezeigt werden (Laufwerks-Kennung, Geekbench-Version).</summary>
    public static bool IsHiddenMetric(BenchmarkMetric m) => m.Name is DiskRunIdentity.MetricDriveId or Geekbench.MetricVersion;

    public void Show(BenchmarkRun? last, BenchmarkRun? previous)
    {
        if (last is null)
        {
            Score = "–";
            When = "Noch nicht getestet";
            Metrics = [];
            Comparison = null;
            Note = null;
            return;
        }
        Score = $"{last.Score:N0}";
        When = Format.DateTimeText(last.Time);
        Metrics = last.Metrics.Where(m => !IsHiddenMetric(m)).ToList();
        Note = last.Note;
        Comparison = previous is null || previous.Score <= 0 ? "Erster Lauf – Vergleich ab dem nächsten Test"
            : $"{(last.Score - previous.Score) / previous.Score * 100:+0;-0;0} % gegenüber {Format.DateText(previous.Time)}";
    }
}

public sealed record BenchmarkHistoryRow(string Date, string Kind, string Score, string Summary);

public sealed record DiskChoice(string Root, string Label)
{
    public override string ToString() => Label;
}

public sealed record DiskModeChoice(DiskTestMode Mode, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// COMPLETE BENCHMARK CENTER (v1.2 §2–§11): CPU, GPU, RAM, Datenträger, Netzwerk, Complete Benchmark mit
/// ULTIMATE X PERFORMANCE SCORE, Erwartet-vs-gemessen und Bottleneck-Analyse. Alles lokal, Verlauf in SQLite.
/// </summary>
public sealed partial class BenchmarkViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "Benchmarks";
    public override string Glyph => "";
    public override string Keywords => "benchmark leistung test cpu gpu grafik ram speicher ssd festplatte netzwerk geschwindigkeit punkte score vergleich performance bottleneck engpass complete";
    public override string? Subtitle => "Leistung messen, bewerten und mit früheren Läufen vergleichen";

    public BenchmarkCard Cpu { get; } = new(BenchmarkService.KindCpu, "Prozessor", "",
        "Ganzzahl, Gleitkomma, Krypto, Kompression, Dekompression, AES-Verschlüsselung und Rendering – je auf einem und auf allen Kernen.");
    public BenchmarkCard Gpu { get; } = new(BenchmarkService.KindGpu, "Grafikkarte", "",
        "Direct3D-11-Compute: FP32-Rechenleistung, Speicherbandbreite und optional Stabilität. Takt, Temperatur, Lüfter und Leistungsaufnahme aus den Sensoren.", "GFLOPS");
    public BenchmarkCard Memory { get; } = new(BenchmarkService.KindMemory, "Arbeitsspeicher", "",
        "Lesen, Schreiben, Kopieren und Zugriffslatenz (ca. 12 Sekunden).");
    public BenchmarkCard Disk { get; } = new(BenchmarkService.KindDisk, "Datenträger", "",
        "Sequentiell und 4K-Zufallszugriffe am Windows-Cache vorbei, IOPS und Zugriffszeit. Die Testdatei wird danach gelöscht.");
    public BenchmarkCard Net { get; } = new(BenchmarkService.KindNetwork, "Netzwerk", "",
        "Ping, Jitter, Paketverlust, DNS, Gateway, Verbindungsgeschwindigkeit, WLAN-Signal, IPv4/IPv6 – optional mit Speedtest.");

    /// <summary>v1.3: Geekbench (Primate Labs) – nur manuell eingetragene Ergebnisse, nicht Teil des ULTIMATE X PERFORMANCE SCORE.</summary>
    public BenchmarkCard GeekbenchResult { get; } = new(Geekbench.Kind, "Geekbench (Primate Labs)", "\uE945",
        "Fremdprogramm, wird nicht mitgeliefert. ULTIMATE X startet nur ein bereits installiertes Geekbench; die Ergebnisse trägst du hier selbst ein.",
        "Multi-Core-Punkte");

    public IReadOnlyList<int> GeekbenchVersions { get; } = [6, 5];
    public IReadOnlyList<string> GeekbenchGpuApis { get; } = Geekbench.GpuApis;

    public ObservableCollection<DiskChoice> Drives { get; } = [];
    public IReadOnlyList<DiskModeChoice> DiskModes { get; } =
    [
        new(DiskTestMode.Quick, "Quick (256 MB)"),
        new(DiskTestMode.Normal, "Normal (1 GB)"),
        new(DiskTestMode.Extended, "Extended (4 GB – nur wenn nötig)"),
    ];
    public ObservableCollection<BenchmarkHistoryRow> History { get; } = [];

    public ObservableCollection<ScoreRow> ComponentScores { get; } = [];
    public ObservableCollection<StepRow> CompleteSteps { get; } = [];
    public ObservableCollection<ExpectedRow> Expected { get; } = [];
    public ObservableCollection<Finding> Bottlenecks { get; } = [];

    [ObservableProperty] private DiskChoice? _selectedDrive;
    [ObservableProperty] private DiskModeChoice? _selectedDiskMode;
    [ObservableProperty] private bool _cpuSustained;
    [ObservableProperty] private bool _gpuStability = true;
    [ObservableProperty] private bool _networkSpeedtest;
    [ObservableProperty] private bool _completeSpeedtest;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _runningStep;
    [ObservableProperty] private double _runningPercent;

    [ObservableProperty] private string _totalScore = "–";
    [ObservableProperty] private string _tierText = "Noch kein Complete Benchmark";
    [ObservableProperty] private DataState _totalState = DataState.NotAvailable;
    [ObservableProperty] private string? _completeWhen;
    [ObservableProperty] private string? _bottleneckSummary;

    /// <summary>v1.3: Welcher Datenträger-Lauf in die SSD-Wertung eingeht (immer das Systemlaufwerk).</summary>
    [ObservableProperty] private string? _storageScoreSource;

    [ObservableProperty] private bool _geekbenchInstalled;
    [ObservableProperty] private string _geekbenchStatus = "Prüfe, ob Geekbench installiert ist …";
    [ObservableProperty] private int _geekbenchVersion = 6;
    [ObservableProperty] private string? _geekbenchSingle;
    [ObservableProperty] private string? _geekbenchMulti;
    [ObservableProperty] private string? _geekbenchGpu;
    [ObservableProperty] private string _geekbenchGpuApi = "OpenCL";

    public BenchmarkViewModel()
    {
        SelectedDiskMode = DiskModes[1];
    }

    protected override async Task LoadAsync()
    {
        var targets = await Task.Run(() => BenchmarkService.GetDiskTargets());
        Drives.Clear();
        var sysLetter = DiskRunIdentity.Letter(SystemDiskProbe.SystemRoot);
        foreach (var t in targets)
            Drives.Add(new DiskChoice(t.Root, $"{t.Label} · {Format.Bytes(t.FreeBytes)} frei" + (DiskRunIdentity.Letter(t.Root) == sysLetter ? " · Systemlaufwerk" : "")));
        SelectedDrive = Drives.FirstOrDefault(d => d.Root == SelectedDrive?.Root)
                        ?? Drives.FirstOrDefault(d => DiskRunIdentity.Letter(d.Root) == sysLetter) ?? Drives.FirstOrDefault();
        DetectGeekbench();
        await ShowHistoryAsync();
    }

    private async Task ShowHistoryAsync()
    {
        var runs = await Task.Run(() => AppServices.Benchmarks.History());
        foreach (var card in new[] { Cpu, Gpu, Memory, Net })
        {
            var mine = runs.Where(r => r.Kind == card.Kind).ToList(); // neueste zuerst
            card.Show(mine.ElementAtOrDefault(0), mine.ElementAtOrDefault(1));
        }
        ShowDiskCard(runs);
        ShowGeekbenchCard(runs);
        History.Clear();
        foreach (var r in runs.Take(60))
            History.Add(new BenchmarkHistoryRow(Format.DateTimeText(r.Time), r.Kind, $"{r.Score:N0}",
                (r.Kind == BenchmarkService.KindDisk && CompleteBenchmarkService.DriveOf(r) is { } drive ? drive + " · " : "") +
                string.Join(" · ", r.Metrics.Where(m => !BenchmarkCard.IsHiddenMetric(m)).Take(4).Select(m => $"{m.Name}: {m.Value:N1} {m.Unit}"))));

        var complete = runs.FirstOrDefault(r => r.Kind == BenchmarkService.KindComplete);
        var scores = CompleteBenchmarkService.ScoresFromHistory(runs);
        ShowScores(scores, complete);
        await AnalyzeAsync(scores);
    }

    /// <summary>
    /// Datenträger-Karte: zeigt den zuletzt getesteten Datenträger (Vergleich nur mit früheren Läufen desselben Laufwerks)
    /// und deutlich, dass in den Performance Score immer das Systemlaufwerk eingeht.
    /// </summary>
    private void ShowDiskCard(IReadOnlyList<BenchmarkRun> runs)
    {
        var sysRoot = SystemDiskProbe.SystemRoot;
        var disks = runs.Where(r => r.Kind == BenchmarkService.KindDisk).OrderByDescending(r => r.Time).ToList();
        var last = disks.FirstOrDefault();
        var lastDrive = last is null ? null : CompleteBenchmarkService.DriveOf(last);
        var previous = last is null || lastDrive is null ? null
            : disks.Skip(1).FirstOrDefault(r => DiskRunIdentity.IsDrive(r.Metric(DiskRunIdentity.MetricDriveId), r.Note, lastDrive));
        Disk.Show(last, previous);

        var sysRun = CompleteBenchmarkService.LastDiskRun(runs, sysRoot);
        if (sysRun is null)
        {
            Disk.ScoreHint = Loc.F("Performance Score: gewertet wird nur das Systemlaufwerk {0} – es wurde noch nicht getestet (SSD-Wertung N/A).", sysRoot);
            StorageScoreSource = Loc.F("SSD-Wertung: Systemlaufwerk {0} – noch nicht getestet.", sysRoot);
        }
        else
        {
            var seqText = sysRun.Metric(BenchmarkService.MetricDiskSeqRead) is { } v ? $"{v:N0} MB/s" : "N/A";
            Disk.ScoreHint = ReferenceEquals(sysRun, last)
                ? Loc.F("Performance Score: dieser Lauf des Systemlaufwerks {0} wird gewertet.", sysRoot)
                : Loc.F("Angezeigt: letzter Test von {0}. Performance Score: gewertet wird das Systemlaufwerk {1} (Test vom {2}, {3}).",
                    lastDrive ?? "?", sysRoot, Format.DateTimeText(sysRun.Time), seqText);
            StorageScoreSource = Loc.F("SSD-Wertung: Systemlaufwerk {0} (Test vom {1}).", sysRoot, Format.DateTimeText(sysRun.Time));
        }
    }

    /// <summary>Geekbench-Karte: letzter Wert, Datum und Vergleich zum vorherigen Lauf derselben Hauptversion.</summary>
    private void ShowGeekbenchCard(IReadOnlyList<BenchmarkRun> runs)
    {
        var mine = runs.Where(r => r.Kind == Geekbench.Kind).OrderByDescending(r => r.Time).ToList();
        var last = mine.FirstOrDefault();
        var version = last?.Metric(Geekbench.MetricVersion);
        var previous = last is null ? null : mine.Skip(1).FirstOrDefault(r => r.Metric(Geekbench.MetricVersion) == version);
        GeekbenchResult.Show(last, previous);
        if (last is not null && previous is null && mine.Count > 1)
            GeekbenchResult.Comparison = "Kein Vergleich: frühere Ergebnisse stammen von einer anderen Geekbench-Version (andere Skala).";
    }

    private void ShowScores(ComponentScores s, BenchmarkRun? complete)
    {
        var perf = PerformanceScore.Combine(s);
        ComponentScores.Clear();
        ComponentScores.Add(new ScoreRow("CPU", s.Cpu));
        ComponentScores.Add(new ScoreRow("GPU", s.Gpu));
        ComponentScores.Add(new ScoreRow("RAM", s.Ram));
        ComponentScores.Add(new ScoreRow("SSD", s.Storage));
        ComponentScores.Add(new ScoreRow("Network", s.Network));
        ComponentScores.Add(new ScoreRow("Cooling", s.Cooling));
        TotalScore = perf.Total is { } t ? $"{t} / 100" : "–";
        TierText = perf.Tier is null ? (perf.Total is null ? "Noch kein Complete Benchmark" : "Einstufung erst mit CPU- und GPU-Wertung") : perf.TierText;
        TotalState = perf.Total is null ? DataState.NotAvailable : ScoreMath.StatusOf(perf.Total).ToDataState();
        CompleteWhen = complete is null ? null : "Letzter Complete Benchmark: " + Format.DateTimeText(complete.Time);
    }

    /// <summary>Erwartet vs. gemessen (§10) und Bottleneck-Analyse (§11) aus den letzten Messungen.</summary>
    private async Task AnalyzeAsync(ComponentScores scores)
    {
        CollectedFacts facts;
        try
        {
            facts = await FactsCollector.CollectAsync(includeProcesses: false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            BottleneckSummary = "Analyse nicht möglich: " + ex.Message;
            return;
        }

        var report = BottleneckAnalyzer.Analyze(facts.Facts with { Scores = scores });
        BottleneckSummary = report.Summary;
        Bottlenecks.Clear();
        foreach (var f in report.Findings) Bottlenecks.Add(f);

        Expected.Clear();
        var gpuRun = facts.History.FirstOrDefault(r => r.Kind == BenchmarkService.KindGpu);
        var spec = HardwareDatabase.FindGpu(facts.PrimaryGpuName);
        if (spec is null)
            Expected.Add(new ExpectedRow("GPU", "N/A", gpuRun is null ? "–" : $"{gpuRun.Score:N0} GFLOPS", "–", DataState.NotAvailable,
                $"{facts.PrimaryGpuName ?? "GPU"} ist nicht in der lokalen Datenbank – keine Erwartung möglich."));
        else
        {
            var expected = spec.Fp32Tflops * 1000 * spec.ExpectedComputeEfficiency;
            Expected.Add(Compare($"GPU ({spec.Name} detected)", expected, gpuRun?.Score, "GFLOPS", BenchmarkCauses.For(BenchComponent.Gpu, facts.Facts),
                $"Erwartet: {spec.Fp32Tflops:0.#} TFLOPS laut Hersteller × {spec.ExpectedComputeEfficiency:P0} typische Auslastung (ESTIMATED)"));
        }

        var ramRun = facts.History.FirstOrDefault(r => r.Kind == BenchmarkService.KindMemory);
        var theo = MemoryAnalysis.TheoreticalBandwidthGbs(facts.Channels.Channels, facts.RamSpeed.CurrentMTs);
        if (theo is { } th)
            Expected.Add(Compare($"RAM ({facts.Channels.Text}, {facts.RamSpeed.CurrentMTs} MT/s)", th * 0.75, ramRun?.Metric(BenchmarkService.MetricRamRead), "GB/s",
                BenchmarkCauses.For(BenchComponent.Ram, facts.Facts),
                $"Theoretisch {th:0.#} GB/s, typisch erreichbar ca. 75 % (ESTIMATED)"));
        else
            Expected.Add(new ExpectedRow("RAM", "N/A", ramRun?.Metric(BenchmarkService.MetricRamRead) is { } r ? $"{r:0.0} GB/s" : "–", "–", DataState.NotAvailable,
                "Kanalanzahl oder Takt nicht ermittelbar."));

        // v1.3: Datenträger – immer das Systemlaufwerk (wie im Performance Score). Erwartung aus Bustyp und – bei NVMe – der
        // PCIe-Generation, nur wenn eindeutig ermittelbar; sonst N/A.
        var sd = facts.SystemDisk;
        var sysRoot = sd?.Root ?? SystemDiskProbe.SystemRoot;
        var diskRun = CompleteBenchmarkService.LastDiskRun(facts.History, sysRoot);
        var diskExpected = sd is null ? null : StorageExpectation.ExpectedSeqReadMbs(sd.BusType, sd.MediaType, sd.PcieGen, sd.PcieWidth);
        var diskCauses = BenchmarkCauses.For(BenchComponent.Storage, facts.Facts,
            new StorageCauseFacts(MeasuredDriveIsSystem: true, sd?.BusType, sd?.FreePercent, facts.StorageMaxTempC));
        if (diskExpected is { } de && sd is not null)
            Expected.Add(Compare($"Datenträger {sysRoot} ({sd.Text})", de, diskRun?.Metric(BenchmarkService.MetricDiskSeqRead), "MB/s", diskCauses,
                sd.BusType == "NVMe"
                    ? $"Typischer Wert für NVMe PCIe {sd.PcieGen}.0 x{sd.PcieWidth ?? 4} (ESTIMATED) · gewertet wird das Systemlaufwerk"
                    : "Typischer Wert der Schnittstelle (ESTIMATED) · gewertet wird das Systemlaufwerk"));
        else
            Expected.Add(new ExpectedRow($"Datenträger {sysRoot}" + (sd?.DiskName is { } dn ? $" ({dn})" : ""), "N/A",
                diskRun?.Metric(BenchmarkService.MetricDiskSeqRead) is { } sr ? $"{sr:N0} MB/s" : "–", "–", DataState.NotAvailable,
                sd?.BusType == "NVMe"
                    ? "PCIe-Generation des NVMe-Controllers nicht eindeutig ermittelbar – keine verlässliche Erwartung."
                    : "Bustyp des Systemlaufwerks nicht eindeutig ermittelbar – keine verlässliche Erwartung."));
        Expected.Add(new ExpectedRow("CPU", "N/A", facts.History.FirstOrDefault(r => r.Kind == BenchmarkService.KindCpu) is { } c ? $"{c.Score:N0} Punkte" : "–", "–",
            DataState.NotAvailable, "Für CPUs liegen keine geprüften Referenzwerte dieses Tests vor – Vergleich nur mit eigenen früheren Läufen."));
    }

    private static ExpectedRow Compare(string component, double expected, double? measured, string unit, CauseList causes, string basis)
    {
        if (measured is not { } m || m <= 0)
            return new ExpectedRow(component, $"{expected:N0} {unit}", "noch nicht gemessen", "–", DataState.NotAvailable, basis);
        var dev = (m - expected) / expected * 100;
        var state = dev >= -10 ? DataState.Ok : dev >= -25 ? DataState.Warning : DataState.Problem;
        var text = dev >= 0 ? $"{dev:0}% above expected" : $"{-dev:0}% below expected";
        return new ExpectedRow(component, $"{expected:N0} {unit}", $"{m:N0} {unit}", text, state,
            basis + (dev < -10 ? "\n" + FormatCauses(causes) : ""));
    }

    /// <summary>Ursachen je Komponente (v1.3) – jedes Element wird vor dem Zusammenfügen einzeln übersetzt.</summary>
    private static string FormatCauses(CauseList causes)
    {
        var items = string.Join(", ", causes.Items.Select(Loc.T));
        return causes.Detected
            ? Loc.F("Mögliche Ursachen: {0}", items)
            : Loc.F("Mögliche Ursachen – nichts Konkretes erkannt, bitte prüfen: {0}", items);
    }

    // ---------------------------------------------------------------- Einzeltests

    [RelayCommand]
    private Task RunCpuAsync() => RunAsync("CPU-Benchmark", (p, ct) => AppServices.Benchmarks.RunCpu(new CpuBenchmarkOptions(2, CpuSustained ? 120 : 0), p, ct),
        CpuSustained
            ? "Der Prozessor wird ca. 2½ Minuten voll ausgelastet (inkl. 2 Minuten Dauerlast zur Erkennung von Drosselung). Lüfter können hörbar hochdrehen."
            : "Der Prozessor wird ca. 30 Sekunden voll ausgelastet. Lüfter können hörbar hochdrehen.");

    [RelayCommand]
    private Task RunGpuAsync() => RunAsync("GPU-Benchmark", (p, ct) => GpuBenchmarkService.Run(AppServices.Benchmarks, GpuStability ? 60 : 0, p, ct),
        "Die Grafikkarte wird über Direct3D-11-Compute-Shader ausgelastet" + (GpuStability ? " (inkl. 60 Sekunden Stabilitätstest)" : "") +
        ". Es werden bis zu 512 MB Grafikspeicher belegt. Bildschirmausgabe und andere 3D-Programme können kurz ruckeln.");

    [RelayCommand]
    private Task RunMemoryAsync() => RunAsync("RAM-Benchmark", (p, ct) => AppServices.Benchmarks.RunMemory(p, ct),
        "Es werden kurzzeitig bis zu 512 MB Arbeitsspeicher belegt. Für einen Fehlertest des RAM gibt es die Seite „Memory Test“.");

    [RelayCommand]
    private Task RunDiskAsync()
    {
        if (SelectedDrive is null || SelectedDiskMode is null) { StatusMessage = "Bitte Laufwerk und Testmodus wählen."; return Task.CompletedTask; }
        var root = SelectedDrive.Root;
        var mode = SelectedDiskMode.Mode;
        var size = BenchmarkService.DiskModeParameters(mode).SizeBytes;
        return RunAsync("Datenträger-Benchmark", (p, ct) => AppServices.Benchmarks.RunDisk(root, mode, p, ct),
            $"Auf {root} wird eine Testdatei mit {Format.Bytes((ulong)size)} geschrieben, gelesen und anschließend gelöscht. " +
            (mode == DiskTestMode.Extended ? "Der Extended-Modus schreibt deutlich mehr Daten – auf SSDs nur bei Bedarf verwenden." : "Bei SSDs entspricht das einem winzigen Bruchteil der Lebensdauer."));
    }

    [RelayCommand]
    private Task RunNetworkAsync()
    {
        var speed = NetworkSpeedtest;
        return RunAsyncTask("Netzwerk-Benchmark", async (p, ct) => (await AppServices.NetworkBenchmark.RunAsync(speed, p, ct)).Run,
            speed ? "Ping, DNS, Gateway und Speedtest (Testdaten werden mit speed.cloudflare.com ausgetauscht, ca. 50–200 MB)."
                  : "Ping, DNS, Gateway, Adapter und IPv4/IPv6 – ohne Speedtest (Download/Upload werden als N/A geführt).");
    }

    private Task RunAsync(string title, Func<IProgress<BenchmarkProgress>, CancellationToken, BenchmarkRun> work, string explanation)
        => RunAsyncTask(title, (p, ct) => Task.Run(() => work(p, ct), ct), explanation);

    private async Task RunAsyncTask(string title, Func<IProgress<BenchmarkProgress>, CancellationToken, Task<BenchmarkRun>> work, string explanation)
    {
        if (IsRunning) return;
        if (!Dialogs.Confirm(title, explanation + "\n\nFür genaue Werte andere Programme möglichst schließen. Starten?")) return;
        IsRunning = true;
        StatusMessage = null;
        _cts = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p => { RunningStep = p.Step; RunningPercent = p.Percent; });
        try
        {
            var run = await work(progress, _cts.Token);
            StatusMessage = $"{title} abgeschlossen: {run.Score:N0} {UnitFor(run.Kind)}.";
            await ShowHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"{title} abgebrochen.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OutOfMemoryException
                                       or TimeoutException or HttpRequestException or DllNotFoundException or EntryPointNotFoundException)
        {
            StatusMessage = $"{title} nicht möglich: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
            RunningStep = null;
            RunningPercent = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    private static string UnitFor(string kind) => kind switch
    {
        BenchmarkService.KindGpu => "GFLOPS",
        BenchmarkService.KindNetwork => "Punkte (Network Health Score)",
        _ => "Punkte",
    };

    // ---------------------------------------------------------------- Complete Benchmark (§9)

    [RelayCommand]
    private async Task RunCompleteAsync()
    {
        if (IsRunning) return;
        // v1.3: Der Complete Benchmark testet immer das Systemlaufwerk (nur dieses fließt in die SSD-Wertung ein).
        var diskRoot = SystemDiskProbe.SystemRoot;
        if (!Dialogs.Confirm("START COMPLETE BENCHMARK",
                "Ablauf: CPU → GPU → RAM → SSD/HDD → Network → Cooling → Stability (RAM-Schnelltest).\n\n" +
                $"Dauer: ca. 5–10 Minuten. CPU und GPU werden voll ausgelastet, auf dem Systemlaufwerk {diskRoot} wird kurz eine 256-MB-Testdatei geschrieben, " +
                "für den RAM-Schnelltest wird ein Viertel des freien Arbeitsspeichers belegt." +
                (CompleteSpeedtest ? "\nDer Speedtest überträgt Testdaten zu speed.cloudflare.com." : "") +
                "\n\nAndere Programme möglichst schließen. Starten?")) return;

        IsRunning = true;
        StatusMessage = null;
        CompleteSteps.Clear();
        foreach (var n in new[] { "CPU", "GPU", "RAM", "SSD/HDD", "Network", "Cooling", "Stability" }) CompleteSteps.Add(new StepRow(n));
        _cts = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p =>
        {
            if (!IsRunning) return; // verspätete Meldung nach Laufende ignorieren
            RunningStep = p.Step;
            RunningPercent = p.Percent;
            var phase = p.Step.Split(':')[0];
            var key = phase switch { "Datenträger" => "SSD/HDD", "Netzwerk" => "Network", "Stability" => "Stability", _ => phase };
            foreach (var s in CompleteSteps) s.IsRunning = s.Name == key;
        });
        try
        {
            var r = await AppServices.CompleteBenchmark.RunAsync(new CompleteBenchmarkOptions(CompleteSpeedtest, true), progress, _cts.Token);
            foreach (var step in r.Steps)
            {
                var row = CompleteSteps.FirstOrDefault(s => s.Name == step.Name);
                if (row is null) continue;
                row.IsRunning = false;
                row.Status = step.Status;
                row.Detail = step.Detail;
            }
            StatusMessage = $"ULTIMATE X PERFORMANCE SCORE: {r.Performance.Total?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? "N/A"} / 100 · {r.Performance.TierText}";
            await ShowHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Complete Benchmark abgebrochen.";
        }
        finally
        {
            foreach (var s in CompleteSteps) s.IsRunning = false;
            IsRunning = false;
            RunningStep = null;
            RunningPercent = 0;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ---------------------------------------------------------------- Geekbench (v1.3)

    private static readonly Uri GeekbenchDownloadPage = new("https://www.geekbench.com/download/");

    private void DetectGeekbench()
    {
        var path = CommandCatalog.FindInstalledGeekbench();
        GeekbenchInstalled = path is not null;
        if (CommandCatalog.GeekbenchVersionOf(path) is { } v) GeekbenchVersion = v;
        GeekbenchStatus = path is null
            ? "Geekbench ist nicht installiert (gesucht unter „Programme\\Geekbench 6“ und „Programme\\Geekbench 5“)."
            : Loc.F("Installiert: {0}", path);
    }

    /// <summary>Startet ein bereits installiertes Geekbench (ohne Adminrechte, eigenes Fenster). Nichts wird heruntergeladen.</summary>
    [RelayCommand]
    private void LaunchGeekbench()
    {
        DetectGeekbench();
        var spec = CommandCatalog.Build(AllowedCommand.GeekbenchLaunch);
        if (!GeekbenchInstalled || !File.Exists(spec.FilePath))
        {
            StatusMessage = "Geekbench ist nicht installiert. Über „Offizielle Download-Seite öffnen“ kannst du es selbst bei Primate Labs herunterladen.";
            return;
        }
        bool started;
        try
        {
            started = ProcessRunner.StartDetached(spec);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Geekbench konnte nicht gestartet werden: " + ex.Message;
            return;
        }
        StatusMessage = started
            ? "Geekbench wurde gestartet. Nach dem Test die Werte unten eintragen und speichern."
            : "Geekbench konnte nicht gestartet werden.";
    }

    [RelayCommand]
    private void OpenGeekbenchDownload() => ShowResult(UriLauncher.Open(GeekbenchDownloadPage));

    /// <summary>Speichert ein manuell eingetragenes Geekbench-Ergebnis im Verlauf (fließt nicht in den ULTIMATE X Score ein).</summary>
    [RelayCommand]
    private async Task SaveGeekbenchAsync()
    {
        if (!Geekbench.TryParse(GeekbenchVersion, GeekbenchSingle, GeekbenchMulti, GeekbenchGpu, GeekbenchGpuApi, out var entry, out var error) || entry is null)
        {
            StatusMessage = error;
            return;
        }
        var metrics = new List<BenchmarkMetric>
        {
            new(Geekbench.MetricSingle, entry.SingleCore, "Punkte"),
            new(Geekbench.MetricMulti, entry.MultiCore, "Punkte"),
        };
        if (entry.GpuScore is { } g) metrics.Add(new(Geekbench.MetricGpu, g, $"Punkte ({entry.GpuApi})"));
        metrics.Add(new(Geekbench.MetricVersion, entry.Version, ""));
        var run = new BenchmarkRun(Geekbench.Kind, DateTime.Now, entry.MultiCore, metrics,
            $"Geekbench {entry.Version} · manuell eingetragen · nicht Teil des ULTIMATE X PERFORMANCE SCORE");
        try
        {
            await Task.Run(() => AppServices.Benchmarks.Record(run));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusMessage = "Geekbench-Ergebnis konnte nicht gespeichert werden: " + ex.Message;
            return;
        }
        GeekbenchSingle = GeekbenchMulti = GeekbenchGpu = null;
        StatusMessage = Loc.F("Geekbench-Ergebnis gespeichert: {0} Single-Core / {1} Multi-Core.", entry.SingleCore, entry.MultiCore);
        await ShowHistoryAsync();
    }
}
