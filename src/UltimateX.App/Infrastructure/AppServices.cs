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
using UltimateX.Services.Setup;
using UltimateX.Services.Backup;
using UltimateX.Services.Gaming;
using UltimateX.Services.Insights;
using UltimateX.Services.NetSec;

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

    // ---------------- v1.2 ----------------
    public static SystemInfoService SystemInfo { get; } = new();
    public static DriveHealthService DriveHealth { get; } = new();
    public static MemoryTestService MemoryTest { get; } = new();
    public static NetworkBenchmarkService NetworkBenchmark { get; } = new(Network, Speedtest, Wlan, Benchmarks);
    public static CompleteBenchmarkService CompleteBenchmark { get; } = new(Benchmarks, NetworkBenchmark, MemoryTest);
    public static SoftwareSetupService SoftwareSetup { get; } = new(Programs, Hardware, Health, Security);
    public static OfficeDeploymentService Office { get; } = new();
    public static NvidiaDriverService NvidiaDrivers { get; } = new();
    public static ClamAvService ClamAv { get; } = new();
    public static SnapshotService Snapshots { get; } = new(Hardware, Drivers, Programs, Inventory, Startup, Network, Health);
    public static ComponentTestHistory TestHistory { get; } = new();

    // ---------------- v1.3 ----------------
    public static GameRequirementsOnlineService OnlineGames { get; } = new();
    public static BackupService Backup { get; } = new();
    public static SystemImageService SystemImage { get; } = new(Backup);
    public static DriverBackupService DriverBackup { get; } = new(Backup);
    public static NetworkToolsService NetTools { get; } = new();
    public static SecurityAuditService SecurityAudit { get; } = new(Security);
    public static ScheduledTaskService ScheduledTasks { get; } = new();
    public static PrivacyService Privacy { get; } = new();
    public static ScheduledPowerService ScheduledPower { get; } = new(Power);
    public static UptimeRecorder Uptime { get; } = new();
    public static LifetimeService Lifetime { get; } = new();
    public static TimelineService Timeline { get; } = new(Crashes, Programs);
    public static FpsMonitorService Fps { get; } = new();
    public static BackgroundCheckService BackgroundChecks { get; } = new(DriveHealth);

    /// <summary>Produktversion (aus der Assembly, z. B. „1.3.0“).</summary>
    public static string AppVersion { get; } = typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "1.3.1";

    public static SensitiveActionGuard Guard { get; } = new();

    /// <summary>Zwischenspeicher der letzten Hardware-Erkennung (Dashboard, Hardware, Bericht teilen sie sich).</summary>
    public static HardwareSnapshot? LastHardware { get; set; }

    public static void SaveSettings() => SettingsStore.Save(Settings);
}
