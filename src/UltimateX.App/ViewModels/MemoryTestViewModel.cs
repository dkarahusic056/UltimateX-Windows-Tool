using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

public sealed record MemErrorRow(string Address, string Pattern, int Pass, string Expected, string Actual, int Bits);

public sealed record MemTestModeChoice(MemTestMode Mode, string Label, string Description)
{
    public override string ToString() => Label;
}

/// <summary>
/// RAM STABILITY TEST (v1.2 §5): Quick / Standard / Extended innerhalb von Windows und Boot-Test über die
/// Windows-Speicherdiagnose. Getrennt vom RAM-Benchmark (Seite „Benchmarks“).
/// </summary>
public sealed partial class MemoryTestViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "Memory Test";
    public override string Glyph => "";
    public override string Keywords => "ram test speicher fehler stabilität memtest memory arbeitsspeicher defekt xmp expo boot speicherdiagnose";
    public override string? Subtitle => "Arbeitsspeicher auf Fehler prüfen";

    public IReadOnlyList<MemTestModeChoice> Modes { get; } =
    [
        new(MemTestMode.Quick, "Quick Memory Test", "25 % des freien Speichers · 3 Muster · 1 Durchlauf · ca. 1–3 Minuten"),
        new(MemTestMode.Standard, "Standard Memory Test", "50 % des freien Speichers · 8 Muster · 2 Durchläufe · ca. 5–15 Minuten"),
        new(MemTestMode.Extended, "Extended Memory Test", "70 % des freien Speichers · 8 Muster · 4 Durchläufe · ca. 15–45 Minuten"),
    ];

    public ObservableCollection<MemErrorRow> Errors { get; } = [];
    public ObservableCollection<InfoItem> Configuration { get; } = [];
    public ObservableCollection<InfoItem> BootResults { get; } = [];

    [ObservableProperty] private MemTestModeChoice? _selectedMode;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _phase = "–";
    [ObservableProperty] private string _passText = "–";
    [ObservableProperty] private string _patternText = "–";
    [ObservableProperty] private string _testedText = "–";
    [ObservableProperty] private string _errorText = "–";
    [ObservableProperty] private string _durationText = "–";
    [ObservableProperty] private string _resultText = "Noch kein Test";
    [ObservableProperty] private DataState _resultState = DataState.NotAvailable;
    [ObservableProperty] private string? _resultNote;

    public MemoryTestViewModel()
    {
        SelectedMode = Modes[0];
    }

    protected override async Task LoadAsync()
    {
        var (modules, boot) = await Task.Run(() => (AppServices.SystemInfo.GetRamModules(), MemoryTestService.GetBootTestResults()));
        Configuration.Clear();
        var populated = modules.Where(m => m.CapacityBytes is > 0).ToList();
        var total = populated.Aggregate(0UL, (a, m) => a + (m.CapacityBytes ?? 0));
        Configuration.Add(new InfoItem("Gesamtkapazität", Format.Bytes(total)));
        Configuration.Add(new InfoItem("Module", populated.Count.ToString(Loc())));
        Configuration.Add(new InfoItem("Typ", MemoryAnalysis.TypeName(populated.FirstOrDefault()?.SmbiosMemoryType)));
        var speed = MemoryAnalysis.CheckSpeed(modules);
        Configuration.Add(new InfoItem("Takt", speed.Message, speed.Status.ToDataState()));
        BootResults.Clear();
        foreach (var b in boot)
            BootResults.Add(new InfoItem(Format.DateTimeText(b.Time), b.Message, b.ErrorsFound ? DataState.Problem : DataState.Ok));
        if (BootResults.Count == 0) BootResults.Add(InfoItem.Unavailable("Boot Memory Test", "Noch kein Ergebnis der Windows-Speicherdiagnose vorhanden."));
    }

    private static System.Globalization.CultureInfo Loc() => Core.Localization.Loc.Culture;

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning || SelectedMode is null) return;
        if (!Dialogs.Confirm(SelectedMode.Label,
                $"{SelectedMode.Description}.\n\nDer Test belegt einen großen Teil des freien Arbeitsspeichers und lastet alle Kerne aus. " +
                "Andere Programme können währenddessen langsamer reagieren. Bitte vorher Arbeit speichern.\n\nStarten?")) return;

        IsRunning = true;
        StatusMessage = null;
        Errors.Clear();
        ResultText = "Läuft …";
        ResultState = DataState.NotAvailable;
        ResultNote = null;
        _cts = new CancellationTokenSource();
        var mode = SelectedMode.Mode;
        var progress = new Progress<MemTestProgress>(p =>
        {
            Percent = p.Percent;
            Phase = p.Phase;
            PassText = $"{p.Pass} / {p.Passes}";
            PatternText = MemoryPatterns.Name(p.Pattern);
            TestedText = Format.Bytes(p.TestedBytes);
            ErrorText = p.ErrorCount.ToString(Loc());
        });
        try
        {
            var r = await Task.Run(() => AppServices.MemoryTest.Run(mode, progress, _cts.Token));
            Show(r);
            AppServices.Benchmarks.Record(new BenchmarkRun(BenchmarkService.KindMemTest, r.Started, r.ErrorCount,
            [
                new BenchmarkMetric("Fehler", r.ErrorCount, ""),
                new BenchmarkMetric("Durchläufe", r.PassesCompleted, ""),
                new BenchmarkMetric("Getesteter Speicher", Math.Round(r.TestedBytes / 1024.0 / 1024 / 1024, 1), "GB"),
                new BenchmarkMetric("Testdauer", Math.Round(r.Duration.TotalMinutes, 1), "min"),
            ], $"{r.Mode} · {r.Status.Label()}"));
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            ResultText = "Nicht möglich";
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Show(MemTestResult r)
    {
        ResultText = r.Status.Label();
        ResultState = r.Status.ToDataState();
        PassText = $"{r.PassesCompleted} / {r.PassesPlanned}";
        TestedText = $"{Format.Bytes(r.TestedBytes)} ({r.TestedPercent:0} % des RAM)";
        ErrorText = r.ErrorCount.ToString(Loc());
        DurationText = Format.Duration(r.Duration);
        PatternText = string.Join(", ", r.Patterns.Select(MemoryPatterns.Name));
        Percent = r.Completed ? 100 : Percent;
        ResultNote = (r.Completed ? "" : "Test abgebrochen – Ergebnis unvollständig. ")
                     + (r.Status == CheckStatus.Fail
                         ? "Es wurden Speicherfehler gefunden. Mögliche Ursachen: defektes Modul, zu hoher Takt/XMP-Profil, Übertaktung, Spannung. Module einzeln testen und Boot-Test ausführen. "
                         : r.Status == CheckStatus.Warning ? "Ein einzelner Bitfehler – Test wiederholen (Extended) und Boot-Test ausführen. " : "")
                     + r.Note;
        Errors.Clear();
        foreach (var e in r.Errors.Take(200))
            Errors.Add(new MemErrorRow($"0x{e.Address:X12}", MemoryPatterns.Name(e.Pattern), e.Pass, $"0x{e.Expected:X16}", $"0x{e.Actual:X16}", e.FlippedBits));
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Boot Memory Test: plant die Windows-Speicherdiagnose (mdsched) – Test läuft beim nächsten Neustart außerhalb von Windows.</summary>
    [RelayCommand]
    private async Task BootTestAsync()
    {
        await AppServices.Guard.RunAsync("Boot Memory Test",
            "Die Windows-Speicherdiagnose (mdsched.exe) prüft den Arbeitsspeicher beim nächsten Neustart außerhalb des laufenden Windows – " +
            "dabei kann nahezu der gesamte RAM getestet werden (Prinzip wie MemTest86). Im folgenden Windows-Dialog wählen Sie „Jetzt neu starten“ oder " +
            "„Beim nächsten Start prüfen“. Das Ergebnis erscheint danach hier unter „Boot Memory Test“.",
            async () =>
            {
                var r = await Core.Processes.ElevatedCommandRunner.RunAsync(AllowedCommand.MemoryDiagnostic);
                StatusMessage = r.Cancelled ? "Die Administratorfreigabe wurde abgelehnt." : "Windows-Speicherdiagnose geöffnet.";
                return !r.Cancelled;
            }, warning: false);
    }
}
