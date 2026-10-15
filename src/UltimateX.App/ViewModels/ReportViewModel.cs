using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Health;
using UltimateX.Services.Hardware;
using UltimateX.Services.Reports.Pdf;

namespace UltimateX.App.ViewModels;

/// <summary>
/// PC-BERICHT (v1.1 Phase 5): Vorschau mit Abschnittsauswahl, EXPORT PC REPORT als echtes PDF und PRINT PC REPORT
/// über den Windows-Druckdialog (Drucker oder „Microsoft Print to PDF“). Datenschutzmodus standardmäßig AN.
/// </summary>
public sealed partial class ReportViewModel : PageViewModel
{
    private readonly INavigator _nav;
    private PcReportData? _data;
    private ReportDocument? _doc;
    private int _renderVersion;

    public ReportViewModel(INavigator nav) => _nav = nav;

    public override string Title => "PC-Bericht";
    public override string Glyph => "";
    public override string Keywords => "bericht report pdf drucken print export systembericht pc report vorschau";
    public override string? Subtitle => "PDF-Export und Druck mit Vorschau – Datenschutzmodus standardmäßig an";

    [ObservableProperty] private bool _hardware = true;
    [ObservableProperty] private bool _sensors = true;
    [ObservableProperty] private bool _network = true;
    [ObservableProperty] private bool _security = true;
    [ObservableProperty] private bool _drivers = true;
    [ObservableProperty] private bool _windowsHealth = true;
    [ObservableProperty] private bool _componentTests = true;
    [ObservableProperty] private bool _benchmarks = true;
    [ObservableProperty] private bool _crashAnalysis = true;
    [ObservableProperty] private bool _analysis = true;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private bool _isRendering;
    [ObservableProperty] private string? _dataStamp;

    public ObservableCollection<ImageSource> Pages { get; } = [];
    public ObservableCollection<string> Included { get; } = [];

    private bool _privacyMode = true;

    /// <summary>Datenschutzmodus. Abschalten erfordert eine Windows-Hello-Bestätigung (wie „Sensible Daten maskieren“).</summary>
    public bool PrivacyMode
    {
        get => _privacyMode;
        set
        {
            if (value == _privacyMode) return;
            if (value)
            {
                _privacyMode = true;
                OnPropertyChanged();
                _ = RenderAsync();
                return;
            }
            _ = DisablePrivacyAsync();
        }
    }

    private async Task DisablePrivacyAsync()
    {
        if (await AppServices.Guard.AuthenticateOnlyAsync("Bericht ohne Datenschutzmodus (vollständige IP/MAC/Namen)"))
        {
            _privacyMode = false;
            AppLog.Security("ReportPrivacy", "PC-Bericht: Datenschutzmodus deaktiviert");
            await RenderAsync();
        }
        OnPropertyChanged(nameof(PrivacyMode));
    }

    partial void OnHardwareChanged(bool value) => _ = RenderAsync();
    partial void OnSensorsChanged(bool value) => _ = RenderAsync();
    partial void OnNetworkChanged(bool value) => _ = RenderAsync();
    partial void OnSecurityChanged(bool value) => _ = RenderAsync();
    partial void OnDriversChanged(bool value) => _ = RenderAsync();
    partial void OnWindowsHealthChanged(bool value) => _ = RenderAsync();
    partial void OnComponentTestsChanged(bool value) => _ = RenderAsync();
    partial void OnBenchmarksChanged(bool value) => _ = RenderAsync();
    partial void OnCrashAnalysisChanged(bool value) => _ = RenderAsync();
    partial void OnAnalysisChanged(bool value) => _ = RenderAsync();

    private PcReportOptions Options => new()
    {
        Hardware = Hardware, Sensors = Sensors, Network = Network, Security = Security, Drivers = Drivers,
        WindowsHealth = WindowsHealth, ComponentTests = ComponentTests, Benchmarks = Benchmarks, CrashAnalysis = CrashAnalysis,
        Analysis = Analysis, PrivacyMode = PrivacyMode,
    };

    protected override async Task LoadAsync()
    {
        _data = await CollectAsync();
        DataStamp = $"Daten erhoben: {_data.Created:dd.MM.yyyy HH:mm:ss} · Bericht-ID {_data.ReportId}";
        await RenderAsync();
    }

    /// <summary>Erhebt alle Berichtsdaten (nur lesend). Fehler einzelner Quellen lassen den Abschnitt leer statt abzubrechen.</summary>
    private async Task<PcReportData> CollectAsync()
    {
        // UI-Thread: Monitor-Verlauf und Komponententest dieser Sitzung übernehmen.
        var recent = MonitorHub.Recent(MonitorHub.MaxSamples);
        var sessionRun = _nav.Page<ComponentTestViewModel>().CurrentRun;
        ReportAnalysis? analysis = null;
        try { analysis = await BuildAnalysisAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Report", "Bewertungen nicht verfügbar", ex.Message);
        }

        T? Safe<T>(Func<T?> f, string what) where T : class
        {
            try { return f(); }
            catch (Exception ex)
            {
                AppLog.Warn("Report", $"{what} nicht verfügbar", ex.Message);
                return null;
            }
        }

        return await Task.Run(() =>
        {
            var hw = AppServices.LastHardware ?? Safe(() => AppServices.Hardware.Collect(), "Hardware");
            if (hw is not null) AppServices.LastHardware = hw;
            var security = Safe(() => AppServices.Security.GetStatus(), "Sicherheit") ?? [];
            var reboot = Safe(() => AppServices.Health.GetRebootStatus(), "Neustartstatus");
            var services = Safe(() => AppServices.Health.GetServiceStatus(), "Dienste") ?? [];
            var crashes = Safe(() => AppServices.Health.GetCrashSummary(), "Absturzübersicht");
            var devices = Safe(() => AppServices.Inventory.GetDevices().Devices, "Geräte") ?? [];
            IReadOnlyList<double> Down(Func<Services.Monitoring.MonitorSample, double?> sel)
            {
                if (recent.Count < 2) return [];
                var step = Math.Max(1, recent.Count / 150);
                return recent.Where((_, i) => i % step == 0).Select(s => sel(s) ?? 0).ToArray();
            }
            var componentRun = sessionRun ?? Safe(() => AppServices.TestHistory.Load().LastOrDefault(), "Komponententest");

            return new PcReportData
            {
                ReportId = $"UX-{DateTime.Now:yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}",
                Created = DateTime.Now,
                AppVersion = AppServices.AppVersion,
                UserName = Elevation.CurrentUserName,
                Hardware = hw,
                Score = HealthScore.Calculate(hw, security, reboot, services, crashes),
                Sensors = AppServices.Monitor.SensorManager.Sensors,
                SensorSources = AppServices.Monitor.SensorManager.Sources,
                CpuHistory = Down(s => s.CpuPercent),
                RamHistory = Down(s => s.RamPercent),
                GpuHistory = Down(s => s.GpuPercent),
                Adapters = Safe(() => AppServices.Network.GetAdapters(), "Netzwerk") ?? [],
                Speedtests = Safe(() => AppServices.History.GetSpeedtests(365), "Speedtests") ?? [],
                Security = security,
                ProblemDevices = devices.Where(d => d.ErrorCode is not null and not 0).ToList(),
                Services = services,
                Reboot = reboot,
                Crashes = crashes,
                ComponentTests = componentRun,
                Benchmarks = Safe(() => AppServices.Benchmarks.History(), "Benchmarks") ?? [],
                CrashReport = Safe(() => AppServices.Crashes.Analyze(30), "Absturzanalyse"),
                Smart = SmartService.LoadCached()?.Disks ?? [],
                Analysis = analysis,
            };
        });
    }

    /// <summary>v1.2: Performance Score, Gaming Score, RAM-Test, Laufwerke, Netzwerk, Probleme und Empfehlungen.</summary>
    private static async Task<ReportAnalysis> BuildAnalysisAsync()
    {
        var facts = await FactsCollector.CollectAsync(includeProcesses: false);
        return await Task.Run(() =>
        {
            var perf = Core.Analysis.PerformanceScore.Combine(facts.Facts.Scores);
            var gaming = Core.Analysis.GamingCheck.Evaluate(facts.Facts);
            var bottleneck = Core.Analysis.BottleneckAnalyzer.Analyze(facts.Facts);
            var advice = Core.Analysis.UpgradeAdvisor.Advise(facts.Facts);
            var mem = facts.History.Where(r => r.Kind == Services.Benchmark.BenchmarkService.KindMemTest).OrderByDescending(r => r.Time).FirstOrDefault();
            var drives = AppServices.DriveHealth.GetDrives(SmartService.LoadCached())
                .Select(d => (d.Name, d.Verdict.Label, d.Verdict.State)).ToList();
            var problems = gaming.Findings.Concat(bottleneck.Findings)
                .Where(f => f.Status is Core.Analysis.CheckStatus.Warning or Core.Analysis.CheckStatus.Fail)
                .GroupBy(f => f.Area).Select(g => g.First()).ToList();
            var recs = advice.FreeFixes.Concat(advice.Upgrades.Select(u => $"{u.Component}: {u.Reason}")).ToList();
            var memFailed = mem?.Metric("Fehler") is > 0;
            return new ReportAnalysis(perf, gaming.Score, facts.Facts.Scores.Network,
                mem is null ? null : $"{mem.Note} · {Services.Common.Format.DateTimeText(mem.Time)}",
                mem is null ? DataState.NotAvailable : memFailed ? DataState.Problem : DataState.Ok,
                drives, problems, recs);
        });
    }

    private async Task RenderAsync()
    {
        if (_data is null) return;
        var version = ++_renderVersion;
        IsRendering = true;
        try
        {
            var data = _data;
            var options = Options;
            var doc = await Task.Run(() => PcReportBuilder.Build(data, options));
            if (version != _renderVersion) return;
            _doc = doc;
            Pages.Clear();
            foreach (var p in doc.Pages)
            {
                Pages.Add(ReportRenderer.RenderBitmap(p, 720));
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                if (version != _renderVersion) return;
            }
            PageCount = doc.Pages.Count;
            UpdateIncluded(options);
        }
        finally
        {
            if (version == _renderVersion) IsRendering = false;
        }
    }

    private void UpdateIncluded(PcReportOptions o)
    {
        Included.Clear();
        Included.Add("Übersicht: Health Score, Hardware-Karten, Warnungen, Auslastungsdiagramm");
        if (o.Hardware) Included.Add("Hardware: Windows, CPU, GPU, RAM-Module, Mainboard/BIOS, Datenträger (SMART), Volumes");
        if (o.Sensors) Included.Add("Sensoren: Quellenstatus, einheitliches Sensormodell mit Min/Ø/Max");
        if (o.Network) Included.Add(o.PrivacyMode ? "Netzwerk: Adapter, Link, maskierte IP/MAC, Speedtests" : "Netzwerk: Adapter, Link, VOLLSTÄNDIGE IP/MAC, Gateway, DNS, Speedtests");
        if (o.Security) Included.Add("Sicherheit: Virenschutz, Firewall, Secure Boot, TPM, BitLocker, UAC");
        if (o.Drivers) Included.Add("Treiber: Grafiktreiber, Geräte mit Fehlercode");
        if (o.WindowsHealth) Included.Add("Windows Health: Dienste, Neustart, Abstürze, Score-Abzüge");
        if (o.ComponentTests) Included.Add("Komponententests: letzter Lauf mit Status je Komponente");
        if (o.Benchmarks) Included.Add("Benchmarks: letzte Ergebnisse und Verlauf");
        if (o.CrashAnalysis) Included.Add("Absturzanalyse: Bluescreens, WHEA, Programmabstürze");
        if (o.Analysis) Included.Add("Bewertung: Performance Score, Gaming Score, RAM-Test, Laufwerke, Netzwerk, Probleme, Empfehlungen");
        Included.Add(o.PrivacyMode
            ? "Datenschutzmodus AN: Benutzer- und Computername, Seriennummern, IP-/MAC-Adressen, Gateway/DNS maskiert; keine öffentliche IP."
            : "Datenschutzmodus AUS: vollständige Gerätedaten enthalten – nur an vertrauenswürdige Personen weitergeben.");
    }

    [RelayCommand]
    private async Task RecollectAsync()
    {
        AppServices.LastHardware = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (_doc is null) return;
        var path = Dialogs.SaveFile($"UltimateX-PC-Bericht-{DateTime.Now:yyyyMMdd-HHmm}", "PDF-Dokument (*.pdf)|*.pdf");
        if (path is null) return;
        try
        {
            var doc = _doc;
            var bytes = await Task.Run(() => PdfWriter.Write(doc));
            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = $"PDF gespeichert: {path} ({PageCount} Seiten)";
            AppLog.Info("Report", "PC-Bericht als PDF exportiert", $"{PageCount} Seiten, Datenschutzmodus {(PrivacyMode ? "an" : "aus")}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "PDF konnte nicht gespeichert werden: " + ex.Message;
        }
    }

    /// <summary>Erstellt den Bericht neu und speichert ihn ohne Dialog als PDF (für das One-Click-Setup). Liefert den Pfad oder null.</summary>
    public async Task<string?> ExportToDocumentsAsync()
    {
        // Läuft gerade ein Laden der Seite, darauf warten – danach immer frisch erheben und direkt aus DIESEN Daten bauen,
        // damit nie ein älterer Bericht gespeichert wird.
        for (var i = 0; IsBusy && i < 240; i++) await Task.Delay(500);
        if (IsBusy) return null;
        AppServices.LastHardware = null;
        PcReportData data;
        IsBusy = true;
        try
        {
            data = await CollectAsync();
            _data = data;
            DataStamp = $"Daten erhoben: {data.Created:dd.MM.yyyy HH:mm:ss} · Bericht-ID {data.ReportId}";
        }
        finally
        {
            IsBusy = false;
        }
        var options = Options;
        var doc = await Task.Run(() => PcReportBuilder.Build(data, options));
        _ = RenderAsync(); // Vorschau aktualisieren (unabhängig vom Export)
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ULTIMATE X");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"UltimateX-PC-Bericht-{DateTime.Now:yyyyMMdd-HHmm}.pdf");
        var bytes = await Task.Run(() => PdfWriter.Write(doc));
        await File.WriteAllBytesAsync(path, bytes);
        AppLog.Info("Report", "PC-Bericht automatisch gespeichert", $"{doc.Pages.Count} Seiten");
        return path;
    }

    [RelayCommand]
    private void Print()
    {
        if (_doc is null) return;
        var dlg = new System.Windows.Controls.PrintDialog { UserPageRangeEnabled = false };
        if (dlg.ShowDialog() != true) return;
        try
        {
            dlg.PrintDocument(new ReportRenderer.Paginator(_doc), $"ULTIMATE X PC-Bericht {_data?.ReportId}");
            StatusMessage = $"An „{dlg.PrintQueue?.FullName}“ gesendet ({PageCount} Seiten).";
        }
        catch (Exception ex) when (ex is System.Printing.PrintSystemException or InvalidOperationException)
        {
            StatusMessage = "Drucken fehlgeschlagen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenComponentTests() => _nav.Navigate(_nav.Page<ComponentTestViewModel>());
}
