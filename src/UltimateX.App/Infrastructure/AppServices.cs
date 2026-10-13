using UltimateX.Core.Settings;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Cleanup;
using UltimateX.Services.Data;
using UltimateX.Services.Desktop;
using UltimateX.Services.Diagnostics;
using UltimateX.Services.Updates;
using UltimateX.Services.Drivers;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Monitoring;
using UltimateX.Services.Network;
using UltimateX.Services.Security;
using UltimateX.Services.Software;
using UltimateX.Services.Startup;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Einfache Service-Registry (bewusst ohne DI-Framework → keine zusätzliche Abhängigkeit, §92).
/// ViewModels erhalten Services ausschließlich hierüber; die UI greift nie direkt auf Systemfunktionen zu (§98).
/// </summary>
public static class AppServices
{
    public static AppSettings Settings { get; set; } = new();

    public static HardwareService Hardware { get; } = new();
    public static LiveMonitorService Monitor { get; } = new();
    public static NetworkService Network { get; } = new();
    public static SpeedtestService Speedtest { get; } = new();
    public static DriverService Drivers { get; } = new();
    public static ProgramService Programs { get; } = new();
    public static StartupService Startup { get; } = new();
    public static WindowsHealthService Health { get; } = new();
    public static SecurityService Security { get; } = new();
    public static AuthenticationService Auth { get; } = new();
    public static RecycleBinService RecycleBin { get; } = new();
    public static PowerService Power { get; } = new();
    public static ToolLauncher Tools { get; } = new();
    public static StorageCleanerService Cleaner { get; } = new();
    public static DesktopOrganizerService Organizer { get; } = new();
    public static WlanService Wlan { get; } = new();
    public static DisplayService Displays { get; } = new();
    public static SmartService Smart { get; } = new();
    public static RestorePointService RestorePoints { get; } = new();
    public static WindowsUpdateService WindowsUpdate { get; } = new();
    public static WingetService Winget { get; } = new();
    public static AppUpdateService AppUpdate { get; } = new();
    public static ProcessService Processes { get; } = new();
    public static InventoryService Inventory { get; } = new();
    public static NotificationService Notifications { get; } = new();
    public static HistoryDatabase History { get; } = new();
    public static BenchmarkService Benchmarks { get; } = new(History);
    public static NetworkScannerService Scanner { get; } = new(History);
    public static CrashAnalysisService Crashes { get; } = new();

    public static ComponentTestService ComponentTests { get; } = new(Hardware, Security, Health, Network, Wlan, Displays, Inventory, Monitor.SensorManager);
    public static ComponentTestHistory TestHistory { get; } = new();

    /// <summary>Produktversion (aus der Assembly, z. B. „1.1.0“).</summary>
    public static string AppVersion { get; } = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";

    public static SensitiveActionGuard Guard { get; } = new();

    /// <summary>Zwischenspeicher der letzten Hardware-Erkennung (Dashboard, Hardware, Bericht teilen sie sich).</summary>
    public static HardwareSnapshot? LastHardware { get; set; }

    public static void SaveSettings() => SettingsStore.Save(Settings);
}
