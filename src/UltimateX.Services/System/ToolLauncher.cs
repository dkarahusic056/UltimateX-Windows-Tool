using System.ComponentModel;
using System.Diagnostics;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Services.SystemTools;

public enum SystemTool
{
    Cmd,
    CmdAdmin,
    PowerShell,
    PowerShellAdmin,
    WindowsTerminal,
    TaskManager,
    DeviceManager,
    DiskManagement,
    SystemInformation,
    WindowsUpdate,
    NetworkAdapters,
    FirewallSettings,
    WindowsSecurity,
    AppsAndFeatures,
    StorageSettings,
    SystemRestore,
    LocationPrivacy,
    ReliabilityMonitor,
}

public sealed record ToolDefinition(SystemTool Id, string Title, string Description, bool RequiresAdmin);

/// <summary>
/// Startet Windows-eigene Werkzeuge (§38). Jeder Eintrag ist fest verdrahtet (absoluter Pfad bzw.
/// ms-settings-URI). Es werden keine Argumente aus Benutzereingaben übergeben.
/// Admin-Varianten laufen ausschließlich über UAC (Verb "runas").
/// </summary>
public sealed class ToolLauncher
{
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        new(SystemTool.Cmd, "Eingabeaufforderung", "CMD als Standardbenutzer", false),
        new(SystemTool.CmdAdmin, "CMD (Administrator)", "Mit UAC-Freigabe", true),
        new(SystemTool.PowerShell, "PowerShell", "Als Standardbenutzer", false),
        new(SystemTool.PowerShellAdmin, "PowerShell (Administrator)", "Mit UAC-Freigabe", true),
        new(SystemTool.WindowsTerminal, "Windows Terminal", "Falls installiert", false),
        new(SystemTool.TaskManager, "Task-Manager", "Prozesse und Leistung", false),
        new(SystemTool.DeviceManager, "Geräte-Manager", "Hardware und Treiber", false),
        new(SystemTool.DiskManagement, "Datenträgerverwaltung", "Partitionen und Volumes", true),
        new(SystemTool.SystemInformation, "Systeminformationen", "msinfo32", false),
        new(SystemTool.WindowsUpdate, "Windows Update", "Einstellungen öffnen", false),
        new(SystemTool.NetworkAdapters, "Netzwerkadapter", "Netzwerkverbindungen", false),
        new(SystemTool.FirewallSettings, "Firewall-Einstellungen", "Windows Defender Firewall", false),
        new(SystemTool.WindowsSecurity, "Windows-Sicherheit", "Viren- und Bedrohungsschutz", false),
        new(SystemTool.AppsAndFeatures, "Apps & Features", "Programme deinstallieren", false),
        new(SystemTool.StorageSettings, "Speichereinstellungen", "Speicheroptimierung", false),
        new(SystemTool.SystemRestore, "Systemwiederherstellung", "Wiederherstellungspunkte", true),
        new(SystemTool.LocationPrivacy, "Standort-Datenschutz", "Für WLAN-Details unter Windows 11", false),
        new(SystemTool.ReliabilityMonitor, "Zuverlässigkeitsverlauf", "Abstürze und Fehler im Zeitverlauf", false),
    ];

    public OperationResult Launch(SystemTool tool)
    {
        var sys = AppPaths.System32;
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
        var ps = Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");

        ProcessStartInfo psi = tool switch
        {
            SystemTool.Cmd => Exe(Path.Combine(sys, "cmd.exe")),
            SystemTool.CmdAdmin => Admin(Path.Combine(sys, "cmd.exe")),
            SystemTool.PowerShell => Exe(ps),
            SystemTool.PowerShellAdmin => Admin(ps),
            SystemTool.WindowsTerminal => Exe(wt),
            SystemTool.TaskManager => Exe(Path.Combine(sys, "Taskmgr.exe")),
            SystemTool.DeviceManager => Exe(Path.Combine(sys, "mmc.exe"), Path.Combine(sys, "devmgmt.msc")),
            SystemTool.DiskManagement => Admin(Path.Combine(sys, "mmc.exe"), Path.Combine(sys, "diskmgmt.msc")),
            SystemTool.SystemInformation => Exe(Path.Combine(sys, "msinfo32.exe")),
            SystemTool.WindowsUpdate => Uri("ms-settings:windowsupdate"),
            SystemTool.NetworkAdapters => Exe(Path.Combine(sys, "control.exe"), "ncpa.cpl"),
            SystemTool.FirewallSettings => Exe(Path.Combine(sys, "control.exe"), "firewall.cpl"),
            SystemTool.WindowsSecurity => Uri("windowsdefender:"),
            SystemTool.AppsAndFeatures => Uri("ms-settings:appsfeatures"),
            SystemTool.StorageSettings => Uri("ms-settings:storagesense"),
            SystemTool.SystemRestore => Exe(Path.Combine(sys, "rstrui.exe")),
            SystemTool.LocationPrivacy => Uri("ms-settings:privacy-location"),
            SystemTool.ReliabilityMonitor => new ProcessStartInfo(Path.Combine(sys, "perfmon.exe"), "/rel") { UseShellExecute = true, WorkingDirectory = sys },
            _ => throw new ArgumentOutOfRangeException(nameof(tool)),
        };
        if (tool == SystemTool.WindowsTerminal && !File.Exists(wt))
            return OperationResult.Fail("Windows Terminal ist nicht installiert.");

        try
        {
            using var p = Process.Start(psi);
            var def = All.First(t => t.Id == tool);
            if (def.RequiresAdmin) AppLog.Security("AdminOperation", $"Werkzeug mit Adminrechten gestartet: {def.Title}");
            else AppLog.Info("Tools", $"Werkzeug gestartet: {def.Title}");
            return OperationResult.Ok("Gestartet.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
        }
        catch (Win32Exception ex)
        {
            return OperationResult.Fail(tool == SystemTool.WindowsTerminal
                ? "Windows Terminal ist nicht installiert."
                : "Werkzeug konnte nicht gestartet werden: " + ex.Message);
        }
    }

    private static ProcessStartInfo Exe(string file, string? arg = null)
    {
        var psi = new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = AppPaths.System32 };
        if (arg is not null) psi.Arguments = "\"" + arg + "\""; // feste Konstante, keine Benutzereingabe
        return psi;
    }

    private static ProcessStartInfo Admin(string file, string? arg = null)
    {
        var psi = Exe(file, arg);
        psi.Verb = "runas";
        return psi;
    }

    private static ProcessStartInfo Uri(string uri) => new(uri) { UseShellExecute = true };
}
