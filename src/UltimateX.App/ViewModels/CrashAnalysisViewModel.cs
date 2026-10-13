using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Health;

namespace UltimateX.App.ViewModels;

public sealed record BlueScreenRow(string Date, string Code, string Name, string Meaning, string Advice, string Parameters, string Source, string? DumpFile);

public sealed record AppCrashRow(string Application, string Module, string Code, int Count, string Last, string Kind);

public sealed record DumpRow(string File, string Date, string Size, string Code, string Name, string Parameters);

/// <summary>Absturzanalyse (§103): Bluescreens verstehen, Hardwarefehler erkennen, wiederholte Programmabstürze finden.</summary>
public sealed partial class CrashAnalysisViewModel : PageViewModel
{
    public override string Title => "Absturzanalyse";
    public override string Glyph => "";
    public override string Keywords => "absturz absturzanalyse bluescreen bsod bugcheck crash dump minidump whea hardwarefehler programm abgestürzt hängt treiber";
    public override string? Subtitle => "Bluescreens, Hardwarefehler und Programmabstürze verständlich erklärt";

    public ObservableCollection<CrashHint> Hints { get; } = [];
    public ObservableCollection<BlueScreenRow> BlueScreens { get; } = [];
    public ObservableCollection<HardwareErrorGroup> HardwareErrors { get; } = [];
    public ObservableCollection<AppCrashRow> AppCrashes { get; } = [];
    public ObservableCollection<DumpRow> Dumps { get; } = [];

    public IReadOnlyList<int> DayOptions { get; } = [7, 30, 90, 365];
    [ObservableProperty] private int _days = 90;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private DumpRow? _selectedDump;
    [ObservableProperty] private bool _isAnalyzing;
    [ObservableProperty] private string? _analysisResult;
    [ObservableProperty] private string? _analysisRaw;
    [ObservableProperty] private bool _dumpsLoaded;

    public bool DebuggerInstalled => CrashAnalysisService.DebuggerInstalled;

    partial void OnDaysChanged(int value) => _ = RefreshAsync();

    protected override async Task LoadAsync()
    {
        var days = Days;
        var report = await Task.Run(() => AppServices.Crashes.Analyze(days));
        Hints.Clear();
        foreach (var h in report.Hints.OrderBy(h => h.Severity switch { DataState.Problem => 0, DataState.Warning => 1, _ => 2 })) Hints.Add(h);
        BlueScreens.Clear();
        foreach (var b in report.BlueScreens)
            BlueScreens.Add(new BlueScreenRow(Format.DateTimeText(b.Time), $"0x{b.Code:X8}", b.Info.Name, b.Info.Meaning, b.Info.Advice, b.Parameters, b.Source, b.DumpFile));
        HardwareErrors.Clear();
        foreach (var w in report.HardwareErrors) HardwareErrors.Add(w);
        AppCrashes.Clear();
        foreach (var a in report.AppCrashes)
            AppCrashes.Add(new AppCrashRow(a.Application, a.Module, a.ExceptionCode, a.Count, Format.DateTimeText(a.Last), a.IsHang ? "Hängt" : "Absturz"));
        Summary = $"Letzte {report.Days} Tage: {report.BlueScreens.Count} Bluescreen(s) · {report.PowerLossResets} unerwartete Abschaltung(en) · " +
                  $"{report.DisplayDriverResets} Grafiktreiber-Reset(s) · {report.HardwareErrors.Sum(h => h.Count)} Hardware-Ereignis(se) · " +
                  $"{report.AppCrashes.Sum(a => a.Count)} Programmabstürze/-hänger";
    }

    /// <summary>Liest die Köpfe der Absturzabbilder (UAC, da der Ordner nur für Administratoren lesbar ist).</summary>
    [RelayCommand]
    private async Task LoadDumpsAsync()
    {
        IsBusy = true;
        try
        {
            var (dumps, error) = await AppServices.Crashes.ReadDumpsAsync();
            Dumps.Clear();
            foreach (var d in dumps)
                Dumps.Add(new DumpRow(d.File, Format.DateTimeText(d.Time), Format.Bytes(d.Size),
                    d.Valid ? $"0x{d.Code:X8}" : "–", d.Valid ? d.Info.Name : "Kopf nicht lesbar", d.Parameters));
            DumpsLoaded = error is null;
            StatusMessage = error ?? (dumps.Count == 0 ? "Keine Absturzabbilder vorhanden – Windows hat keinen Dump gespeichert." : $"{dumps.Count} Absturzabbild(er) gefunden.");
            SelectedDump = Dumps.FirstOrDefault();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AnalyzeDumpAsync()
    {
        if (SelectedDump is null || IsAnalyzing) return;
        if (!DebuggerInstalled)
        {
            Dialogs.Info("Debugger nicht installiert",
                "Für die genaue Ursache (welcher Treiber?) nutzt ULTIMATE X den offiziellen Microsoft-Debugger.\n\n" +
                "Installation: Windows SDK von Microsoft herunterladen (developer.microsoft.com → Windows SDK) und im Setup nur " +
                "„Debugging Tools for Windows“ auswählen.\n\nOhne Debugger zeigt ULTIMATE X Fehlercode, Bedeutung und Empfehlung.");
            return;
        }
        if (!Dialogs.Confirm("Absturzabbild analysieren",
                $"{SelectedDump.File} wird mit dem Microsoft-Debugger (kd.exe) analysiert. Dafür sind Administratorrechte nötig.\n\n" +
                "Beim ersten Mal lädt der Debugger Symboldateien vom Microsoft-Symbolserver (HTTPS, einige 100 MB möglich). " +
                "Der Dump selbst wird nicht hochgeladen.\n\nFortfahren?"))
            return;

        IsAnalyzing = true;
        AnalysisResult = "Analyse läuft …";
        AnalysisRaw = null;
        var progress = new Progress<string>(chunk => AnalysisRaw = ((AnalysisRaw ?? "") + chunk) is { Length: > 40_000 } t ? t[^40_000..] : (AnalysisRaw ?? "") + chunk);
        try
        {
            var a = await AppServices.Crashes.AnalyzeDumpAsync(SelectedDump.File, progress);
            AnalysisRaw = a.RawOutput;
            AnalysisResult = !a.DebuggerAvailable ? "Debugger nicht verfügbar."
                : a.ProbablyCausedBy is null && a.Image is null ? "Der Debugger hat keine eindeutige Ursache ermittelt (siehe Originalausgabe)."
                : $"Wahrscheinliche Ursache: {a.ProbablyCausedBy ?? a.Image}" +
                  (a.Image is not null ? $"\nDatei: {a.Image}" : "") +
                  (a.Process is not null ? $"\nProzess: {a.Process}" : "") +
                  (a.BugCheckText is not null ? $"\nFehler: {a.BugCheckText}" : "") +
                  "\n\nEmpfehlung: Den Treiber/das Programm zu dieser Datei aktualisieren oder neu installieren. Endet die Datei auf „.sys“, handelt es sich um einen Treiber.";
        }
        catch (ArgumentException ex)
        {
            AnalysisResult = ex.Message;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    [RelayCommand]
    private void OpenReliabilityMonitor() => ShowResult(AppServices.Tools.Launch(Services.SystemTools.SystemTool.ReliabilityMonitor));

    [RelayCommand]
    private Task RunMemoryDiagnosticAsync() => RunMemoryDiagnosticCoreAsync();

    private async Task RunMemoryDiagnosticCoreAsync()
    {
        if (!Dialogs.Confirm("Windows-Speicherdiagnose", "Die Windows-Speicherdiagnose wird geöffnet. Sie testet den Arbeitsspeicher beim nächsten Neustart (ca. 15–30 Minuten). Fortfahren?"))
            return;
        var r = await Core.Processes.ElevatedCommandRunner.RunAsync(AllowedCommand.MemoryDiagnostic);
        StatusMessage = r.Cancelled ? "Abgebrochen." : "Speicherdiagnose geöffnet – dort „Jetzt neu starten“ oder „Beim nächsten Start“ wählen.";
    }
}
