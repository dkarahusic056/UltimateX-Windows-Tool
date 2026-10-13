using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Diagnostics;

namespace UltimateX.App.ViewModels;

/// <summary>Eine Testzeile in der Oberfläche.</summary>
public sealed partial class TestRow(ComponentTestDefinition definition) : ObservableObject
{
    public ComponentTestDefinition Definition { get; } = definition;
    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Description => Definition.Description;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel), nameof(State), nameof(DetailsText), nameof(HasResult))]
    private ComponentTestResult? _result;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isExpanded;

    public bool HasResult => Result is not null;
    public string StatusLabel => IsRunning ? "TESTING …" : (Result?.Status ?? TestStatus.NotTested).Label();
    public DataState State => (Result?.Status ?? TestStatus.NotTested).ToDataState();
    public string Summary => Result?.Summary ?? Description;
    public string DetailsText => Result is null ? "" : string.Join("\n", Result.Details);
    public string DurationText => Result is null ? "" : $"{Result.DurationMs / 1000:0.0} s";

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusLabel));

    partial void OnResultChanged(ComponentTestResult? value)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(DurationText));
    }

    /// <summary>Hat dieser Test eine optionale Benutzeraktion?</summary>
    public bool HasSound => Id == "speakers";
    public bool HasMic => Id == "microphone";
    public bool HasCamera => Id == "camera";
    public bool HasColorTest => Id == "monitors";
    public bool HasInputTest => Id is "keyboard" or "mouse";
    public bool HasWriteTest => Id is "nvme" or "ssd" or "hdd";
    public bool HasPrintTest => Id == "printer";
    public bool HasMemoryDiag => Id == "ram";
}

public sealed record TestGroup(string Name, IReadOnlyList<TestRow> Rows);

/// <summary>
/// SYSTEM COMPONENT TEST CENTER (v1.1 Phase 4): „TEST ALL SYSTEM COMPONENTS“, Einzeltests, interaktive Prüfungen
/// (nur auf Klick), Verlauf mit Vergleich zum letzten Lauf und Export als TXT/JSON.
/// </summary>
public sealed partial class ComponentTestViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;
    private MicLevelMeter? _mic;
    private readonly DispatcherTimer _micTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly Dictionary<string, TestRow> _rows;

    public ComponentTestViewModel()
    {
        _rows = ComponentTestService.Definitions.ToDictionary(d => d.Id, d => new TestRow(d));
        Groups = ComponentTestService.Definitions.GroupBy(d => d.Category)
            .Select(g => new TestGroup(ComponentTestReport.CategoryName(g.Key), g.Select(d => _rows[d.Id]).ToList()))
            .ToList();
        _micTimer.Tick += (_, _) =>
        {
            if (_mic is null) return;
            MicLevel = _mic.Level * 100;
            MicPeak = _mic.PeakSinceStart * 100;
        };
    }

    public override string Title => "Komponententest";
    public override string Glyph => "";
    public override string Keywords => "test komponenten hardware prüfen check cpu ram gpu ssd nvme festplatte netzwerk usb audio mikrofon kamera tastatur maus drucker tpm defender component test";
    public override string? Subtitle => "System Component Test Center – sichere, nicht-destruktive Prüfungen";

    public IReadOnlyList<TestGroup> Groups { get; }

    [ObservableProperty] private bool _isRunningAll;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "Bereit. „TEST ALL SYSTEM COMPONENTS“ startet alle sicheren Tests.";
    [ObservableProperty] private int _passCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _failCount;
    [ObservableProperty] private int _naCount;
    [ObservableProperty] private int _userActionCount;
    [ObservableProperty] private string _overallText = "NOT TESTED";
    [ObservableProperty] private DataState _overallState = DataState.NotAvailable;
    [ObservableProperty] private bool _isMicRunning;
    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private double _micPeak;
    [ObservableProperty] private string? _historyText;
    public ObservableCollection<string> Changes { get; } = [];

    private ComponentTestRun? _lastRun;
    private ComponentTestRun? _previousRun;

    protected override async Task LoadAsync()
    {
        var runs = await Task.Run(() => AppServices.TestHistory.Load());
        if (runs.Count > 0)
        {
            _lastRun = runs[^1];
            _previousRun = runs.Count > 1 ? runs[^2] : null;
            foreach (var r in _lastRun.Results)
                if (_rows.TryGetValue(r.Id, out var row)) row.Result = r;
            UpdateCounts();
            UpdateHistory();
        }
    }

    // ------------------------------------------------------------ Gesamtlauf

    [RelayCommand]
    private async Task RunAllAsync()
    {
        if (IsRunningAll) return;
        IsRunningAll = true;
        _cts = new CancellationTokenSource();
        foreach (var r in _rows.Values) { r.Result = null; r.IsRunning = false; }
        UpdateCounts();
        var total = ComponentTestService.Definitions.Count(d => !d.Interactive);
        var done = 0;
        var defs = ComponentTestService.Definitions.Where(d => !d.Interactive).ToList();
        if (defs.Count > 0) _rows[defs[0].Id].IsRunning = true;
        var progress = new Progress<ComponentTestResult>(r =>
        {
            done++;
            var row = _rows[r.Id];
            row.IsRunning = false;
            row.Result = r;
            if (done < defs.Count) _rows[defs[done].Id].IsRunning = true;
            Progress = 100.0 * done / total;
            ProgressText = $"{done}/{total} getestet · zuletzt: {r.Name} – {r.Status.Label()}";
            UpdateCounts();
        });
        try
        {
            var run = await AppServices.ComponentTests.RunAllAsync(progress, _cts.Token);
            _previousRun = _lastRun;
            _lastRun = run;
            await Task.Run(() => AppServices.TestHistory.Save(run));
            ProgressText = $"Fertig: {run.Results.Count} Tests in {(run.Finished - run.Started).TotalSeconds:0} s · Gesamt {run.Overall.Label()}";
            AppLog.Info("ComponentTest", "Komponententest abgeschlossen", ProgressText);
            UpdateHistory();
        }
        catch (OperationCanceledException)
        {
            ProgressText = "Test abgebrochen.";
        }
        catch (IOException ex)
        {
            StatusMessage = "Testverlauf konnte nicht gespeichert werden: " + ex.Message;
        }
        finally
        {
            foreach (var r in _rows.Values) r.IsRunning = false;
            IsRunningAll = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelRun() => _cts?.Cancel();

    [RelayCommand]
    private async Task RunOneAsync(TestRow? row)
    {
        if (row is null || row.IsRunning) return;
        row.IsRunning = true;
        try
        {
            AppServices.ComponentTests.ResetContext();
            Apply(await AppServices.ComponentTests.RunAsync(row.Id, CancellationToken.None));
        }
        finally
        {
            row.IsRunning = false;
        }
    }

    [RelayCommand]
    private void ToggleDetails(TestRow? row)
    {
        if (row is not null) row.IsExpanded = !row.IsExpanded;
    }

    private void Apply(ComponentTestResult r)
    {
        if (_rows.TryGetValue(r.Id, out var row)) row.Result = r;
        UpdateCounts();
        // Einzelergebnisse fließen in den aktuellen Lauf ein (für Bericht/Verlauf).
        if (_lastRun is not null)
        {
            var list = _lastRun.Results.Where(x => x.Id != r.Id).Append(r)
                .OrderBy(x => ComponentTestService.Definitions.ToList().FindIndex(d => d.Id == x.Id)).ToList();
            _lastRun = _lastRun with { Results = list, Finished = DateTime.Now };
        }
        else
        {
            _lastRun = new ComponentTestRun(Guid.NewGuid().ToString("N"), DateTime.Now, DateTime.Now, [r]);
        }
    }

    private void UpdateCounts()
    {
        var results = _rows.Values.Select(r => r.Result).OfType<ComponentTestResult>().ToList();
        PassCount = results.Count(r => r.Status == TestStatus.Pass);
        WarningCount = results.Count(r => r.Status == TestStatus.Warning);
        FailCount = results.Count(r => r.Status == TestStatus.Fail);
        NaCount = results.Count(r => r.Status == TestStatus.NotAvailable);
        UserActionCount = results.Count(r => r.Status == TestStatus.UserActionRequired);
        var overall = results.Count == 0 ? TestStatus.NotTested
            : FailCount > 0 ? TestStatus.Fail : WarningCount > 0 ? TestStatus.Warning : TestStatus.Pass;
        OverallText = overall.Label();
        OverallState = overall.ToDataState();
    }

    private void UpdateHistory()
    {
        Changes.Clear();
        if (_lastRun is null) { HistoryText = null; return; }
        HistoryText = _previousRun is null
            ? $"Letzter Lauf: {_lastRun.Started:dd.MM.yyyy HH:mm} · noch kein vorheriger Lauf zum Vergleich."
            : $"Letzter Lauf: {_lastRun.Started:dd.MM.yyyy HH:mm} · verglichen mit {_previousRun.Started:dd.MM.yyyy HH:mm}";
        if (_previousRun is null) return;
        var changes = ComponentTestHistory.Compare(_previousRun, _lastRun);
        foreach (var c in changes)
            Changes.Add($"{(c.Worsened ? "▼" : c.Improved ? "▲" : "•")} {c.Name}: {c.Previous?.Label() ?? "neu"} → {c.Current.Label()}");
        if (Changes.Count == 0) Changes.Add("Keine Veränderungen zum vorherigen Lauf.");
    }

    // ------------------------------------------------------------ Interaktive Tests (nur auf Benutzeraktion)

    [RelayCommand]
    private async Task PlaySoundAsync()
    {
        StatusMessage = "Testton läuft: links → rechts → beide Seiten …";
        var ok = await Task.Run(AudioTest.PlayTestTone);
        if (!ok)
        {
            Apply(ComponentTestService.UserConfirmed("speakers", false, "Testton konnte nicht abgespielt werden (kein Ausgabegerät oder Gerät belegt)."));
            StatusMessage = null;
            return;
        }
        var heard = Dialogs.Confirm("Lautsprecher-Test", "Hast du den Ton zuerst LINKS, dann RECHTS und zuletzt auf BEIDEN Seiten gehört?");
        Apply(ComponentTestService.UserConfirmed("speakers", heard,
            heard ? "Testton links/rechts/beide hörbar – vom Benutzer bestätigt." : "Testton nicht (vollständig) hörbar – Lautstärke, Ausgabegerät und Kabel prüfen."));
        StatusMessage = null;
    }

    [RelayCommand]
    private void ToggleMic()
    {
        if (_mic is not null)
        {
            StopMic(record: true);
            return;
        }
        _mic = new MicLevelMeter();
        var error = _mic.Start();
        if (error is not null)
        {
            _mic.Dispose();
            _mic = null;
            Apply(ComponentTestService.UserConfirmed("microphone", false, error));
            return;
        }
        IsMicRunning = true;
        _micTimer.Start();
        StatusMessage = "Mikrofon-Pegel läuft – bitte sprechen. Es wird nichts aufgenommen oder gespeichert. Erneut klicken zum Beenden.";
    }

    private void StopMic(bool record)
    {
        if (_mic is null) return;
        var peak = _mic.PeakSinceStart;
        _micTimer.Stop();
        _mic.Dispose();
        _mic = null;
        IsMicRunning = false;
        MicLevel = 0;
        StatusMessage = null;
        if (!record) return;
        var ok = peak >= 0.02;
        Apply(ComponentTestService.UserConfirmed("microphone", ok,
            ok ? $"Mikrofon liefert Signal (Spitzenpegel {peak * 100:0} %)." : "Kein Signal vom Mikrofon – stummgeschaltet, Datenschutzfreigabe oder falsches Standardgerät?",
            ["Es wurde nichts aufgenommen oder gespeichert; nur der Pegel wurde ausgewertet."]));
    }

    [RelayCommand]
    private async Task CameraTestAsync()
    {
        if (!Dialogs.Confirm("Kameratest", "Die Kamera wird kurz geöffnet und sofort wieder freigegeben. Es wird kein Bild aufgenommen oder gespeichert. Fortfahren?"))
            return;
        Apply(await ComponentTestService.RunCameraAccessTestAsync());
    }

    [RelayCommand]
    private void ColorTest()
    {
        var w = new Views.ColorTestWindow { Owner = System.Windows.Application.Current.MainWindow };
        w.ShowDialog();
        if (!w.Completed) return;
        var defects = Dialogs.Confirm("Farbtest", "Sind dir Pixelfehler (dauerhaft helle/dunkle Punkte), Streifen oder starke Helligkeitsunterschiede aufgefallen?");
        Apply(ComponentTestResult.Create(ComponentTestService.Definition("monitors"), defects ? TestStatus.Warning : TestStatus.Pass,
            defects ? "Farbtest: Auffälligkeiten vom Benutzer gemeldet." : "Farbtest ohne Auffälligkeiten (vom Benutzer bestätigt).",
            ["Getestet: Rot, Grün, Blau, Weiß, Schwarz, Grau (Vollbild)."]));
    }

    [RelayCommand]
    private void InputTest()
    {
        var w = new Views.InputTestWindow { Owner = System.Windows.Application.Current.MainWindow };
        w.ShowDialog();
        var mouseDetails = w.MouseCounts.Where(x => x.Value > 0).Select(x => $"{x.Key}: {x.Value}×").ToList();
        if (w.KeyboardOk is { } kb)
            Apply(ComponentTestService.UserConfirmed("keyboard", kb && w.DistinctKeys > 0,
                kb ? $"{w.DistinctKeys} verschiedene Tasten erkannt – vom Benutzer bestätigt." : "Problem mit der Tastatur vom Benutzer gemeldet.",
                [$"Erkannte Tasten: {w.DistinctKeys}", "Erfassung nur im Testfenster, nichts gespeichert."]));
        if (w.MouseOk is { } ms)
            Apply(ComponentTestService.UserConfirmed("mouse", ms,
                ms ? "Maus funktioniert – vom Benutzer bestätigt." : "Problem mit der Maus vom Benutzer gemeldet.",
                mouseDetails.Count == 0 ? ["Keine Mausaktion im Testfeld"] : mouseDetails));
    }

    [RelayCommand]
    private async Task WriteTestAsync(TestRow? row)
    {
        if (row is null) return;
        var kind = row.Id switch { "nvme" => DiskKind.Nvme, "ssd" => DiskKind.Ssd, _ => DiskKind.Hdd };
        var disks = await Task.Run(() => AppServices.ComponentTests.GetDisks());
        var letters = disks.Where(d => d.Kind == kind).SelectMany(d => d.Letters).ToList();
        if (letters.Count == 0)
        {
            StatusMessage = "Für diesen Datenträgertyp ist kein Laufwerk mit Buchstaben vorhanden.";
            return;
        }
        var letter = letters[0];
        if (!Dialogs.Confirm("Optionaler Schreibtest",
                $"Auf Laufwerk {letter}: wird eine NEUE Testdatei mit 128 MB Zufallsdaten geschrieben, zurückgelesen, per SHA-256 verglichen und anschließend gelöscht.\n\n" +
                "Bestehende Dateien werden nicht verändert. Bei SSDs entspricht das einem winzigen Bruchteil der Lebensdauer.\n\nSchreibtest jetzt ausführen?", true))
            return;
        row.IsRunning = true;
        try
        {
            Apply(await ComponentTestService.RunWriteTestAsync(row.Id, letter, CancellationToken.None));
        }
        finally
        {
            row.IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task PrintTestPageAsync()
    {
        var printers = await Task.Run(ComponentTestService.GetPrinterNames);
        if (printers.Count == 0) { StatusMessage = "Kein Drucker eingerichtet."; return; }
        var target = printers[0];
        var def = await Task.Run(() => Services.Common.Wmi.Query("SELECT Name, Default FROM Win32_Printer").Rows
            .FirstOrDefault(r => r.Bool("Default") == true)?.Str("Name"));
        if (def is not null) target = def;
        if (!Dialogs.Confirm("Windows-Testseite", $"Eine Windows-Testseite wird auf „{target}“ gedruckt (Standarddrucker). Fortfahren?"))
            return;
        var r = await Task.Run(() => ComponentTestService.PrintTestPage(target));
        StatusMessage = r.Message;
        if (r.Success)
        {
            var printed = Dialogs.Confirm("Windows-Testseite", "Wurde die Testseite korrekt gedruckt?");
            Apply(ComponentTestService.UserConfirmed("printer", printed, printed ? $"Testseite auf „{target}“ korrekt gedruckt." : $"Testseite auf „{target}“ fehlerhaft oder nicht gedruckt."));
        }
    }

    [RelayCommand]
    private async Task MemoryDiagnosticAsync()
    {
        if (!Dialogs.Confirm("Windows-Speicherdiagnose", "Die Windows-Speicherdiagnose testet den gesamten Arbeitsspeicher beim nächsten Neustart (ca. 15–30 Minuten). Das Ergebnis erscheint danach hier im RAM-Test. Jetzt öffnen?"))
            return;
        var r = await Core.Processes.ElevatedCommandRunner.RunAsync(AllowedCommand.MemoryDiagnostic);
        StatusMessage = r.Cancelled ? "Abgebrochen." : "Speicherdiagnose geöffnet – dort „Jetzt neu starten“ oder „Beim nächsten Start“ wählen.";
    }

    // ------------------------------------------------------------ Export

    [RelayCommand]
    private void Export()
    {
        if (_lastRun is null) { StatusMessage = "Noch kein Testlauf vorhanden."; return; }
        var path = Dialogs.SaveFile($"UltimateX-Komponententest-{DateTime.Now:yyyyMMdd-HHmm}", "Textdatei (*.txt)|*.txt|JSON (*.json)|*.json");
        if (path is null) return;
        var changes = ComponentTestHistory.Compare(_previousRun, _lastRun);
        var content = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? ComponentTestReport.ToJson(_lastRun, changes, AppServices.AppVersion)
            : ComponentTestReport.ToText(_lastRun, changes, AppServices.AppVersion);
        try
        {
            File.WriteAllText(path, content);
            StatusMessage = "Bericht gespeichert: " + path + " (PDF: Seite „PC-Bericht“).";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Bericht konnte nicht gespeichert werden: " + ex.Message;
        }
    }

    /// <summary>Aktueller Lauf für den PDF-Bericht (Phase 5).</summary>
    public ComponentTestRun? CurrentRun => _lastRun;

    public override void OnNavigatedFrom() => StopMic(record: false);
}
