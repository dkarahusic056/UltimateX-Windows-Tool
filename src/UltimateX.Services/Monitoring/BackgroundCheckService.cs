using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;

namespace UltimateX.Services.Monitoring;

/// <summary>
/// Langsame Prüfungen der Hintergrundüberwachung (v1.3, Infobereich). Ausschließlich LESEND und ohne Adminrechte:
/// Laufwerksfüllstand (DriveInfo), SMART-Bewertung (zwischengespeicherte Daten + MSFT_PhysicalDisk),
/// neue Bluescreens (Ereignisprotokoll System) und Defender-Echtzeitschutz (WMI). Kein Netzwerkzugriff.
/// Jede Prüfung liefert null, wenn keine Quelle verfügbar ist – dann wird die zugehörige Regel übersprungen.
/// </summary>
public sealed class BackgroundCheckService
{
    private readonly DriveHealthService _driveHealth;

    public BackgroundCheckService(DriveHealthService driveHealth) => _driveHealth = driveHealth;

    /// <summary>Belegung aller bereiten lokalen Festplatten-Laufwerke.</summary>
    public IReadOnlyList<DriveFillInfo>? GetDriveFill()
    {
        try
        {
            var list = new List<DriveFillInfo>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                    var used = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize;
                    list.Add(new DriveFillInfo(d.Name.TrimEnd('\\'), Math.Round(used, 1), (ulong)Math.Max(0, d.TotalFreeSpace)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Laufwerk gerade nicht lesbar (z. B. BitLocker gesperrt) → überspringen
                }
            }
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Hintergrund", "Laufwerke nicht lesbar", ex.Message);
            return null;
        }
    }

    /// <summary>Laufwerke mit Warnung/kritischem Zustand laut Windows bzw. SMART-Bewertung (Texte für die Meldung).</summary>
    public IReadOnlyList<string>? GetSmartWarnings()
    {
        try
        {
            var drives = _driveHealth.GetDrives(SmartService.LoadCached());
            if (drives.Count == 0) return null;
            var warnings = new List<string>();
            foreach (var d in drives)
            {
                var bad = d.Verdict.Verdict is DriveVerdict.Warning or DriveVerdict.Critical
                          || d.WindowsHealthState is DataState.Warning or DataState.Problem;
                if (!bad) continue;
                var reason = d.Verdict.Reasons.FirstOrDefault() ?? d.WindowsHealth;
                warnings.Add($"{d.Name}: {d.Verdict.Label} – {reason}");
            }
            return warnings;
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Hintergrund", "SMART-Prüfung fehlgeschlagen", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Bluescreens seit <paramref name="sinceUtc"/> (WER-SystemErrorReporting 1001 mit BugCheck, Kernel-Power 41 mit BugcheckCode ≠ 0).
    /// Null = Protokoll nicht lesbar.
    /// </summary>
    public IReadOnlyList<DateTime>? GetBlueScreensSince(DateTime sinceUtc)
    {
        var since = sinceUtc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        var xpath = "*[System[((Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) or " +
                    "(Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41)) and TimeCreated[@SystemTime>'" + since + "']]]";
        var result = new List<DateTime>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var i = 0; i < 50; i++)
            {
                using var rec = reader.ReadEvent();
                if (rec is null) break;
                var time = rec.TimeCreated ?? DateTime.Now;
                if (rec.Id == 41)
                {
                    // Kernel-Power 41: Eigenschaft 0 = BugcheckCode; 0 = Stromausfall/Reset ohne Bluescreen → kein Bluescreen
                    var code = rec.Properties.Count > 0 ? Convert.ToString(rec.Properties[0].Value, CultureInfo.InvariantCulture) : null;
                    if (code is null || code == "0") continue;
                    if (result.Any(t => Math.Abs((t - time).TotalMinutes) < 10)) continue; // derselbe Absturz
                }
                else if (result.Any(t => Math.Abs((t - time).TotalMinutes) < 10))
                {
                    continue;
                }
                result.Add(time);
            }
            return result;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Hintergrund", "Systemprotokoll nicht lesbar", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// true = Defender-Echtzeitschutz aus UND kein anderer aktiver Virenschutz; false = geschützt; null = nicht feststellbar.
    /// </summary>
    public bool? IsAntivirusOff()
    {
        var defender = Wmi.Query("SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus", Wmi.Defender, 15);
        var d = defender.Rows.FirstOrDefault();
        if (d is not null && d.Bool("AntivirusEnabled") == true && d.Bool("RealTimeProtectionEnabled") == true) return false;

        // Defender passiv/aus → aktiven Drittanbieter-Virenschutz über das Sicherheitscenter prüfen.
        var av = Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", Wmi.SecurityCenter2, 15);
        if (!av.Ok && d is null) return null;
        var anyActive = av.Rows.Any(r => r.Str("displayName") is not null && (((r.U64("productState") ?? 0) >> 8) & 0xF0) == 0x10);
        if (anyActive) return false;
        return d is not null || av.Ok ? true : null;
    }
}
