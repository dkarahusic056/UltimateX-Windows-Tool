using System.Diagnostics.Eventing.Reader;
using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Common;

namespace UltimateX.Services.Health;

public enum RepairOutcome
{
    NoProblems,
    Repaired,
    NotRepaired,
    RepairableCorruption,
    CouldNotRun,
    Cancelled,
    Unknown,
}

public sealed record RepairResult(AllowedCommand Command, RepairOutcome Outcome, string Summary, int ExitCode, string RawOutput)
{
    public DataState State => Outcome switch
    {
        RepairOutcome.NoProblems or RepairOutcome.Repaired => DataState.Ok,
        RepairOutcome.RepairableCorruption or RepairOutcome.Unknown or RepairOutcome.Cancelled => DataState.Warning,
        _ => DataState.Problem,
    };
}

public sealed record EventProblem(string Log, string Provider, long EventId, string Level, int Count, DateTime? LastSeen, string? Message);

public sealed record EventEntry(string Log, DateTime? Time, string Level, string Provider, long EventId, string? Message);

public sealed record CrashSummary(int UnexpectedShutdowns, int BugChecks, int AppCrashes, DateTime? LastBugCheck, string? DumpInfo);

public sealed record ServiceStatus(string Name, string DisplayName, string State, string StartMode, DataState Health);

public sealed record RebootStatus(bool RebootRequired, IReadOnlyList<string> Reasons);

/// <summary>
/// Windows Health &amp; Repair Center (§39–§48).
/// SFC/DISM/CHKDSK laufen ausschließlich über die Allowlist + Admin-Helper (UAC).
/// Die Auswertung ist bewusst konservativ: Was nicht eindeutig erkannt wird, heißt "Ergebnis unklar"
/// und verweist auf die Originalausgabe – es wird keine Diagnose erfunden (§47).
/// </summary>
public sealed class WindowsHealthService
{
    // ---------- SFC / DISM / CHKDSK ----------

    public async Task<RepairResult> RunAsync(AllowedCommand command, string? parameter, IProgress<string>? progress, CancellationToken ct)
    {
        var r = await ElevatedCommandRunner.RunAsync(command, parameter, progress, ct).ConfigureAwait(false);
        if (r.Cancelled) return new RepairResult(command, RepairOutcome.Cancelled, r.Output, r.ExitCode, r.Output);
        if (!r.Started) return new RepairResult(command, RepairOutcome.CouldNotRun, r.Output, r.ExitCode, r.Output);

        var (outcome, summary) = command switch
        {
            AllowedCommand.SfcScan => InterpretSfc(r.Output),
            AllowedCommand.DismCheckHealth or AllowedCommand.DismScanHealth or AllowedCommand.DismRestoreHealth => InterpretDism(command, r.ExitCode, r.Output),
            AllowedCommand.ChkdskScan or AllowedCommand.ChkdskSpotFix => InterpretChkdsk(r.ExitCode),
            AllowedCommand.ScheduleBootDiskCheck => r.ExitCode == 0
                ? (RepairOutcome.NoProblems, "Die Datenträgerprüfung wird beim nächsten Neustart ausgeführt.")
                : (RepairOutcome.CouldNotRun, "Die Prüfung konnte nicht geplant werden."),
            AllowedCommand.MemoryDiagnostic => (RepairOutcome.Unknown, "Windows-Speicherdiagnose wurde gestartet. Bitte im Dialog den Zeitpunkt des Neustarts wählen."),
            _ => (RepairOutcome.Unknown, "Ergebnis unklar – bitte technische Details prüfen."),
        };

        AppLog.Info("WindowsRepair", $"{command}: {outcome}", $"ExitCode={r.ExitCode}");
        return new RepairResult(command, outcome, summary, r.ExitCode, r.Output);
    }

    /// <summary>Erkennt die Standardmeldungen von SFC in Deutsch und Englisch.</summary>
    internal static (RepairOutcome, string) InterpretSfc(string output)
    {
        var o = output.ToLowerInvariant();
        if (o.Contains("keine integritätsverletzungen") || o.Contains("did not find any integrity violations"))
            return (RepairOutcome.NoProblems, "Keine Fehler gefunden – die Systemdateien sind intakt.");
        if (o.Contains("erfolgreich repariert") || o.Contains("successfully repaired"))
            return (RepairOutcome.Repaired, "Beschädigte Systemdateien wurden gefunden und repariert. Ein Neustart wird empfohlen.");
        if (o.Contains("konnte jedoch einige") || o.Contains("unable to fix some"))
            return (RepairOutcome.NotRepaired, "Beschädigte Dateien gefunden, aber nicht alle repariert. Empfehlung: 'Windows Repair' (DISM) ausführen und SFC danach erneut starten.");
        if (o.Contains("konnte den angeforderten vorgang nicht") || o.Contains("could not perform the requested operation"))
            return (RepairOutcome.CouldNotRun, "SFC konnte nicht ausgeführt werden (evtl. ausstehender Neustart). Bitte neu starten und erneut versuchen.");
        return (RepairOutcome.Unknown, "Ergebnis unklar – bitte technische Details prüfen.");
    }

    internal static (RepairOutcome, string) InterpretDism(AllowedCommand cmd, int exitCode, string output)
    {
        var o = output.ToLowerInvariant();
        if (exitCode != 0)
            return (RepairOutcome.CouldNotRun, $"DISM wurde mit Fehlercode {exitCode} (0x{exitCode:X8}) beendet. Für RestoreHealth ist eine Internetverbindung bzw. Windows Update erforderlich.");

        bool repairable = o.Contains("kann repariert werden") || o.Contains("is repairable");
        bool noCorruption = o.Contains("keine komponentenspeicherbeschädigung") || o.Contains("no component store corruption");
        if (cmd == AllowedCommand.DismRestoreHealth)
            return (RepairOutcome.Repaired, "Die Reparatur des Komponentenspeichers wurde erfolgreich abgeschlossen. Empfehlung: SFC jetzt erneut ausführen.");
        if (repairable)
            return (RepairOutcome.RepairableCorruption, "Der Komponentenspeicher ist beschädigt, kann aber repariert werden. Empfehlung: 'Windows Repair' ausführen.");
        if (noCorruption)
            return (RepairOutcome.NoProblems, "Keine Beschädigung des Komponentenspeichers gefunden.");
        return (RepairOutcome.Unknown, "DISM wurde erfolgreich beendet. Details siehe technische Ausgabe.");
    }

    /// <summary>CHKDSK-Exitcodes laut Microsoft-Dokumentation.</summary>
    internal static (RepairOutcome, string) InterpretChkdsk(int exitCode) => exitCode switch
    {
        0 => (RepairOutcome.NoProblems, "Keine Fehler gefunden."),
        1 => (RepairOutcome.Repaired, "Fehler wurden gefunden und behoben."),
        2 => (RepairOutcome.NoProblems, "Bereinigung durchgeführt (z. B. Freigabe ungenutzter Indizes), keine Fehler."),
        3 => (RepairOutcome.RepairableCorruption, "Fehler gefunden, die nicht online behoben werden konnten. Empfehlung: Prüfung beim Neustart planen."),
        _ => (RepairOutcome.Unknown, $"Unbekannter Exitcode {exitCode}."),
    };

    // ---------- Neustartstatus (§37) ----------

    public RebootStatus GetRebootStatus()
    {
        var reasons = new List<string>();
        using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using (var k = lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"))
            if (k is not null) reasons.Add("Windows-Komponenten-Update wartet auf Neustart");
        using (var k = lm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"))
            if (k is not null) reasons.Add("Windows Update erfordert einen Neustart");
        using (var k = lm.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            if (k?.GetValue("PendingFileRenameOperations") is string[] { Length: > 0 }) reasons.Add("Dateien werden beim Neustart ersetzt");
        return new RebootStatus(reasons.Count > 0, reasons);
    }

    public (int? Start, int? End) GetActiveHours()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
        return (k?.GetValue("ActiveHoursStart") as int?, k?.GetValue("ActiveHoursEnd") as int?);
    }

    // ---------- Dienste (§45, nur lesend) ----------

    private static readonly (string Name, string Label)[] ImportantServices =
    [
        ("wuauserv", "Windows Update"),
        ("EventLog", "Windows-Ereignisprotokoll"),
        ("WinDefend", "Microsoft Defender Antivirus"),
        ("Dhcp", "DHCP-Client"),
        ("Dnscache", "DNS-Client"),
        ("RpcSs", "Remoteprozeduraufruf (RPC)"),
        ("PlugPlay", "Plug & Play"),
        ("Schedule", "Aufgabenplanung"),
        ("mpssvc", "Windows Defender Firewall"),
        ("BFE", "Basisfiltermodul"),
    ];

    public IReadOnlyList<ServiceStatus> GetServiceStatus()
    {
        var r = Wmi.Query("SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
        var byName = r.Rows.Where(x => x.Str("Name") is not null)
            .GroupBy(x => x.Str("Name")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var list = new List<ServiceStatus>();
        foreach (var (name, label) in ImportantServices)
        {
            if (!byName.TryGetValue(name, out var row))
            {
                // WinDefend fehlt, wenn ein Drittanbieter-Virenschutz aktiv ist – das ist kein Fehler.
                list.Add(new ServiceStatus(name, label, "nicht vorhanden", "–", DataState.NotAvailable));
                continue;
            }
            var state = row.Str("State") ?? "?";
            var mode = row.Str("StartMode") ?? "?";
            // wuauserv/Dhcp etc. dürfen "Manual" sein und gestoppt; nur "Disabled" ist auffällig.
            var health = mode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? DataState.Problem
                : state.Equals("Running", StringComparison.OrdinalIgnoreCase) ? DataState.Ok
                : mode.Equals("Auto", StringComparison.OrdinalIgnoreCase) ? DataState.Warning : DataState.Ok;
            list.Add(new ServiceStatus(name, label, TranslateState(state), TranslateMode(mode), health));
        }
        return list;
    }

    private static string TranslateState(string s) => s switch
    {
        "Running" => "Läuft",
        "Stopped" => "Beendet",
        "Paused" => "Angehalten",
        "Start Pending" => "Wird gestartet",
        "Stop Pending" => "Wird beendet",
        _ => s,
    };

    private static string TranslateMode(string s) => s switch
    {
        "Auto" => "Automatisch",
        "Manual" => "Manuell",
        "Disabled" => "Deaktiviert",
        "Boot" or "System" => "System",
        _ => s,
    };

    // ---------- Event Logs (§46, §47) ----------

    public IReadOnlyList<EventProblem> GetTopProblems(int days = 7, int top = 15)
    {
        var all = new List<EventRecordInfo>();
        foreach (var log in new[] { "System", "Application", "Setup" })
            all.AddRange(ReadEvents(log, $"*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= {(long)TimeSpan.FromDays(days).TotalMilliseconds}]]]", 5000));

        return all
            .GroupBy(e => (e.Log, e.Provider, e.Id))
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Time).First();
                return new EventProblem(g.Key.Log, g.Key.Provider, g.Key.Id, latest.Level, g.Count(), latest.Time, latest.Message);
            })
            .OrderByDescending(p => p.Level == "Kritisch")
            .ThenByDescending(p => p.Count)
            .Take(top)
            .ToList();
    }

    public CrashSummary GetCrashSummary(int days = 30)
    {
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        string Q(string cond) => $"*[System[{cond} and TimeCreated[timediff(@SystemTime) <= {ms}]]]";

        var kernelPower = ReadEvents("System", Q("Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41"), 500, withMessage: false);
        var unexpected = ReadEvents("System", Q("Provider[@Name='EventLog'] and EventID=6008"), 500, withMessage: false);
        var bugchecks = ReadEvents("System", Q("Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001"), 500, withMessage: false);
        var appCrashes = ReadEvents("Application", Q("Provider[@Name='Application Error'] and EventID=1000"), 2000, withMessage: false);

        string? dumpInfo = null;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var memoryDmp = Path.Combine(windows, "MEMORY.DMP");
        var minidump = Path.Combine(windows, "Minidump");
        try
        {
            var parts = new List<string>();
            if (File.Exists(memoryDmp)) parts.Add($"{memoryDmp} ({Format.Bytes(new FileInfo(memoryDmp).Length)})");
            if (Directory.Exists(minidump))
            {
                var count = Directory.GetFiles(minidump, "*.dmp").Length;
                if (count > 0) parts.Add($"{minidump} ({count} Minidump(s))");
            }
            dumpInfo = parts.Count > 0 ? string.Join("; ", parts) : null;
        }
        catch (UnauthorizedAccessException)
        {
            dumpInfo = $"{minidump} (Lesen erfordert Adminrechte)";
        }

        return new CrashSummary(
            Math.Max(kernelPower.Count, unexpected.Count),
            bugchecks.Count,
            appCrashes.Count,
            bugchecks.Count > 0 ? bugchecks.Max(b => b.Time) : null,
            dumpInfo);
    }

    /// <summary>Einzelne Ereignisse für die Ansicht "Windows Logs" (Admin/Service Mode).</summary>
    public IReadOnlyList<EventEntry> GetRecentEvents(string log, bool includeWarnings, int days = 7, int max = 500)
    {
        if (log is not ("System" or "Application" or "Setup")) throw new ArgumentException("Unbekanntes Protokoll.", nameof(log));
        var levels = includeWarnings ? "Level=1 or Level=2 or Level=3" : "Level=1 or Level=2";
        var ms = (long)TimeSpan.FromDays(Math.Clamp(days, 1, 90)).TotalMilliseconds;
        return ReadEvents(log, $"*[System[({levels}) and TimeCreated[timediff(@SystemTime) <= {ms}]]]", Math.Clamp(max, 1, 2000))
            .Select(e => new EventEntry(e.Log, e.Time, e.Level, e.Provider, e.Id, e.Message))
            .ToList();
    }

    private sealed record EventRecordInfo(string Log, string Provider, long Id, string Level, DateTime? Time, string? Message);

    private static List<EventRecordInfo> ReadEvents(string log, string xpath, int max, bool withMessage = true)
    {
        var list = new List<EventRecordInfo>();
        var messageBudget = withMessage ? 300 : 0; // FormatDescription ist teuer → nur für die ersten Treffer
        try
        {
            var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (int i = 0; i < max; i++)
            {
                using var rec = reader.ReadEvent();
                if (rec is null) break;
                string? msg = null;
                if (messageBudget-- > 0)
                {
                    try { msg = rec.FormatDescription(); }
                    catch (EventLogException) { }
                    if (msg is { Length: > 400 }) msg = msg[..400] + " …";
                }
                list.Add(new EventRecordInfo(log, rec.ProviderName ?? "?", rec.Id,
                    rec.Level switch { 1 => "Kritisch", 2 => "Fehler", 3 => "Warnung", _ => "Info" },
                    rec.TimeCreated, msg));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            AppLog.Warn("EventLog", $"Ereignisprotokoll '{log}' nicht lesbar", ex.Message);
        }
        return list;
    }
}
