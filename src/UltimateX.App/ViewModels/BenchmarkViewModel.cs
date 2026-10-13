using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Eine Benchmark-Kachel (CPU, RAM oder Datenträger) mit letztem Ergebnis und Vergleich zum vorherigen Lauf.</summary>
public sealed partial class BenchmarkCard(string kind, string title, string glyph, string description) : ObservableObject
{
    public string Kind { get; } = kind;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public string Description { get; } = description;

    [ObservableProperty] private string _score = "–";
    [ObservableProperty] private string? _comparison;
    [ObservableProperty] private string? _when;
    [ObservableProperty] private IReadOnlyList<BenchmarkMetric> _metrics = [];
    [ObservableProperty] private string? _note;

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
        Metrics = last.Metrics;
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

public sealed record SizeChoice(long Bytes, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Benchmarks (§103): CPU, RAM, Datenträger – lokal, ohne Fremdsoftware, Verlauf in SQLite.</summary>
public sealed partial class BenchmarkViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "Benchmarks";
    public override string Glyph => "";
    public override string Keywords => "benchmark leistung test cpu ram speicher ssd festplatte geschwindigkeit punkte score vergleich";
    public override string? Subtitle => "Leistung messen und mit früheren Läufen vergleichen";

    public BenchmarkCard Cpu { get; } = new(BenchmarkService.KindCpu, "Prozessor", "",
        "Ganzzahl-, Gleitkomma- und Krypto-Last auf einem Kern und auf allen Kernen (ca. 15 Sekunden).");
    public BenchmarkCard Memory { get; } = new(BenchmarkService.KindMemory, "Arbeitsspeicher", "",
        "Kopier- und Lesebandbreite sowie Zugriffslatenz (ca. 10 Sekunden).");
    public BenchmarkCard Disk { get; } = new(BenchmarkService.KindDisk, "Datenträger", "",
        "Sequentiell und 4K-Zufallszugriffe am Windows-Cache vorbei. Die Testdatei wird danach gelöscht.");

    public ObservableCollection<DiskChoice> Drives { get; } = [];
    public IReadOnlyList<SizeChoice> Sizes { get; } =
    [
        new(256L * 1024 * 1024, "256 MB (schnell)"),
        new(1024L * 1024 * 1024, "1 GB (genauer)"),
        new(4096L * 1024 * 1024, "4 GB (SSD-Cache überlisten)"),
    ];
    public ObservableCollection<BenchmarkHistoryRow> History { get; } = [];

    [ObservableProperty] private DiskChoice? _selectedDrive;
    [ObservableProperty] private SizeChoice? _selectedSize;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string? _runningStep;
    [ObservableProperty] private double _runningPercent;

    public BenchmarkViewModel()
    {
        SelectedSize = Sizes[1];
    }

    protected override async Task LoadAsync()
    {
        var targets = await Task.Run(() => BenchmarkService.GetDiskTargets());
        Drives.Clear();
        foreach (var t in targets) Drives.Add(new DiskChoice(t.Root, $"{t.Label} · {Format.Bytes(t.FreeBytes)} frei"));
        SelectedDrive ??= Drives.FirstOrDefault();
        await ShowHistoryAsync();
    }

    private async Task ShowHistoryAsync()
    {
        var runs = await Task.Run(() => AppServices.Benchmarks.History());
        foreach (var card in new[] { Cpu, Memory, Disk })
        {
            var mine = runs.Where(r => r.Kind == card.Kind).ToList(); // neueste zuerst
            card.Show(mine.ElementAtOrDefault(0), mine.ElementAtOrDefault(1));
        }
        History.Clear();
        foreach (var r in runs.Take(50))
            History.Add(new BenchmarkHistoryRow(Format.DateTimeText(r.Time), r.Kind, $"{r.Score:N0}",
                string.Join(" · ", r.Metrics.Take(3).Select(m => $"{m.Name}: {m.Value:N1} {m.Unit}"))));
    }

    [RelayCommand]
    private Task RunCpuAsync() => RunAsync("CPU-Benchmark", (p, ct) => AppServices.Benchmarks.RunCpu(p, ct),
        "Der Prozessor wird ca. 15 Sekunden voll ausgelastet. Lüfter können hörbar hochdrehen.");

    [RelayCommand]
    private Task RunMemoryAsync() => RunAsync("RAM-Benchmark", (p, ct) => AppServices.Benchmarks.RunMemory(p, ct),
        "Es werden kurzzeitig bis zu 512 MB Arbeitsspeicher belegt.");

    [RelayCommand]
    private Task RunDiskAsync()
    {
        if (SelectedDrive is null || SelectedSize is null) { StatusMessage = "Bitte Laufwerk und Testgröße wählen."; return Task.CompletedTask; }
        var root = SelectedDrive.Root;
        var size = SelectedSize.Bytes;
        return RunAsync("Datenträger-Benchmark", (p, ct) => AppServices.Benchmarks.RunDisk(root, size, p, ct),
            $"Auf {root} wird eine Testdatei mit {Format.Bytes((ulong)size)} geschrieben, gelesen und anschließend gelöscht. " +
            "Bei SSDs entspricht das einem winzigen Bruchteil der Lebensdauer.");
    }

    private async Task RunAsync(string title, Func<IProgress<BenchmarkProgress>, CancellationToken, BenchmarkRun> work, string explanation)
    {
        if (IsRunning) return;
        if (!Dialogs.Confirm(title, explanation + "\n\nFür genaue Werte andere Programme möglichst schließen. Starten?")) return;
        IsRunning = true;
        StatusMessage = null;
        _cts = new CancellationTokenSource();
        var progress = new Progress<BenchmarkProgress>(p => { RunningStep = p.Step; RunningPercent = p.Percent; });
        try
        {
            var token = _cts.Token;
            var run = await Task.Run(() => work(progress, token), token);
            StatusMessage = $"{title} abgeschlossen: {run.Score:N0} Punkte.";
            await ShowHistoryAsync();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"{title} abgebrochen.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OutOfMemoryException)
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

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
