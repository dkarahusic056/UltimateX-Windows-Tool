using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Monitoring;
using UltimateX.Services.Network;
using UltimateX.Services.Reports;
using UltimateX.Services.Security;
using UltimateX.Services.Sensors;
using UltimateX.Services.Startup;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed partial class DashboardCard(string title, string glyph, PageViewModel? target) : ObservableObject
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public PageViewModel? Target { get; } = target;

    [ObservableProperty] private string _value = "…";
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private DataState _state = DataState.NotAvailable;
    [ObservableProperty] private double? _percent;
}

public sealed partial class CheckStep(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private DataState _state = DataState.NotAvailable;
    [ObservableProperty] private string _detail = "Ausstehend";
    [ObservableProperty] private bool _isRunning;
}

/// <summary>Hauptdashboard (§12, §13, §49, §50, §82, §100).</summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly INavigator _nav;

    public DashboardViewModel(INavigator nav)
    {
        _nav = nav;
        HealthCard = new DashboardCard("PC Health", "\uE95E", null);
        WindowsCard = new DashboardCard("Windows Health", "\uE90F", null);
        SecurityCard = new DashboardCard("Security", "\uEA18", null);
        CpuCard = new DashboardCard("CPU", "\uE950", null);
        GpuCard = new DashboardCard("GPU", "\uE7F4", null);
        RamCard = new DashboardCard("RAM", "\uE964", null);
        BoardCard = new DashboardCard("Mainboard", "\uE964", null);
        StorageCard = new DashboardCard("Storage", "\uEDA2", null);
        VramCard = new DashboardCard("VRAM", "\uE7F4", null);
        NetworkCard = new DashboardCard("Netzwerk", "\uE968", null);
        TempCard = new DashboardCard("Temperaturen", "\uE9CA", null);
        InternetCard = new DashboardCard("Internet", "\uE774", null);
        DriversCard = new DashboardCard("Treiber", "\uE772", null);
        SoftwareCard = new DashboardCard("Software", "\uE71D", null);
        StartupCard = new DashboardCard("Autostart", "\uE7E8", null);
        Cards = [HealthCard, WindowsCard, SecurityCard, CpuCard, GpuCard, RamCard, VramCard, BoardCard, StorageCard, NetworkCard, TempCard, InternetCard, DriversCard, SoftwareCard, StartupCard];
    }

    public override string Title => "Dashboard";
    public override string Glyph => "\uE80F";
    public override string Keywords => "start übersicht status health score bericht report check";
    public override string? Subtitle => "Your Complete Windows Control Center";

    public DashboardCard HealthCard { get; }
    public DashboardCard WindowsCard { get; }
    public DashboardCard SecurityCard { get; }
    public DashboardCard CpuCard { get; }
    public DashboardCard GpuCard { get; }
    public DashboardCard RamCard { get; }
    public DashboardCard BoardCard { get; }
    public DashboardCard StorageCard { get; }
    public DashboardCard VramCard { get; }
    public DashboardCard NetworkCard { get; }
    public DashboardCard TempCard { get; }
    public DashboardCard InternetCard { get; }
    public DashboardCard DriversCard { get; }
    public DashboardCard SoftwareCard { get; }
    public DashboardCard StartupCard { get; }
    public IReadOnlyList<DashboardCard> Cards { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScorePercent))]
    private HealthScoreResult? _score;

    /// <summary>Score als Ringwert (null = noch nicht berechnet).</summary>
    public double? ScorePercent => Score?.Score;
    [ObservableProperty] private bool _includeRepairScanInCheck;
    [ObservableProperty] private bool _includeSensitiveInReport;
    [ObservableProperty] private bool _isCheckRunning;
    [ObservableProperty] private string? _checkSummary;

    public ObservableCollection<CheckStep> CheckSteps { get; } = [];

    // ---- Hero-Banner / Kacheln im "ULTIMATE X"-Layout ----
    [ObservableProperty] private string _heroStatus = "…";
    [ObservableProperty] private string _heroText = "System wird analysiert …";
    [ObservableProperty] private DataState _heroState = DataState.NotAvailable;
    [ObservableProperty] private string _osText = "Windows";
    [ObservableProperty] private string _osBuild = "";
    [ObservableProperty] private string _uptimeText = "–";
    [ObservableProperty] private string _lastCheckText = "Noch kein Check";
    [ObservableProperty] private IReadOnlyList<double>? _cpuHistory;
    [ObservableProperty] private IReadOnlyList<double>? _ramHistory;
    [ObservableProperty] private IReadOnlyList<double>? _diskHistory;
    [ObservableProperty] private IReadOnlyList<double>? _gpuHistory;
    [ObservableProperty] private string _sensorSourceText = "Sensoren werden gelesen …";
    [ObservableProperty] private IReadOnlyList<SecurityItem> _securityHighlights = [];
    [ObservableProperty] private IReadOnlyList<InfoItem> _healthRows = [];
    [ObservableProperty] private IReadOnlyList<InfoItem> _updateRows = [];
    [ObservableProperty] private double? _lastDownMbps;
    [ObservableProperty] private double? _lastUpMbps;
    [ObservableProperty] private string _lastPingText = "Noch kein Speedtest";
    [ObservableProperty] private string _cpuSub = "";
    [ObservableProperty] private string _gpuSub = "";
    [ObservableProperty] private string _ramSub = "";
    [ObservableProperty] private string _storageSub = "";
    [ObservableProperty] private IReadOnlyList<TempRow> _temperatures = [];
    [ObservableProperty] private IReadOnlyList<double>? _healthHistory;
    [ObservableProperty] private string? _healthHistoryText;

    // Letzte erhobene Daten (für Score und Bericht)
    private IReadOnlyList<SecurityItem>? _security;
    private RebootStatus? _reboot;
    private IReadOnlyList<ServiceStatus>? _services;
    private CrashSummary? _crashes;
    private IReadOnlyList<StartupEntry>? _startup;
    private IReadOnlyList<AdapterInfo>? _adapters;

    protected override async Task LoadAsync()
    {
        var hwTask = Task.Run(() => AppServices.Hardware.Collect());
        var secTask = Task.Run(() => AppServices.Security.GetStatus());
        var rebootTask = Task.Run(() => AppServices.Health.GetRebootStatus());
        var svcTask = Task.Run(() => AppServices.Health.GetServiceStatus());
        var startupTask = Task.Run(() => AppServices.Startup.GetEntries());
        var progTask = Task.Run(() => AppServices.Programs.GetPrograms().Count);
        var adapterTask = Task.Run(() => AppServices.Network.GetAdapters());
        var crashTask = Task.Run(() => AppServices.Health.GetCrashSummary());

        var hw = await hwTask;
        AppServices.LastHardware = hw;
        ApplyHardware(hw);

        _security = await secTask;
        var problems = _security.Count(s => s.State == DataState.Problem);
        SecurityCard.State = problems > 0 ? DataState.Problem : _security.Any(s => s.State == DataState.Warning) ? DataState.Warning : DataState.Ok;
        SecurityCard.Value = problems > 0 ? "HANDLUNGSBEDARF" : SecurityCard.State == DataState.Ok ? "PROTECTED" : "HINWEISE";
        SecurityCard.Detail = problems > 0 ? $"{problems} Problem(e)" : $"{_security.Count} Prüfpunkte";

        _reboot = await rebootTask;
        _services = await svcTask;
        var svcProblems = _services.Count(s => s.Health == DataState.Problem);
        WindowsCard.State = svcProblems > 0 ? DataState.Problem : _reboot.RebootRequired ? DataState.Warning : DataState.Ok;
        WindowsCard.Value = WindowsCard.State switch { DataState.Ok => "GOOD", DataState.Warning => "HINWEIS", _ => "PROBLEM" };
        WindowsCard.Detail = _reboot.RebootRequired ? "Neustart ausstehend" : svcProblems > 0 ? $"{svcProblems} Dienst(e) deaktiviert" : "Dienste und Neustartstatus OK";

        _startup = await startupTask;
        var enabled = _startup.Count(s => s.Enabled);
        StartupCard.Value = $"{enabled} aktiv";
        StartupCard.Detail = $"{_startup.Count} Einträge gesamt";
        StartupCard.State = enabled > 15 ? DataState.Warning : DataState.Ok;

        SoftwareCard.Value = $"{await progTask} Programme";
        SoftwareCard.Detail = "Update-Prüfung (winget) folgt in Phase 3";
        SoftwareCard.State = DataState.Ok;

        DriversCard.Value = "Inventur";
        DriversCard.Detail = "Alle Gerätetreiber anzeigen";
        DriversCard.State = DataState.Ok;

        _adapters = await adapterTask;
        var last = AppServices.Speedtest.LoadHistory().LastOrDefault();
        var online = _adapters.Any(a => a.IsUp && a.Gateways.Count > 0);
        InternetCard.State = online ? DataState.Ok : DataState.Problem;
        InternetCard.Value = last is null ? (online ? "Verbunden" : "Offline") : $"↓ {Format.BitsPerSecond(last.DownloadBps)}";
        InternetCard.Detail = last is null ? "Noch kein Speedtest" : $"↑ {Format.BitsPerSecond(last.UploadBps)} · Ping {last.PingMs:0} ms · {last.Time:dd.MM. HH:mm}";

        _crashes = await crashTask;
        UpdateScore(hw);

        LastDownMbps = last?.DownloadBps / 1e6;
        LastUpMbps = last?.UploadBps / 1e6;
        LastPingText = last is null ? "Noch kein Speedtest" : $"Ping {last.PingMs:0} ms · Jitter {last.JitterMs:0.0} ms";

        SecurityHighlights = _security.Where(x => x.Area != "Application Integrity" && x.Area != "Security-Ereignisse (7 Tage)").Take(5).ToList();
        BuildHealthRows(hw);
        BuildUpdateRows();
        UpdatesViewModel.CountsChanged -= OnCountsChanged;
        UpdatesViewModel.CountsChanged += OnCountsChanged;

        // PC-Health-Verlauf (§103) – höchstens ein Wert pro Stunde in der lokalen Datenbank
        if (Score is not null)
        {
            var score = Score.Score;
            await Task.Run(() => AppServices.History.AddHealthScore(score));
        }
        var history = await Task.Run(() => AppServices.History.GetHealthHistory(30));
        HealthHistory = history.Count >= 2 ? history.Select(h => (double)h.Score).ToArray() : null;
        HealthHistoryText = history.Count >= 2 ? $"Verlauf 30 Tage: min. {history.Min(h => h.Score)}, max. {history.Max(h => h.Score)}" : "Verlauf entsteht mit der Nutzung (1 Wert/Stunde).";

        // Benachrichtigungen (§83)
        foreach (var v in hw.Volumes.Where(v => v.UsedPercent >= 90))
            AppServices.Notifications.Notify(Services.SystemTools.NotificationKind.LowDiskSpace, "Speicherplatz knapp",
                $"Laufwerk {v.Letter} ist zu {v.UsedPercent:0} % belegt.");
        if (_security.Any(x => x.State == DataState.Problem && x.Area != "Application Integrity"))
            AppServices.Notifications.Notify(Services.SystemTools.NotificationKind.SecurityAlert, "Sicherheitshinweis",
                string.Join(", ", _security.Where(x => x.State == DataState.Problem).Select(x => $"{x.Area}: {x.Status}")));

        UpdateHero();
        OnNavigatedTo();
    }

    private void OnCountsChanged() => System.Windows.Application.Current?.Dispatcher.InvokeAsync(BuildUpdateRows);

    private void BuildUpdateRows()
    {
        var d = UpdatesViewModel.DriverCount;
        var sw = UpdatesViewModel.SoftwareCount;
        UpdateRows =
        [
            new InfoItem("Windows Update", _reboot?.RebootRequired == true ? "Neustart ausstehend" : "Kein Neustart ausstehend",
                _reboot?.RebootRequired == true ? DataState.Warning : DataState.Ok),
            d is null ? InfoItem.Unavailable("Treiber-Updates", "Noch nicht geprüft") : new InfoItem("Treiber-Updates", d == 0 ? "Keine" : $"{d} verfügbar", d == 0 ? DataState.Ok : DataState.Warning),
            sw is null ? InfoItem.Unavailable("Software-Updates", "Noch nicht geprüft") : new InfoItem("Software-Updates", sw == 0 ? "Keine" : $"{sw} verfügbar", sw == 0 ? DataState.Ok : DataState.Warning),
        ];
        DriversCard.Value = d is null ? "Inventur" : d == 0 ? "Aktuell" : $"{d} Updates";
        DriversCard.State = d is > 0 ? DataState.Warning : DataState.Ok;
        SoftwareCard.Detail = sw is null ? "Update-Prüfung über winget" : sw == 0 ? "Alle Programme aktuell" : $"{sw} Software-Update(s) verfügbar";
    }

    [RelayCommand]
    private void OpenUpdates() => _nav.Navigate(_nav.Page<UpdatesViewModel>());

    private void BuildHealthRows(HardwareSnapshot? hw)
    {
        var svcBad = _services?.Count(x => x.Health == DataState.Problem) ?? 0;
        var diskBad = hw?.Disks.Any(d => d.HealthState == DataState.Problem) == true;
        HealthRows =
        [
            new InfoItem("Windows-Dienste", svcBad == 0 ? "OK" : $"{svcBad} deaktiviert", svcBad == 0 ? DataState.Ok : DataState.Problem),
            new InfoItem("Datenträger", hw is null || hw.Disks.Count == 0 ? "nicht verfügbar" : diskBad ? "Fehler gemeldet" : "OK",
                hw is null || hw.Disks.Count == 0 ? DataState.NotAvailable : diskBad ? DataState.Problem : DataState.Ok),
            new InfoItem("Stabilität (30 T.)", _crashes is null ? "–" : _crashes.BugChecks == 0 ? "Keine Bluescreens" : $"{_crashes.BugChecks} Bluescreen(s)",
                _crashes is null ? DataState.NotAvailable : _crashes.BugChecks == 0 ? DataState.Ok : DataState.Problem),
            new InfoItem("SFC / DISM", "Prüfung unter Windows Health", DataState.NotAvailable),
        ];
    }

    private void UpdateHero()
    {
        var states = new[] { HealthCard.State, WindowsCard.State, SecurityCard.State, StorageCard.State };
        HeroState = states.Contains(DataState.Problem) ? DataState.Problem : states.Contains(DataState.Warning) ? DataState.Warning : DataState.Ok;
        (HeroStatus, HeroText) = HeroState switch
        {
            DataState.Ok => ("GOOD", "Dein System läuft stabil. Keine Probleme erkannt."),
            DataState.Warning => ("HINWEISE", "Einige Punkte verdienen Aufmerksamkeit – Details unten."),
            _ => ("HANDLUNGSBEDARF", "Es wurden Probleme erkannt – Details unten."),
        };
        if (AppServices.LastHardware is { } hw)
        {
            OsText = hw.Os.Caption.Replace("Microsoft ", "", StringComparison.Ordinal);
            OsBuild = $"Version {hw.Os.DisplayVersion} · Build {hw.Os.Build}";
        }
    }

    private void UpdateScore(HardwareSnapshot? hw)
    {
        Score = HealthScore.Calculate(hw, _security, _reboot, _services, _crashes);
        HealthCard.Value = $"{Score.Score} / 100";
        HealthCard.Detail = Score.Factors.Count == 0 ? $"{Score.Rating} – keine Abzüge" : $"{Score.Rating} – {Score.Factors.Count} Abzug/Abzüge";
        HealthCard.State = Score.State;
        HealthCard.Percent = Score.Score;
    }

    private void ApplyHardware(HardwareSnapshot hw)
    {
        CpuCard.Value = hw.Cpu?.Name ?? "nicht verfügbar";
        CpuCard.State = hw.Cpu is null ? DataState.NotAvailable : DataState.Ok;
        CpuCard.Detail = hw.Cpu is null ? null : $"{hw.Cpu.Cores} Kerne / {hw.Cpu.Threads} Threads";

        var gpu = hw.Gpus.FirstOrDefault(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) ?? hw.Gpus.FirstOrDefault();
        GpuCard.Value = gpu?.Name ?? "nicht verfügbar";
        GpuCard.Detail = gpu is null ? null : $"VRAM {Format.Bytes(gpu.VramBytes)} · Treiber {Format.OrNa(gpu.DriverVersion)}";
        GpuCard.State = gpu is null ? DataState.NotAvailable : DataState.Ok;

        BoardCard.Value = $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim() is { Length: > 0 } b ? b : "nicht verfügbar";
        BoardCard.Detail = $"BIOS {Format.OrNa(hw.Board.BiosVersion)} · {hw.Board.Firmware}";
        BoardCard.State = DataState.Ok;

        var sys = hw.Volumes.FirstOrDefault(v => v.Letter.StartsWith(Path.GetPathRoot(Environment.SystemDirectory)?[..1] ?? "C", StringComparison.OrdinalIgnoreCase)) ?? hw.Volumes.FirstOrDefault();
        if (sys is not null)
        {
            StorageCard.Value = $"{Format.Bytes(sys.UsedBytes)} / {Format.Bytes(sys.SizeBytes)}";
            StorageCard.Detail = $"{sys.Letter} · {Format.Bytes(sys.FreeBytes)} frei";
            StorageCard.Percent = sys.UsedPercent;
            StorageCard.State = sys.UsedPercent >= 95 ? DataState.Problem : sys.UsedPercent >= 85 ? DataState.Warning : DataState.Ok;
        }
        if (hw.Disks.Any(d => d.HealthState == DataState.Problem)) StorageCard.State = DataState.Problem;

        CpuSub = hw.Cpu is null ? "nicht verfügbar" : $"{hw.Cpu.Cores} Kerne / {hw.Cpu.Threads} Threads";
        GpuSub = gpu is null ? "nicht verfügbar" : $"{Format.Bytes(gpu.VramBytes)} VRAM";
        var mod = hw.Memory.Modules.FirstOrDefault();
        RamSub = mod?.SpeedMTs is { } mts ? $"{mts} MT/s · {hw.Memory.Modules.Count} Modul(e)" : $"{hw.Memory.Modules.Count} Modul(e)";
        var disk = hw.Disks.FirstOrDefault();
        StorageSub = disk is null ? "" : $"{disk.MediaType} · {disk.Name}";

        TempCard.Value = "–";
        TempCard.Detail = "Sensoren werden gelesen …";
        TempCard.State = DataState.NotAvailable;
    }

    public override void OnNavigatedTo()
    {
        MonitorHub.SampleArrived -= OnSample;
        MonitorHub.SampleArrived += OnSample;
        if (MonitorHub.Latest is { } s) OnSample(s);
    }

    public override void OnNavigatedFrom() => MonitorHub.SampleArrived -= OnSample;

    private void OnSample(MonitorSample s)
    {
        var smart = SmartService.LoadCached();
        var ssd = smart?.Disks.FirstOrDefault(d => d.TemperatureC is not null);
        var mgr = AppServices.Monitor.SensorManager;
        TempRow? Unified(UnifiedSensorId id, string label)
        {
            var u = mgr.Best(id);
            if (u is null) return null;
            var c = (int)Math.Round(u.Current);
            return new TempRow(label, c, $"{c} °C", $"{u.SourceName} · max. {u.Max:0} °C");
        }
        var rows = new List<TempRow>
        {
            Unified(UnifiedSensorId.CpuPackageTemp, "CPU Package") ?? Unified(UnifiedSensorId.CpuCoreTemp, "CPU-Kern (max.)")
                ?? new TempRow("CPU", null, "n. v.", "HWiNFO oder LibreHardwareMonitor starten (Seite „Sensorquellen“)"),
            Unified(UnifiedSensorId.GpuCoreTemp, "GPU-Kern")
                ?? new TempRow("GPU", null, "n. v.", "Der Grafiktreiber meldet keine Temperatur"),
        };
        if (Unified(UnifiedSensorId.GpuHotspotTemp, "GPU Hotspot") is { } hot) rows.Add(hot);
        if (Unified(UnifiedSensorId.GpuMemoryTemp, "GPU-Speicher") is { } gmem) rows.Add(gmem);
        if (Unified(UnifiedSensorId.VrmTemp, "VRM") is { } vrm) rows.Add(vrm);
        if (Unified(UnifiedSensorId.MainboardTemp, "Mainboard") is { } mb) rows.Add(mb);
        rows.Add(Unified(UnifiedSensorId.NvmeTemp, "NVMe / SSD")
                 ?? new TempRow("NVMe / SSD", ssd?.TemperatureC, ssd?.TemperatureC is { } st ? $"{st} °C" : "n. v.",
                     ssd is null ? "„SMART lesen“ auf der Hardware-Seite" : $"SMART, Stand {smart!.Time:dd.MM. HH:mm}"));
        Temperatures = rows.Take(6).ToList();
        var connected = mgr.Sources.Where(x => x.State == SensorSourceState.Connected).Select(x => x.Name).ToList();
        SensorSourceText = connected.Count == 0 ? "Keine Sensorquelle verbunden" : "Quellen: " + string.Join(" · ", connected);
        var maxTemp = Temperatures.Where(t => t.Celsius is not null).MaxBy(t => t.Celsius);
        TempCard.Value = maxTemp is null ? "n. v." : $"max. {maxTemp.Celsius} °C";
        TempCard.Detail = maxTemp is null ? "Keine Sensorquelle meldet Werte" : $"{maxTemp.Label} · {maxTemp.Source}";
        TempCard.State = maxTemp?.State ?? DataState.NotAvailable;
        if (s.GpuPercent is { } gp)
        {
            GpuCard.Percent = gp;
            GpuSub = $"Last {Format.Percent(gp)}";
        }
        UptimeText = Format.Duration(TimeSpan.FromMilliseconds(Environment.TickCount64));
        var recent = MonitorHub.Recent(120);
        CpuHistory = recent.Select(x => x.CpuPercent ?? 0).ToArray();
        RamHistory = recent.Select(x => x.RamPercent ?? 0).ToArray();
        DiskHistory = recent.Select(x => x.DiskActivePercent ?? 0).ToArray();
        GpuHistory = recent.Select(x => x.GpuPercent ?? 0).ToArray();

        // VRAM: belegter dedizierter Grafikspeicher (Leistungsindikator) im Verhältnis zur installierten Größe.
        var gpuInfo = AppServices.LastHardware?.Gpus.FirstOrDefault(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase));
        if (s.VramUsedBytes is { } vu && gpuInfo?.VramBytes is > 0 and var vt)
        {
            var pct = Math.Clamp(100.0 * vu / vt, 0, 100);
            VramCard.Percent = pct;
            VramCard.Value = $"{Format.Bytes(vu)} / {Format.Bytes(vt)}";
            VramCard.Detail = $"{Format.Percent(pct)} belegt";
            VramCard.State = pct >= 95 ? DataState.Warning : DataState.Ok;
        }
        else
        {
            VramCard.Value = gpuInfo?.VramBytes is { } installed ? $"{Format.Bytes(installed)} installiert" : "nicht verfügbar";
            VramCard.Detail = "Belegung: Leistungsindikator nicht verfügbar";
            VramCard.State = DataState.NotAvailable;
        }

        // Netzwerk: aktueller Durchsatz und Auslastung der schnellsten aktiven Verbindung.
        var link = _adapters?.Where(a => a.IsUp && a.Gateways.Count > 0).Select(a => a.LinkSpeedBps ?? 0).Where(x => x > 0).DefaultIfEmpty(0).Max() ?? 0;
        NetworkCard.Value = $"↓ {Format.BitsPerSecond(s.DownloadBitsPerSecond)}";
        NetworkCard.Detail = $"↑ {Format.BitsPerSecond(s.UploadBitsPerSecond)}" + (link > 0 ? $" · Link {Format.BitsPerSecond(link)}" : "");
        NetworkCard.Percent = link > 0 ? Math.Clamp(100.0 * Math.Max(s.DownloadBitsPerSecond, s.UploadBitsPerSecond) / link, 0, 100) : null;
        NetworkCard.State = _adapters is null ? DataState.NotAvailable : _adapters.Any(a => a.IsUp && a.Gateways.Count > 0) ? DataState.Ok : DataState.Problem;

        if (s.CpuPercent is { } cpu)
        {
            CpuCard.Percent = cpu;
            if (AppServices.LastHardware?.Cpu is { } c) CpuCard.Detail = $"{c.Cores} Kerne / {c.Threads} Threads · Last {Format.Percent(cpu)}";
        }
        if (s.RamPercent is { } ram && AppServices.LastHardware?.Memory.TotalBytes is { } total)
        {
            RamCard.Value = $"{Format.Bytes(s.RamUsedBytes)} / {Format.Bytes(total)}";
            RamCard.Percent = ram;
            RamCard.Detail = $"{Format.Percent(ram)} belegt";
            RamCard.State = ram >= 90 ? DataState.Warning : DataState.Ok;
        }
    }

    [RelayCommand]
    private void OpenCard(DashboardCard? card)
    {
        PageViewModel? target = card?.Title switch
        {
            "PC Health" or "Windows Health" => _nav.Page<HealthViewModel>(),
            "Security" => _nav.Page<SecurityViewModel>(),
            "CPU" or "GPU" or "RAM" or "VRAM" or "Mainboard" or "Storage" => _nav.Page<HardwareViewModel>(),
            "Temperaturen" => _nav.Page<SensorSourcesViewModel>(),
            "Internet" or "Netzwerk" => _nav.Page<NetworkViewModel>(),
            "Treiber" => _nav.Page<DriversViewModel>(),
            "Software" => _nav.Page<ProgramsViewModel>(),
            "Autostart" => _nav.Page<StartupViewModel>(),
            _ => null,
        };
        if (target is not null) _nav.Navigate(target);
    }

    // ---------------- Zentrale Aktionen (v1.1 Dashboard) ----------------

    [RelayCommand]
    private void OpenReport() => _nav.Navigate(_nav.Page<ReportViewModel>());

    [RelayCommand]
    private void TestComponents() => _nav.Navigate(_nav.Page<ComponentTestViewModel>());

    [RelayCommand]
    private void NetworkTest() => _nav.Navigate(_nav.Page<NetworkScannerViewModel>());

    [RelayCommand]
    private void GameMode() => _nav.Navigate(_nav.Page<GamingViewModel>());

    [RelayCommand]
    private async Task SecurityCheckAsync()
    {
        var page = _nav.Page<SecurityViewModel>();
        _nav.Navigate(page);
        await page.RefreshAsync();
    }

    [RelayCommand]
    private void OpenSensors() => _nav.Navigate(_nav.Page<SensorSourcesViewModel>());

    // ---------------- Quick Actions (§13) ----------------

    [RelayCommand]
    private void OrganizeDesktop() => _nav.Navigate(_nav.Page<DesktopOrganizerViewModel>());

    [RelayCommand]
    private void EmptyRecycleBin()
    {
        var info = AppServices.RecycleBin.Query();
        if (info is null) { StatusMessage = "Papierkorb konnte nicht abgefragt werden."; return; }
        if (info.ItemCount == 0) { StatusMessage = "Der Papierkorb ist bereits leer."; return; }
        if (!Dialogs.Confirm("Papierkorb leeren",
                $"{info.ItemCount} Element(e) mit insgesamt {Format.Bytes(info.SizeBytes)} werden endgültig gelöscht.\n\nDies kann nicht rückgängig gemacht werden. Fortfahren?", true))
            return;
        var handle = new System.Windows.Interop.WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;
        ShowResult(AppServices.RecycleBin.Empty(handle));
    }

    [RelayCommand]
    private void SystemCleanup() => _nav.Navigate(_nav.Page<CleanupViewModel>());

    [RelayCommand]
    private void Speedtest() => _nav.Navigate(_nav.Page<SpeedtestViewModel>());

    [RelayCommand]
    private void CheckWindows() => _nav.Navigate(_nav.Page<HealthViewModel>());

    [RelayCommand]
    private void OpenCmd() => ShowResult(AppServices.Tools.Launch(SystemTool.Cmd));

    [RelayCommand]
    private void OpenPowerShell() => ShowResult(AppServices.Tools.Launch(SystemTool.PowerShell));

    [RelayCommand]
    private void Sleep()
    {
        if (Dialogs.Confirm("Energiesparmodus", "PC jetzt in den Energiesparmodus versetzen?")) ShowResult(AppServices.Power.Sleep());
    }

    [RelayCommand]
    private void LockPc() => ShowResult(AppServices.Power.Lock());

    [RelayCommand]
    private Task Restart() => ToolsViewModel.ConfirmPowerAsync(true, SetStatus);

    [RelayCommand]
    private Task Shutdown() => ToolsViewModel.ConfirmPowerAsync(false, SetStatus);

    internal void SetStatus(string message) => StatusMessage = message;

    // ---------------- Complete PC Check (§49) ----------------

    [RelayCommand]
    private async Task CompleteCheckAsync()
    {
        if (IsCheckRunning) return;
        IsCheckRunning = true;
        CheckSummary = null;
        CheckSteps.Clear();
        string[] names =
        [
            "Windows", "Systemdateien (SFC)", "Komponentenspeicher (DISM)", "Laufwerke", "Datenträger-Gesundheit", "Dienste",
            "Updates / Neustart", "Sicherheit", "Netzwerk", "Autostart", "Ereignisprotokolle", "Hardware", "Temperaturen",
        ];
        foreach (var n in names) CheckSteps.Add(new CheckStep(n));
        CheckStep S(string n) => CheckSteps.First(c => c.Name == n);

        async Task Step(string name, Func<Task<(DataState, string)>> work)
        {
            var step = S(name);
            step.IsRunning = true;
            step.Detail = "Wird geprüft …";
            try
            {
                var (state, detail) = await work();
                step.State = state;
                step.Detail = detail;
            }
            catch (Exception ex)
            {
                step.State = DataState.NotAvailable;
                step.Detail = "Prüfung nicht möglich: " + ex.Message;
            }
            finally { step.IsRunning = false; }
        }

        HardwareSnapshot? hw = null;
        try
        {
            await Step("Windows", async () =>
            {
                hw = await Task.Run(() => AppServices.Hardware.Collect());
                AppServices.LastHardware = hw;
                return (DataState.Ok, $"{hw.Os.Caption} {hw.Os.DisplayVersion} (Build {hw.Os.Build})");
            });

            if (IncludeRepairScanInCheck)
            {
                await Step("Systemdateien (SFC)", async () =>
                {
                    var r = await AppServices.Health.RunAsync(AllowedCommand.SfcScan, null, null, CancellationToken.None);
                    return (r.State, r.Summary);
                });
                await Step("Komponentenspeicher (DISM)", async () =>
                {
                    var r = await AppServices.Health.RunAsync(AllowedCommand.DismCheckHealth, null, null, CancellationToken.None);
                    return (r.State, r.Summary);
                });
            }
            else
            {
                S("Systemdateien (SFC)").Detail = "Übersprungen (Option „inkl. SFC/DISM“ aktivieren)";
                S("Komponentenspeicher (DISM)").Detail = "Übersprungen";
            }

            await Step("Laufwerke", () => Task.FromResult(hw is null ? (DataState.NotAvailable, "–")
                : hw.Volumes.Any(v => v.UsedPercent >= 95) ? (DataState.Problem, "Mindestens ein Laufwerk ist fast voll")
                : hw.Volumes.Any(v => v.UsedPercent >= 85) ? (DataState.Warning, "Wenig freier Speicher auf mindestens einem Laufwerk")
                : (DataState.Ok, string.Join(", ", hw.Volumes.Select(v => $"{v.Letter} {Format.Bytes(v.FreeBytes)} frei")))));

            await Step("Datenträger-Gesundheit", () => Task.FromResult(hw is null || hw.Disks.Count == 0 ? (DataState.NotAvailable, "Keine Daten")
                : hw.Disks.Any(d => d.HealthState == DataState.Problem) ? (DataState.Problem, "Windows meldet einen fehlerhaften Datenträger")
                : hw.Disks.Any(d => d.HealthState == DataState.Warning) ? (DataState.Warning, "Warnung für mindestens einen Datenträger")
                : (DataState.Ok, $"{hw.Disks.Count} Datenträger fehlerfrei (Windows-Status)")));

            await Step("Dienste", async () =>
            {
                _services = await Task.Run(() => AppServices.Health.GetServiceStatus());
                var bad = _services.Where(s => s.Health == DataState.Problem).ToList();
                return bad.Count > 0 ? (DataState.Problem, "Deaktiviert: " + string.Join(", ", bad.Select(b => b.DisplayName))) : (DataState.Ok, "Wichtige Windows-Dienste OK");
            });

            await Step("Updates / Neustart", async () =>
            {
                _reboot = await Task.Run(() => AppServices.Health.GetRebootStatus());
                return _reboot.RebootRequired ? (DataState.Warning, string.Join("; ", _reboot.Reasons)) : (DataState.Ok, "Kein Neustart ausstehend");
            });

            await Step("Sicherheit", async () =>
            {
                _security = await Task.Run(() => AppServices.Security.GetStatus());
                var p = _security.Where(s => s.State == DataState.Problem && s.Area != "Application Integrity").ToList();
                var w = _security.Count(s => s.State == DataState.Warning);
                return p.Count > 0 ? (DataState.Problem, string.Join(", ", p.Select(x => $"{x.Area}: {x.Status}")))
                    : w > 0 ? (DataState.Warning, $"{w} Hinweis(e) – Details im Security Center") : (DataState.Ok, "Schutzfunktionen aktiv");
            });

            await Step("Netzwerk", async () =>
            {
                var steps = await AppServices.Network.DiagnoseAsync();
                var bad = steps.FirstOrDefault(s => s.State == DataState.Problem);
                return bad is not null ? (DataState.Problem, $"{bad.Name}: {bad.Detail}") : (DataState.Ok, "Verbindung bis ins Internet OK");
            });

            await Step("Autostart", async () =>
            {
                _startup = await Task.Run(() => AppServices.Startup.GetEntries());
                var n = _startup.Count(s => s.Enabled);
                var unsigned = _startup.Count(s => s.Enabled && s.Signature?.Status is SignatureStatus.Tampered or SignatureStatus.Untrusted);
                return unsigned > 0 ? (DataState.Warning, $"{n} aktiv, {unsigned} mit ungültiger Signatur")
                    : (n > 15 ? DataState.Warning : DataState.Ok, $"{n} aktive Autostart-Einträge");
            });

            await Step("Ereignisprotokolle", async () =>
            {
                _crashes = await Task.Run(() => AppServices.Health.GetCrashSummary());
                return _crashes.BugChecks > 0 ? (DataState.Problem, $"{_crashes.BugChecks} Bluescreen(s) in 30 Tagen")
                    : _crashes.UnexpectedShutdowns > 2 ? (DataState.Warning, $"{_crashes.UnexpectedShutdowns} unerwartete Neustarts in 30 Tagen")
                    : (DataState.Ok, $"Keine Bluescreens · {_crashes.AppCrashes} Programmabstürze in 30 Tagen");
            });

            await Step("Hardware", () => Task.FromResult(hw is null ? (DataState.NotAvailable, "–")
                : (hw.Warnings.Count > 0 ? DataState.Warning : DataState.Ok,
                   hw.Warnings.Count > 0 ? string.Join(" ", hw.Warnings) : $"{hw.Cpu?.Name} · {Format.Bytes(hw.Memory.TotalBytes)} RAM")));

            await Step("Temperaturen", () =>
            {
                var t = MonitorHub.Latest;
                var hottest = new[] { t?.CpuTempC, t?.GpuTempC, t?.AcpiTempC }.Where(x => x is not null).Max();
                return Task.FromResult(hottest switch
                {
                    null => (DataState.NotAvailable, "Keine Temperaturquelle verfügbar (siehe Seite „Sensoren“)."),
                    >= 90 => (DataState.Problem, $"Sehr hohe Temperatur: {hottest} °C – Kühlung/Lüfter prüfen."),
                    >= 80 => (DataState.Warning, $"Erhöhte Temperatur: {hottest} °C."),
                    _ => (DataState.Ok, $"Temperaturen unauffällig (max. {hottest} °C)."),
                });
            });

            UpdateScore(hw);
            var problems = CheckSteps.Count(c => c.State == DataState.Problem);
            var warnings = CheckSteps.Count(c => c.State == DataState.Warning);
            CheckSummary = $"PC Health Score {Score?.Score}/100 ({Score?.Rating}) · {problems} Problem(e), {warnings} Hinweis(e)";
            AppLog.Info("SystemCheck", "Complete PC Check abgeschlossen", CheckSummary);
            LastCheckText = $"Heute, {DateTime.Now:HH:mm}";
            if (_security is not null) SecurityHighlights = _security.Where(x => x.Area != "Application Integrity" && x.Area != "Security-Ereignisse (7 Tage)").Take(5).ToList();
            BuildHealthRows(hw);
            UpdateHero();
        }
        finally
        {
            IsCheckRunning = false;
        }
    }

    // ---------------- System Report (§82) ----------------

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        if (IncludeSensitiveInReport &&
            !await AppServices.Guard.AuthenticateOnlyAsync("Bericht mit sensiblen Daten exportieren"))
            return;

        var path = Dialogs.SaveFile($"UltimateX-Bericht-{DateTime.Now:yyyyMMdd-HHmm}", "Textdatei (*.txt)|*.txt|JSON (*.json)|*.json");
        if (path is null) return;

        int? driverCount = null;
        var input = new SystemReport.Input(AppServices.LastHardware, _adapters, _security, _startup, _reboot, _services, _crashes, Score, driverCount);
        var report = await Task.Run(() => SystemReport.Build(input, IncludeSensitiveInReport));
        var content = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? SystemReport.ToJson(report) : SystemReport.ToText(report);
        try
        {
            SystemReport.Save(path, content, IncludeSensitiveInReport);
            StatusMessage = "Bericht gespeichert: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Bericht konnte nicht gespeichert werden: " + ex.Message;
        }
    }
}

/// <summary>Zeile der Temperatur-Kachel. Ohne Messwert bleibt der Balken leer – es wird nichts geschätzt.</summary>
public sealed record TempRow(string Label, int? Celsius, string ValueText, string Source)
{
    public double Percent => Celsius is { } c ? Math.Clamp(c, 0, 100) : 0;
    public DataState State => Celsius switch { null => DataState.NotAvailable, >= 85 => DataState.Problem, >= 70 => DataState.Warning, _ => DataState.Ok };
}
