using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Settings;

namespace UltimateX.App.ViewModels;

public interface INavigator
{
    void Navigate(PageViewModel page);
    T Page<T>() where T : PageViewModel;
}

public sealed record NavItem(PageViewModel Page, bool StartsGroup = false)
{
    public string Title => Page.Title;
    public string Glyph => Page.Glyph;
}

/// <summary>Shell: Navigation, Layoutmodus, globale Suche, LOCK (§5, §11, §61, §102).</summary>
public sealed partial class MainViewModel : ObservableObject, INavigator
{
    private readonly List<PageViewModel> _pages = [];

    public ObservableCollection<NavItem> NavItems { get; } = [];

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private PageViewModel? _currentPage;

    [ObservableProperty]
    private bool _isSimpleMode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _toast;

    public string ElevationText => Elevation.IsElevated ? "Administrator" : "Standardbenutzer";

    public string UserName => Elevation.CurrentUserName;

    public string MachineName => Environment.MachineName;

    [ObservableProperty] private string _clockTime = DateTime.Now.ToString("HH:mm");
    [ObservableProperty] private string _clockDate = DateTime.Now.ToString("ddd, dd. MMM yyyy");
    [ObservableProperty] private string _osHeader = "Windows";
    [ObservableProperty] private string _osHeaderBuild = "";

    // Technische Statusleiste im Kopf (Live-Werte, 1 Hz aus dem MonitorHub)
    [ObservableProperty] private string _hudCpu = "–";
    [ObservableProperty] private string _hudGpu = "–";
    [ObservableProperty] private string _hudRam = "–";
    [ObservableProperty] private string _hudCpuTemp = "–";
    [ObservableProperty] private string _hudGpuTemp = "–";
    [ObservableProperty] private string _hudNet = "–";
    [ObservableProperty] private double _hudCpuValue;
    [ObservableProperty] private double _hudGpuValue;
    [ObservableProperty] private double _hudRamValue;

    public MainViewModel()
    {
        var dashboard = Add(new DashboardViewModel(this));
        Add(new HardwareViewModel(), true);
        Add(new MonitorViewModel());
        Add(new SensorSourcesViewModel());
        Add(new ComponentTestViewModel());
        Add(new ReportViewModel(this));
        Add(new NetworkViewModel(), true);
        Add(new SpeedtestViewModel());
        Add(new NetworkScannerViewModel());
        Add(new UpdatesViewModel(), true);
        Add(new DriversViewModel());
        Add(new ProgramsViewModel());
        Add(new StartupViewModel());
        Add(new ProcessesViewModel());
        Add(new BenchmarkViewModel());
        Add(new CleanupViewModel(), true);
        Add(new DesktopOrganizerViewModel());
        Add(new HealthViewModel(), true);
        Add(new CrashAnalysisViewModel());
        Add(new EventsViewModel());
        Add(new ServicesViewModel());
        Add(new SecurityViewModel());
        Add(new ToolsViewModel(), true);
        Add(new LogsViewModel());
        Add(new SettingsViewModel(this));
        // Layouts (§5–§10) – auch direkt über die Navigation erreichbar
        Add(new GamingViewModel(), true);
        Add(new TechnicianViewModel());
        Add(new PcMapViewModel(this));
        Add(new AdminServiceViewModel(this));

        // Seiten ohne eigenen Navigationseintrag
        _pages.Add(new SimpleViewModel(this));
        _pages.Add(new ExportViewModel(this));

        AppServices.Auth.SessionMinutes = AppServices.Settings.SecuritySessionMinutes;

        // Kopfzeile: Uhrzeit und Windows-Version (aktualisiert mit dem 1-Hz-Monitor-Takt).
        MonitorHub.SampleArrived += sample =>
        {
            HudCpu = sample.CpuPercent is { } c ? $"{c:0} %" : "–";
            HudGpu = sample.GpuPercent is { } g ? $"{g:0} %" : "–";
            HudRam = sample.RamPercent is { } r ? $"{r:0} %" : "–";
            HudCpuValue = sample.CpuPercent ?? 0;
            HudGpuValue = sample.GpuPercent ?? 0;
            HudRamValue = sample.RamPercent ?? 0;
            HudCpuTemp = sample.CpuTempC is { } ct ? $"{ct} °C" : "–";
            HudGpuTemp = sample.GpuTempC is { } gt ? $"{gt} °C" : "–";
            HudNet = $"↓ {Services.Common.Format.BitsPerSecond(sample.DownloadBitsPerSecond)}  ↑ {Services.Common.Format.BitsPerSecond(sample.UploadBitsPerSecond)}";
            var now = DateTime.Now;
            ClockTime = now.ToString("HH:mm");
            ClockDate = now.ToString("ddd, dd. MMM yyyy");
            if (AppServices.LastHardware is { } hw)
            {
                OsHeader = hw.Os.Caption.Replace("Microsoft ", "", StringComparison.Ordinal);
                OsHeaderBuild = "Build " + hw.Os.Build;
            }
        };
        ApplyLayout(AppServices.Settings.Layout, AppServices.Settings.Layout == LayoutMode.ModernControlCenter ? dashboard : null);
    }

    private PageViewModel Add(PageViewModel page, bool startsGroup = false)
    {
        _pages.Add(page);
        NavItems.Add(new NavItem(page, startsGroup));
        return page;
    }

    public T Page<T>() where T : PageViewModel => _pages.OfType<T>().First();

    /// <summary>Layout-Switcher (§11): bestimmt Startseite und ob die Sidebar sichtbar ist.</summary>
    public void ApplyLayout(LayoutMode layout, PageViewModel? start = null)
    {
        IsSimpleMode = layout == LayoutMode.SimpleMode;
        PageViewModel target = layout switch
        {
            LayoutMode.SimpleMode => Page<SimpleViewModel>(),
            LayoutMode.GamingMode => start ?? Page<GamingViewModel>(),
            LayoutMode.TechnicianMode => start ?? Page<TechnicianViewModel>(),
            LayoutMode.VisualPcMap => start ?? Page<PcMapViewModel>(),
            LayoutMode.AdminServiceMode => start ?? Page<AdminServiceViewModel>(),
            _ => start ?? CurrentPage ?? Page<DashboardViewModel>(),
        };
        Navigate(target);
    }

    /// <summary>Startseite des gewählten Layouts (für "Zurück"/Home).</summary>
    [RelayCommand]
    private void Home() => ApplyLayout(AppServices.Settings.Layout, AppServices.Settings.Layout switch
    {
        LayoutMode.ModernControlCenter => Page<DashboardViewModel>(),
        _ => null,
    });

    public void Navigate(PageViewModel page)
    {
        if (ReferenceEquals(CurrentPage, page)) return;
        CurrentPage?.OnNavigatedFrom();
        CurrentPage = page;
        var nav = NavItems.FirstOrDefault(n => ReferenceEquals(n.Page, page));
        if (!ReferenceEquals(SelectedNav, nav)) SelectedNav = nav;
        page.OnNavigatedTo();
        _ = page.EnsureLoadedAsync();
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is not null) Navigate(value.Page);
    }

    public bool ShowBackToSimple => IsSimpleMode && CurrentPage is not SimpleViewModel;

    partial void OnCurrentPageChanged(PageViewModel? value) => OnPropertyChanged(nameof(ShowBackToSimple));

    partial void OnIsSimpleModeChanged(bool value) => OnPropertyChanged(nameof(ShowBackToSimple));

    [RelayCommand]
    private void BackToSimple() => Navigate(Page<SimpleViewModel>());

    /// <summary>Globale Suche: öffnet die erste Seite, deren Titel oder Stichworte passen.</summary>
    [RelayCommand]
    private void Search()
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return;
        var hit = _pages.FirstOrDefault(p => p.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                             || Core.Localization.Loc.T(p.Title).Contains(q, StringComparison.CurrentCultureIgnoreCase))
                  ?? _pages.FirstOrDefault(p => p.Keywords.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Any(k => k.StartsWith(q, StringComparison.CurrentCultureIgnoreCase) || q.StartsWith(k, StringComparison.CurrentCultureIgnoreCase)));
        if (hit is not null)
        {
            Navigate(hit); // Im Simple Mode bringt "Zurück" den Benutzer wieder zur Startseite.
            SearchText = string.Empty;
        }
        else
        {
            ShowToast($"Kein Bereich zu „{q}“ gefunden.");
        }
    }

    /// <summary>LOCK (§61): beendet die Security Session und maskiert sensible Daten sofort.</summary>
    [RelayCommand]
    private void Lock()
    {
        AppServices.Auth.EndSession();
        ShowToast("Gesperrt – sensible Bereiche erfordern erneute Bestätigung.");
    }

    public async void ShowToast(string message)
    {
        Toast = message;
        await Task.Delay(4000);
        if (Toast == message) Toast = null;
    }
}
