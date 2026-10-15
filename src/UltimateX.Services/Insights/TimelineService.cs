using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Timeline;
using UltimateX.Services.Health;
using UltimateX.Services.Software;
using UltimateX.Services.SystemTools;

namespace UltimateX.Services.Insights;

/// <summary>Ergebnis der Zeitleiste inkl. Ursachen-Hinweisen.</summary>
public sealed record TimelineResult(int Days, IReadOnlyList<TimelineEvent> Events, IReadOnlyList<CauseHint> Hints, IReadOnlyList<string> Notes);

/// <summary>
/// „Zeitleiste – Was ist passiert?“ (v1.3): sammelt Ereignisse der letzten 7/30/90 Tage aus den Windows-Ereignisprotokollen,
/// der Uninstall-Registry (InstallDate) und den eigenen Snapshot-/Sicherungsdateien. Nur lesend, ohne Adminrechte, ohne Upload.
/// Bluescreens kommen aus der vorhandenen Absturzanalyse (<see cref="CrashAnalysisService"/>).
/// </summary>
public sealed class TimelineService(CrashAnalysisService crashes, ProgramService programs)
{
    private const int MaxPerSource = 2000;

    public TimelineResult Load(int days, CancellationToken ct = default)
    {
        if (days is < 1 or > 366) throw new ArgumentOutOfRangeException(nameof(days));
        var window = TimeSpan.FromDays(days);
        var since = DateTime.Now - window;
        var events = new List<TimelineEvent>();
        var notes = new List<string>();

        // --- Windows-Updates: WindowsUpdateClient 19 (installiert) / 20 (fehlgeschlagen) ---
        foreach (var e in EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-WindowsUpdateClient", [19, 20], window), MaxPerSource, false, ct))
        {
            // 19: 0 = Titel · 20: 0 = Fehlercode, 1 = Titel
            var titleIndex = e.Id == 19 ? 0 : 1;
            var title = e.Props.Count > titleIndex && !string.IsNullOrWhiteSpace(e.Props[titleIndex]) ? e.Props[titleIndex].Trim() : "Windows-Update";
            events.Add(new TimelineEvent(e.Time, e.Id == 19 ? TimelineKind.WindowsUpdate : TimelineKind.WindowsUpdateFailed, title,
                e.Id == 19 ? "Installation erfolgreich." : "Installation fehlgeschlagen" + (e.Props.Count > 0 ? $" (Fehlercode {Hex(e.Props[0])})." : "."),
                $"WindowsUpdateClient {e.Id}"));
        }
        ct.ThrowIfCancellationRequested();

        // --- Programme: MsiInstaller 11707 (installiert) / 11724 (entfernt) ---
        foreach (var e in EventLogReaderHelper.Read("Application", EventLogReaderHelper.Query("MsiInstaller", [11707, 11724], window), MaxPerSource, false, ct))
        {
            var name = e.Props.Count > 0 ? TimelineRules.MsiProductName(e.Props[0]) : "";
            if (string.IsNullOrWhiteSpace(name)) name = "Windows Installer-Paket";
            events.Add(new TimelineEvent(e.Time, e.Id == 11707 ? TimelineKind.ProgramInstall : TimelineKind.ProgramUninstall, name,
                e.Id == 11707 ? "Installation abgeschlossen (Windows Installer)." : "Deinstallation abgeschlossen (Windows Installer).", $"MsiInstaller {e.Id}"));
        }
        // --- Programme: Uninstall-Registry InstallDate (nur Datum; auch Nicht-MSI-Installer) ---
        try
        {
            foreach (var p in programs.GetPrograms())
            {
                if (p.InstallDate is not { } d || d < since.Date || d > DateTime.Now) continue;
                events.Add(new TimelineEvent(d.Date.AddHours(12), TimelineKind.ProgramInstall, p.Name,
                    $"Installationsdatum laut Programmliste{(p.Version is { Length: > 0 } v ? $" · Version {v}" : "")}{(p.Publisher is { Length: > 0 } pub ? $" · {pub}" : "")}. Uhrzeit unbekannt.",
                    "Uninstall-Registry", DateOnly: true));
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            notes.Add("Programmliste nicht vollständig lesbar.");
            AppLog.Warn("Timeline", "Programmliste nicht lesbar", ex.Message);
        }
        ct.ThrowIfCancellationRequested();

        // --- Treiber: UserPnp 20001/20003 (Installation/Aktualisierung) ---
        foreach (var e in EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-UserPnp", [20001, 20003], window), MaxPerSource, true, ct))
        {
            var detail = FirstLine(e.Message) ?? string.Join(" · ", e.Props.Where(p => !string.IsNullOrWhiteSpace(p)).Take(4));
            events.Add(new TimelineEvent(e.Time, TimelineKind.DriverInstall, DriverTitle(e), detail, $"UserPnp {e.Id}"));
        }
        // --- Treiber: Kernel-PnP 400 (Gerät konfiguriert) im Protokoll „Kernel-PnP/Configuration“, je Gerät und Tag einmal ---
        var seenDevice = new HashSet<(string, DateTime)>();
        foreach (var e in EventLogReaderHelper.Read("Microsoft-Windows-Kernel-PnP/Configuration",
                     EventLogReaderHelper.Query("Microsoft-Windows-Kernel-PnP", [400], window), MaxPerSource, false, ct))
        {
            var device = e.Props.Count > 0 ? e.Props[0] : "";
            if (string.IsNullOrWhiteSpace(device) || !seenDevice.Add((device, e.Time.Date))) continue;
            // Eigenschaften: 0 Geräte-ID, 1 Treibername (INF), 2 Klassen-GUID, 3 Treiberdatum, 4 Treiberversion, 5 Anbieter … 10 DeviceUpdated
            var inf = e.Props.Count > 1 ? e.Props[1] : "";
            if (string.IsNullOrWhiteSpace(inf)) continue; // Gerät ohne Treiberinstallation
            // 10 = DeviceUpdated: nur echte Treiberänderungen, nicht jede Neukonfiguration beim Start.
            if (e.Props.Count > 10 && !e.Props[10].Equals("true", StringComparison.OrdinalIgnoreCase)) continue;
            var version = e.Props.Count > 4 ? e.Props[4] : "";
            var provider = e.Props.Count > 5 ? e.Props[5] : "";
            events.Add(new TimelineEvent(e.Time, TimelineKind.DriverInstall, $"{inf}{(version.Length > 0 ? " " + version : "")}",
                $"Gerät konfiguriert: {device}{(provider.Length > 0 ? " · Anbieter " + provider : "")}", "Kernel-PnP 400"));
        }
        ct.ThrowIfCancellationRequested();

        // --- Bluescreens (vorhandene Absturzanalyse) ---
        try
        {
            var report = crashes.Analyze(days);
            foreach (var b in report.BlueScreens)
                events.Add(new TimelineEvent(b.Time, TimelineKind.BlueScreen, $"Bluescreen 0x{b.Code:X8} {b.Info.Name}".Trim(),
                    $"{b.Info.Meaning} {(b.DumpFile is null ? "" : "Abbild: " + b.DumpFile)}".Trim(), b.Source));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            notes.Add("Absturzanalyse nicht verfügbar.");
            AppLog.Warn("Timeline", "Absturzanalyse fehlgeschlagen", ex.Message);
        }
        // --- Unerwartete Neustarts ohne Bluescreen: Kernel-Power 41 mit BugcheckCode 0 ---
        foreach (var e in EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-Kernel-Power", [41], window), MaxPerSource, false, ct))
        {
            var code = e.Props.Count > 0 && uint.TryParse(e.Props[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            if (code != 0) continue; // mit BugCheck → bereits als Bluescreen erfasst
            events.Add(new TimelineEvent(e.Time, TimelineKind.UnexpectedRestart, "Unerwarteter Neustart",
                "Windows wurde nicht sauber heruntergefahren (Stromausfall, Reset-Taste, Einfrieren oder Abschalten per Netzschalter).", "Kernel-Power 41"));
        }
        ct.ThrowIfCancellationRequested();

        // --- Programmabstürze: Application Error 1000 ---
        foreach (var e in EventLogReaderHelper.Read("Application", EventLogReaderHelper.Query("Application Error", [1000], window), MaxPerSource, false, ct))
        {
            // Eigenschaften: 0 App, 1 Version, 2 Zeitstempel, 3 Modul, 4 Modulversion, 5 Zeitstempel, 6 Ausnahmecode
            var app = e.Props.Count > 0 && e.Props[0].Length > 0 ? e.Props[0] : "Programm";
            var detail = e.Props.Count >= 7 ? $"Modul {e.Props[3]} {e.Props[4]} · Ausnahme 0x{e.Props[6].Replace("0x", "", StringComparison.OrdinalIgnoreCase)}" : "Absturz protokolliert.";
            events.Add(new TimelineEvent(e.Time, TimelineKind.AppCrash, app + (e.Props.Count > 1 && e.Props[1].Length > 0 ? " " + e.Props[1] : ""), detail, "Application Error 1000"));
        }

        // --- Start/Herunterfahren: Kernel-General 12/13 ---
        foreach (var e in EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-Kernel-General", [12, 13], window), MaxPerSource, false, ct))
            events.Add(new TimelineEvent(e.Time, e.Id == 12 ? TimelineKind.Startup : TimelineKind.Shutdown,
                e.Id == 12 ? "Windows gestartet" : "Windows heruntergefahren",
                e.Id == 12 ? "Kaltstart (Starts aus dem Schnellstart/Ruhezustand erscheinen hier nicht)." : "Sauberes Herunterfahren.", $"Kernel-General {e.Id}"));
        ct.ThrowIfCancellationRequested();

        // --- ULTIMATE X: Snapshots und Sicherungen (nur Dateien, Inhalt wird nicht gelesen) ---
        try
        {
            foreach (var s in SnapshotService.List().Where(s => s.Created >= since))
                events.Add(new TimelineEvent(s.Created, TimelineKind.Snapshot, string.IsNullOrWhiteSpace(s.Label) ? "System-Snapshot" : s.Label,
                    Path.GetFileName(s.Path), "ULTIMATE X"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Timeline", "Snapshots nicht lesbar", ex.Message);
        }
        events.AddRange(ListBackups(since));

        var cleaned = TimelineRules.DeduplicateInstalls(events.Where(e => e.Time >= since && e.Time != DateTime.MinValue));
        var hints = TimelineRules.Correlate(cleaned);
        AppLog.Info("Timeline", $"Zeitleiste {days} Tage", $"{cleaned.Count} Ereignisse, {hints.Count} Hinweise");
        return new TimelineResult(days, cleaned.OrderByDescending(e => e.Time).ToList(), hints, notes);
    }

    /// <summary>Sicherungsdateien von ULTIMATE X (z. B. Autostart-/Desktop-Sicherungen) im Ordner backups.</summary>
    private static IEnumerable<TimelineEvent> ListBackups(DateTime since)
    {
        var dir = Path.Combine(AppPaths.UserDataRoot, "backups");
        if (!Directory.Exists(dir)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = true }).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { yield break; }
        foreach (var f in files)
        {
            DateTime t;
            try { t = File.GetLastWriteTime(f); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
            if (t < since) continue;
            var name = Path.GetFileName(f);
            var what = name.StartsWith("startup-", StringComparison.OrdinalIgnoreCase) ? "Autostart-Sicherung"
                : name.StartsWith("desktop-", StringComparison.OrdinalIgnoreCase) ? "Desktop-Sicherung"
                : "Sicherung";
            yield return new TimelineEvent(t, TimelineKind.Backup, what, name, "ULTIMATE X");
        }
    }

    private static string DriverTitle(LogEvent e)
    {
        // UserPnp 20001/20003: Eigenschaften enthalten Geräte-ID und Treibername; die Reihenfolge variiert je Windows-Version.
        var inf = e.Props.FirstOrDefault(p => p.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(inf)) return Path.GetFileName(inf);
        var dev = e.Props.FirstOrDefault(p => p.Contains('\\', StringComparison.Ordinal));
        return string.IsNullOrWhiteSpace(dev) ? "Treiberinstallation" : dev;
    }

    private static string? FirstLine(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var line = s.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        return line is { Length: > 300 } ? line[..300] + " …" : line;
    }

    private static string Hex(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return "0x" + unchecked((uint)i).ToString("X8", CultureInfo.InvariantCulture);
        if (uint.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u)) return "0x" + u.ToString("X8", CultureInfo.InvariantCulture);
        return s;
    }
}
