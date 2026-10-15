using System.Globalization;

namespace UltimateX.Core.Analysis;

/// <summary>Regeln der Hintergrundüberwachung (v1.3, Infobereich).</summary>
public enum AlertRuleId
{
    CpuTemperature,
    GpuTemperature,
    DiskFull,
    SmartWarning,
    BlueScreen,
    DefenderOff,
    BackupOverdue,
}

public enum AlertSeverity { Warning, Critical }

/// <summary>Eine ausgelöste Meldung. <see cref="Key"/> identifiziert Regel + Gegenstand (z. B. Laufwerk) für die Wiederholungssperre.</summary>
public sealed record AlertEvent(AlertRuleId Rule, string Key, AlertSeverity Severity, string Title, string Message, DateTime Time);

/// <summary>Füllstand eines Laufwerks (Name z. B. „C:“).</summary>
public sealed record DriveFillInfo(string Name, double UsedPercent, ulong FreeBytes);

/// <summary>
/// Ergebnis der langsamen Prüfungen (Intervall, Standard 5 min). Null = in diesem Durchlauf nicht geprüft
/// bzw. keine Quelle → die Regel wird übersprungen (keine Meldung ohne Messwert).
/// </summary>
public sealed record SystemCheckInput(
    IReadOnlyList<DriveFillInfo>? Drives = null,
    IReadOnlyList<string>? SmartWarnings = null,
    IReadOnlyList<DateTime>? NewBlueScreens = null,
    bool? DefenderOff = null,
    IReadOnlyList<string>? BackupOverdue = null);

/// <summary>Einstellungen der Hintergrundüberwachung. Werte werden über <see cref="Normalize"/> auf sinnvolle Bereiche begrenzt.</summary>
public sealed class AlertSettings
{
    public bool CpuTempEnabled { get; set; } = true;
    public int CpuTempLimitC { get; set; } = 90;
    public bool GpuTempEnabled { get; set; } = true;
    public int GpuTempLimitC { get; set; } = 85;
    /// <summary>So lange muss die Temperatur ununterbrochen über dem Grenzwert liegen.</summary>
    public int TempSustainSeconds { get; set; } = 30;

    public bool DiskEnabled { get; set; } = true;
    public int DiskWarnPercent { get; set; } = 90;
    public int DiskCriticalPercent { get; set; } = 95;

    public bool SmartEnabled { get; set; } = true;
    public bool BlueScreenEnabled { get; set; } = true;
    public bool DefenderEnabled { get; set; } = true;
    public bool BackupEnabled { get; set; } = true;

    /// <summary>Intervall der langsamen Prüfungen (Laufwerke, SMART, Bluescreen, Defender, Backup).</summary>
    public int CheckIntervalMinutes { get; set; } = 5;
    /// <summary>Wiederholungssperre je Regel und Gegenstand.</summary>
    public int RepeatLockMinutes { get; set; } = 30;

    public AlertSettings Normalize()
    {
        CpuTempLimitC = Math.Clamp(CpuTempLimitC, 50, 110);
        GpuTempLimitC = Math.Clamp(GpuTempLimitC, 50, 110);
        TempSustainSeconds = Math.Clamp(TempSustainSeconds, 5, 600);
        DiskWarnPercent = Math.Clamp(DiskWarnPercent, 50, 99);
        DiskCriticalPercent = Math.Clamp(DiskCriticalPercent, DiskWarnPercent, 100);
        CheckIntervalMinutes = Math.Clamp(CheckIntervalMinutes, 1, 240);
        RepeatLockMinutes = Math.Clamp(RepeatLockMinutes, 1, 1440);
        return this;
    }
}

/// <summary>
/// Regelwerk der Hintergrundüberwachung – reine Logik, ohne Windows-Zugriff, testbar.
///
/// Entprellung: Temperaturen lösen erst aus, wenn sie <see cref="AlertSettings.TempSustainSeconds"/> lang ununterbrochen
/// über dem Grenzwert liegen; fehlt ein Messwert (null) oder fällt er auf/unter den Grenzwert, beginnt die Zählung neu.
/// Wiederholungssperre: dieselbe Meldung (gleicher <see cref="AlertEvent.Key"/>) höchstens einmal je
/// <see cref="AlertSettings.RepeatLockMinutes"/>. Nicht thread-sicher – Aufrufe nur von einem Thread (UI).
/// </summary>
public sealed class AlertEngine
{
    private readonly Dictionary<string, DateTime> _lastFired = new(StringComparer.Ordinal);
    private DateTime? _cpuAboveSince, _gpuAboveSince;

    public AlertEngine(AlertSettings settings) => Settings = settings;

    public AlertSettings Settings { get; set; }

    /// <summary>Sekündlicher Aufruf mit den aktuellen Temperaturen (null = keine Sensorquelle).</summary>
    public IReadOnlyList<AlertEvent> ObserveTemperatures(DateTime now, double? cpuTempC, double? gpuTempC)
    {
        var list = new List<AlertEvent>();
        var s = Settings;
        Track(ref _cpuAboveSince, s.CpuTempEnabled, cpuTempC, s.CpuTempLimitC, now, AlertRuleId.CpuTemperature, "cpu-temp", "CPU", list);
        Track(ref _gpuAboveSince, s.GpuTempEnabled, gpuTempC, s.GpuTempLimitC, now, AlertRuleId.GpuTemperature, "gpu-temp", "GPU", list);
        return list;
    }

    private void Track(ref DateTime? since, bool enabled, double? value, int limit, DateTime now, AlertRuleId rule, string key, string part, List<AlertEvent> list)
    {
        if (!enabled || value is not { } v || v <= limit)
        {
            since = null;
            return;
        }
        since ??= now;
        if ((now - since.Value).TotalSeconds < Settings.TempSustainSeconds) return;
        if (!TryFire(key, now)) return;
        var secs = Settings.TempSustainSeconds.ToString(CultureInfo.InvariantCulture);
        list.Add(new AlertEvent(rule, key, v >= limit + 10 ? AlertSeverity.Critical : AlertSeverity.Warning,
            $"{part}-Temperatur hoch",
            $"{part}-Temperatur {v.ToString("0", CultureInfo.InvariantCulture)} °C liegt seit über {secs} s über dem Grenzwert von {limit.ToString(CultureInfo.InvariantCulture)} °C. Kühlung und Lüfter prüfen.",
            now));
    }

    /// <summary>Auswertung der langsamen Prüfungen.</summary>
    public IReadOnlyList<AlertEvent> EvaluateChecks(DateTime now, SystemCheckInput input)
    {
        var list = new List<AlertEvent>();
        var s = Settings;

        if (s.DiskEnabled && input.Drives is { } drives)
        {
            foreach (var d in drives)
            {
                var pct = d.UsedPercent.ToString("0", CultureInfo.InvariantCulture);
                if (d.UsedPercent >= s.DiskCriticalPercent)
                {
                    if (TryFire("disk-crit:" + d.Name, now))
                        list.Add(new AlertEvent(AlertRuleId.DiskFull, "disk-crit:" + d.Name, AlertSeverity.Critical, "Laufwerk fast voll",
                            $"Laufwerk {d.Name} ist zu {pct} % belegt. Bitte Speicherplatz freigeben (z. B. Bereinigung).", now));
                }
                else if (d.UsedPercent >= s.DiskWarnPercent)
                {
                    if (TryFire("disk-warn:" + d.Name, now))
                        list.Add(new AlertEvent(AlertRuleId.DiskFull, "disk-warn:" + d.Name, AlertSeverity.Warning, "Wenig Speicherplatz",
                            $"Laufwerk {d.Name} ist zu {pct} % belegt.", now));
                }
            }
        }

        if (s.SmartEnabled && input.SmartWarnings is { } smart)
        {
            foreach (var w in smart)
            {
                var key = "smart:" + w;
                if (TryFire(key, now))
                    list.Add(new AlertEvent(AlertRuleId.SmartWarning, key, AlertSeverity.Critical, "Laufwerkswarnung (SMART)",
                        w + " – Datensicherung prüfen.", now));
            }
        }

        if (s.BlueScreenEnabled && input.NewBlueScreens is { Count: > 0 } blue)
        {
            var latest = blue.Max();
            var key = "bsod:" + latest.ToString("o", CultureInfo.InvariantCulture);
            if (TryFire(key, now))
                list.Add(new AlertEvent(AlertRuleId.BlueScreen, key, AlertSeverity.Critical, "Neuer Bluescreen erkannt",
                    blue.Count == 1
                        ? $"Windows hat am {latest.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)} einen Bluescreen protokolliert. Details unter Absturzanalyse."
                        : $"Windows hat {blue.Count.ToString(CultureInfo.InvariantCulture)} neue Bluescreens protokolliert. Details unter Absturzanalyse.",
                    now));
        }

        if (s.DefenderEnabled && input.DefenderOff == true && TryFire("defender", now))
            list.Add(new AlertEvent(AlertRuleId.DefenderOff, "defender", AlertSeverity.Critical, "Virenschutz inaktiv",
                "Der Echtzeitschutz von Microsoft Defender ist ausgeschaltet und kein anderer aktiver Virenschutz wurde erkannt. Bitte in der Windows-Sicherheit einschalten.", now));

        if (s.BackupEnabled && input.BackupOverdue is { } backups)
        {
            foreach (var b in backups.Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                var key = "backup:" + b;
                if (TryFire(key, now))
                    list.Add(new AlertEvent(AlertRuleId.BackupOverdue, key, AlertSeverity.Warning, "Backup überfällig", b, now));
            }
        }
        return list;
    }

    /// <summary>Wiederholungssperre: true, wenn die Meldung jetzt ausgelöst werden darf (und merkt sich den Zeitpunkt).</summary>
    private bool TryFire(string key, DateTime now)
    {
        if (_lastFired.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(Settings.RepeatLockMinutes))
            return false;
        _lastFired[key] = now;
        return true;
    }
}
