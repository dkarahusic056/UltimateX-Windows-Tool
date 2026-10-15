using Microsoft.Win32;
using UltimateX.Core.Lifetime;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Insights;

/// <summary>Daten der Seite „PC Lifetime“ (ohne Laufwerke – die kommen aus DriveHealthService/SmartService).</summary>
public sealed record LifetimeSystemInfo(
    DateTime? WindowsInstallDate,
    TimeSpan UptimeSinceBoot,
    BootHistorySummary? BootHistory,
    string BootSource,
    DateTime? SystemLogOldest);

/// <summary>
/// „PC Lifetime“ (v1.3): Windows-Installationsdatum (Registry), Laufzeit seit Start (Environment.TickCount64) und
/// Starts/Gesamtlaufzeit aus dem System-Ereignisprotokoll. Alles lesend, ohne Adminrechte.
/// Das Ereignisprotokoll ist größenbegrenzt – die Werte sind daher Untergrenzen („mindestens …, seit …“).
/// </summary>
public sealed class LifetimeService
{
    private const int MaxEvents = 20_000;

    public LifetimeSystemInfo Load(CancellationToken ct = default)
    {
        var install = ReadInstallDate();
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        BootHistorySummary? history = null;
        var source = "N/A";
        DateTime? oldest = null;
        try
        {
            oldest = EventLogReaderHelper.Oldest("System");
            var (entries, src) = ReadBootEntries(ct);
            source = src;
            history = RuntimeCalculator.SummarizeBootLog(entries, DateTime.UtcNow, oldest?.ToUniversalTime());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            AppLog.Warn("Lifetime", "Ereignisprotokoll nicht auswertbar", ex.Message);
        }
        return new LifetimeSystemInfo(install, uptime, history, source, oldest);
    }

    /// <summary>
    /// HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion: InstallTime (FILETIME, genauer) bzw. InstallDate (Unix-Sekunden).
    /// Achtung: Windows setzt den Wert bei Funktionsupdates (z. B. 23H2 → 24H2) neu – die UI weist darauf hin.
    /// </summary>
    public static DateTime? ReadInstallDate()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key is null) return null;
            if (key.GetValue("InstallTime") is long ft && ft > 0)
            {
                try { return DateTime.FromFileTimeUtc(ft).ToLocalTime(); }
                catch (ArgumentOutOfRangeException) { }
            }
            if (key.GetValue("InstallDate") is int unix && unix != 0)
                return DateTimeOffset.FromUnixTimeSeconds((uint)unix).LocalDateTime;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            AppLog.Warn("Lifetime", "InstallDate nicht lesbar", ex.Message);
        }
        return null;
    }

    private static (List<BootLogEntry> Entries, string Source) ReadBootEntries(CancellationToken ct)
    {
        var entries = new List<BootLogEntry>();
        static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime();

        // Start/Stopp: bevorzugt Kernel-General 12/13, sonst EventLog-Dienst 6005/6006.
        var kg = EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-Kernel-General", [12, 13], null), MaxEvents, false, ct);
        string source;
        if (kg.Any(e => e.Id == 12))
        {
            source = "Kernel-General 12/13";
            entries.AddRange(kg.Select(e => new BootLogEntry(Utc(e.Time), e.Id == 12 ? BootLogKind.Start : BootLogKind.Stop)));
        }
        else
        {
            source = "EventLog 6005/6006";
            entries.AddRange(EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("EventLog", [6005, 6006], null), MaxEvents, false, ct)
                .Select(e => new BootLogEntry(Utc(e.Time), e.Id == 6005 ? BootLogKind.Start : BootLogKind.Stop)));
        }

        // Lebenszeichen (6013, täglich) und unerwartetes Herunterfahren (6008).
        var ev = EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("EventLog", [6008, 6013], null), MaxEvents, false, ct);
        entries.AddRange(ev.Where(e => e.Id == 6013).Select(e => new BootLogEntry(Utc(e.Time), BootLogKind.Alive)));

        // Kernel-Power: 41 = unerwarteter Neustart, 42 = Energiesparen beginnt, 107 = Fortsetzen.
        var kp = EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-Kernel-Power", [41, 42, 107], null), MaxEvents, false, ct);
        var k41 = kp.Where(e => e.Id == 41).Select(e => Utc(e.Time)).ToList();
        entries.AddRange(k41.Select(t => new BootLogEntry(t, BootLogKind.Unexpected)));
        // 6008 nur zählen, wenn nicht bereits ein Kernel-Power-41 zum selben Start gehört.
        entries.AddRange(ev.Where(e => e.Id == 6008).Select(e => Utc(e.Time))
            .Where(t => !k41.Any(k => Math.Abs((k - t).TotalMinutes) < 15))
            .Select(t => new BootLogEntry(t, BootLogKind.Unexpected)));
        entries.AddRange(kp.Where(e => e.Id == 42).Select(e => new BootLogEntry(Utc(e.Time), BootLogKind.SleepEnter)));
        entries.AddRange(kp.Where(e => e.Id == 107).Select(e => new BootLogEntry(Utc(e.Time), BootLogKind.Resume)));

        // Power-Troubleshooter 1: „Das System wurde aus dem Energiesparmodus/Ruhezustand/Schnellstart reaktiviert“.
        entries.AddRange(EventLogReaderHelper.Read("System", EventLogReaderHelper.Query("Microsoft-Windows-Power-Troubleshooter", [1], null), MaxEvents, false, ct)
            .Select(e => new BootLogEntry(Utc(e.Time), BootLogKind.Resume)));

        return (entries, source);
    }
}
