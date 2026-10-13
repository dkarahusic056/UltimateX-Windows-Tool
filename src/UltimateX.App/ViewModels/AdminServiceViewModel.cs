using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;

namespace UltimateX.App.ViewModels;

public sealed record ServiceRow(string Name, string DisplayName, string State, string StartMode, string Account, string Path, DataState Health);

/// <summary>Alle Windows-Dienste (Admin/Service Mode, §10, §45). Nur lesend – Änderungen über services.msc.</summary>
public sealed partial class ServicesViewModel : PageViewModel
{
    public override string Title => "Dienste";
    public override string Glyph => "\uE912";
    public override string Keywords => "dienste services windows dienst starttyp";

    private List<ServiceRow> _all = [];
    public ObservableCollection<ServiceRow> Rows { get; } = [];
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _runningOnly;

    partial void OnFilterChanged(string value) => Apply();
    partial void OnRunningOnlyChanged(bool value) => Apply();

    protected override async Task LoadAsync()
    {
        var list = await Task.Run(() => AppServices.Inventory.GetServices());
        _all = list.Select(s => new ServiceRow(s.Name, s.DisplayName,
            s.State switch { "Running" => "Läuft", "Stopped" => "Beendet", _ => s.State },
            s.StartMode switch { "Auto" => "Automatisch", "Manual" => "Manuell", "Disabled" => "Deaktiviert", _ => s.StartMode },
            s.Account ?? "–", s.Path ?? "–",
            s.StartMode == "Auto" && s.State != "Running" ? DataState.Warning : s.State == "Running" ? DataState.Ok : DataState.NotAvailable)).ToList();
        Apply();
    }

    private void Apply()
    {
        Rows.Clear();
        var q = Filter.Trim();
        foreach (var r in _all)
        {
            if (RunningOnly && r.State != "Läuft") continue;
            if (q.Length > 0 && !r.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)) continue;
            Rows.Add(r);
        }
    }
}

public sealed record EventRow(string Time, string Level, string Provider, long EventId, string Message, DataState State);

/// <summary>Windows-Ereignisprotokolle (System/Application/Setup) – Ansicht "Windows Logs" (§10, §46).</summary>
public sealed partial class EventsViewModel : PageViewModel
{
    public override string Title => "Windows Logs";
    public override string Glyph => "\uE81C";
    public override string Keywords => "ereignisanzeige event log windows logs ereignis fehler warnung";

    public ObservableCollection<EventRow> Rows { get; } = [];

    [ObservableProperty] private string _log = "System";
    [ObservableProperty] private bool _includeWarnings;

    public bool LogSystem { get => Log == "System"; set { if (value) Log = "System"; } }
    public bool LogApplication { get => Log == "Application"; set { if (value) Log = "Application"; } }
    public bool LogSetup { get => Log == "Setup"; set { if (value) Log = "Setup"; } }

    partial void OnLogChanged(string value)
    {
        OnPropertyChanged(nameof(LogSystem));
        OnPropertyChanged(nameof(LogApplication));
        OnPropertyChanged(nameof(LogSetup));
        _ = RefreshAsync();
    }

    partial void OnIncludeWarningsChanged(bool value) => _ = RefreshAsync();

    protected override async Task LoadAsync()
    {
        var log = Log;
        var warn = IncludeWarnings;
        var events = await Task.Run(() => AppServices.Health.GetRecentEvents(log, warn));
        Rows.Clear();
        foreach (var e in events)
            Rows.Add(new EventRow(e.Time?.ToString("dd.MM.yyyy HH:mm:ss") ?? "–", e.Level, e.Provider, e.EventId,
                (e.Message ?? "").Split('\n')[0].Trim(),
                e.Level switch { "Kritisch" or "Fehler" => DataState.Problem, "Warnung" => DataState.Warning, _ => DataState.Ok }));
    }
}

public sealed record AdminTab(string Title, PageViewModel Page);

/// <summary>
/// Admin / Service Mode (§10): technische Gesamtansicht mit Bereichen Hardware, Drivers, Processes, Services,
/// Startup, Network, Storage, Windows Logs, Diagnostics, Security und Export – die Bereiche sind die
/// regulären Seiten, eingebettet in eine Registerleiste.
/// </summary>
public sealed partial class AdminServiceViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public AdminServiceViewModel(MainViewModel main)
    {
        _main = main;
    }

    public override string Title => "Admin / Service Mode";
    public override string Glyph => "\uE7EF";
    public override string Keywords => "admin service techniker übersicht export tabs";

    public ObservableCollection<AdminTab> Tabs { get; } = [];

    [ObservableProperty] private AdminTab? _selectedTab;

    partial void OnSelectedTabChanged(AdminTab? oldValue, AdminTab? newValue)
    {
        oldValue?.Page.OnNavigatedFrom();
        if (newValue is null) return;
        newValue.Page.OnNavigatedTo();
        _ = newValue.Page.EnsureLoadedAsync();
    }

    protected override Task LoadAsync()
    {
        if (Tabs.Count == 0)
        {
            Tabs.Add(new AdminTab("Hardware", _main.Page<HardwareViewModel>()));
            Tabs.Add(new AdminTab("Drivers", _main.Page<DriversViewModel>()));
            Tabs.Add(new AdminTab("Processes", _main.Page<ProcessesViewModel>()));
            Tabs.Add(new AdminTab("Services", _main.Page<ServicesViewModel>()));
            Tabs.Add(new AdminTab("Startup", _main.Page<StartupViewModel>()));
            Tabs.Add(new AdminTab("Network", _main.Page<NetworkViewModel>()));
            Tabs.Add(new AdminTab("Storage", _main.Page<CleanupViewModel>()));
            Tabs.Add(new AdminTab("Windows Logs", _main.Page<EventsViewModel>()));
            Tabs.Add(new AdminTab("Diagnostics", _main.Page<HealthViewModel>()));
            Tabs.Add(new AdminTab("Security", _main.Page<SecurityViewModel>()));
            Tabs.Add(new AdminTab("Export", _main.Page<ExportViewModel>()));
            SelectedTab = Tabs[0];
        }
        return Task.CompletedTask;
    }

    public override void OnNavigatedFrom() => SelectedTab?.Page.OnNavigatedFrom();

    public override void OnNavigatedTo() => SelectedTab?.Page.OnNavigatedTo();

    [RelayCommand]
    private void Select(AdminTab? tab)
    {
        if (tab is not null) SelectedTab = tab;
    }
}

/// <summary>Export-Bereich des Admin/Service Mode (§82, §81): Systembericht und Logs.</summary>
public sealed partial class ExportViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public ExportViewModel(MainViewModel main) => _main = main;

    public override string Title => "Export";
    public override string Glyph => "\uEDE1";
    public override string Keywords => "export bericht report logs json txt";

    public DashboardViewModel Dashboard => _main.Page<DashboardViewModel>();

    protected override Task LoadAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        var dash = _main.Page<DashboardViewModel>();
        await dash.EnsureLoadedAsync();
        await dash.ExportReportCommand.ExecuteAsync(null);
        StatusMessage = dash.StatusMessage;
    }

    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        var logs = _main.Page<LogsViewModel>();
        await logs.EnsureLoadedAsync();
        logs.ExportCommand.Execute(null);
        StatusMessage = logs.StatusMessage;
    }
}
