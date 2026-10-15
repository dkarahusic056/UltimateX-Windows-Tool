namespace UltimateX.Core.Backup;

/// <summary>Eine gefundene Quelldatei.</summary>
public sealed record BackupSourceFile(string FullPath, string StorePath, long Size, DateTime LastWriteUtc);

public enum BackupFileAction
{
    /// <summary>Neu oder geändert → aus der Quelle kopieren.</summary>
    Copy,
    /// <summary>Unverändert → Hardlink auf die Datei der letzten Version (Fallback: Kopie).</summary>
    Link,
}

public sealed record BackupPlanItem(BackupSourceFile File, BackupFileAction Action, BackupManifestEntry? Previous);

/// <summary>Reine Entscheidungslogik des Backup-Centers (ohne Dateisystemzugriffe, testbar).</summary>
public static class BackupPlanner
{
    /// <summary>Toleranz für Zeitstempel (FAT/exFAT speichern nur 2-Sekunden-Schritte).</summary>
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Entscheidet je Datei Kopie oder Hardlink. Unverändert = gleiche Größe und gleiche Änderungszeit (±2 s) wie in der letzten
    /// vollständigen Version. Im Hash-Modus muss zusätzlich <paramref name="sourceHash"/> mit dem gespeicherten SHA-256 übereinstimmen;
    /// fehlt der gespeicherte Hash, wird kopiert.
    /// </summary>
    public static BackupPlanItem Decide(BackupSourceFile file, IReadOnlyDictionary<string, BackupManifestEntry>? previous,
        BackupCompareMode mode, Func<BackupSourceFile, string?>? sourceHash = null)
    {
        if (previous is null || !previous.TryGetValue(file.StorePath, out var prev)) return new(file, BackupFileAction.Copy, null);
        var same = prev.Size == file.Size && (prev.LastWriteUtc - file.LastWriteUtc).Duration() <= TimeTolerance;
        if (same && mode == BackupCompareMode.SizeTimeAndHash)
        {
            var h = sourceHash?.Invoke(file);
            same = h is not null && prev.Sha256 is not null && h.Equals(prev.Sha256, StringComparison.OrdinalIgnoreCase);
        }
        return new(file, same ? BackupFileAction.Link : BackupFileAction.Copy, prev);
    }

    public static Dictionary<string, BackupManifestEntry> Index(BackupManifest? manifest)
    {
        var d = new Dictionary<string, BackupManifestEntry>(StringComparer.OrdinalIgnoreCase);
        if (manifest is null) return d;
        foreach (var e in manifest.Files)
            if (BackupPaths.IsValidStoreRelative(e.Path)) d[e.Path] = e;
        return d;
    }

    /// <summary>
    /// Aufbewahrung: Die <paramref name="keep"/> neuesten VOLLSTÄNDIGEN Versionen bleiben. Gelöscht werden ältere vollständige Versionen
    /// sowie unvollständige (abgebrochene) Versionen, die älter als die älteste behaltene sind. Nur gültige Versionsnamen
    /// (yyyy-MM-dd_HHmmss) kommen überhaupt in Frage; alles andere wird nie angefasst. Ohne vollständige Version wird nichts gelöscht.
    /// </summary>
    public static IReadOnlyList<string> SelectVersionsToDelete(IEnumerable<(string Name, bool Complete)> versions, int keep)
    {
        if (keep < BackupProfile.MinKeep) keep = BackupProfile.MinKeep;
        var valid = versions.Where(v => BackupPaths.IsVersionName(v.Name))
            .OrderByDescending(v => v.Name, StringComparer.Ordinal).ToList();
        var complete = valid.Where(v => v.Complete).ToList();
        if (complete.Count <= keep)
        {
            if (complete.Count == 0) return [];
            var oldestKeptAll = complete[^1].Name;
            return valid.Where(v => !v.Complete && string.CompareOrdinal(v.Name, oldestKeptAll) < 0).Select(v => v.Name).ToList();
        }
        var oldestKept = complete[keep - 1].Name;
        return valid.Where(v => string.CompareOrdinal(v.Name, oldestKept) < 0).Select(v => v.Name).ToList();
    }

    /// <summary>Letzter planmäßiger Zeitpunkt (lokal) ≤ <paramref name="nowLocal"/>; null bei manuell/Anstecken.</summary>
    public static DateTime? MostRecentSlot(BackupScheduleSettings schedule, DateTime nowLocal)
    {
        var tod = schedule.TimeOfDay;
        if (tod < TimeSpan.Zero || tod >= TimeSpan.FromDays(1)) tod = new TimeSpan(19, 0, 0);
        switch (schedule.Kind)
        {
            case BackupScheduleKind.Daily:
            {
                var slot = nowLocal.Date + tod;
                return slot <= nowLocal ? slot : slot.AddDays(-1);
            }
            case BackupScheduleKind.Weekly:
            {
                var diff = ((int)nowLocal.DayOfWeek - (int)schedule.DayOfWeek + 7) % 7;
                var slot = nowLocal.Date.AddDays(-diff) + tod;
                return slot <= nowLocal ? slot : slot.AddDays(-7);
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Fällig (täglich/wöchentlich), wenn seit dem letzten planmäßigen Zeitpunkt noch kein Versuch lief. Verpasste Termine
    /// (PC aus, App geschlossen) werden nachgeholt, sobald ULTIMATE X läuft. Ein neues Profil startet nicht rückwirkend.
    /// </summary>
    public static bool IsDue(BackupProfile profile, DateTime nowLocal)
    {
        if (!profile.Enabled) return false;
        var slot = MostRecentSlot(profile.Schedule, nowLocal);
        if (slot is null) return false;
        var baseline = profile.LastAttemptUtc ?? profile.CreatedUtc;
        return baseline.ToLocalTime() < slot.Value;
    }

    /// <summary>„Beim Anstecken“: höchstens eine erfolgreiche Sicherung je 20 Stunden.</summary>
    public static bool ShouldRunOnConnect(BackupProfile profile, DateTime nowUtc)
        => profile.Enabled && profile.Schedule.Kind == BackupScheduleKind.OnTargetConnected
           && (profile.LastSuccessUtc is null || nowUtc - profile.LastSuccessUtc.Value >= TimeSpan.FromHours(20));

    /// <summary>Überfällig, wenn die letzte erfolgreiche Sicherung älter als <paramref name="days"/> Tage ist (bzw. nie, und das Profil ist älter).</summary>
    public static BackupOverdueInfo? CheckOverdue(BackupProfile profile, int days, DateTime nowUtc)
    {
        if (!profile.Enabled || days <= 0) return null;
        var limit = TimeSpan.FromDays(days);
        if (profile.LastSuccessUtc is { } last)
            return nowUtc - last > limit ? new BackupOverdueInfo(profile.Id, profile.Name, last, (int)(nowUtc - last).TotalDays) : null;
        return nowUtc - profile.CreatedUtc > limit ? new BackupOverdueInfo(profile.Id, profile.Name, null, null) : null;
    }

    /// <summary>Benötigter freier Platz: zu kopierende Bytes + 5 % + 64 MB Reserve (Manifest, Dateisystem-Overhead).</summary>
    public static long RequiredFreeBytes(long bytesToCopy) => bytesToCopy + bytesToCopy / 20 + 64L * 1024 * 1024;

    /// <summary>Wählt eine reproduzierbare Stichprobe (gleichmäßig verteilt) für die Hash-Prüfung.</summary>
    public static IReadOnlyList<T> Sample<T>(IReadOnlyList<T> items, int count)
    {
        if (count <= 0 || items.Count == 0) return [];
        if (items.Count <= count) return items;
        var step = items.Count / (double)count;
        var result = new List<T>(count);
        for (var i = 0; i < count; i++) result.Add(items[(int)(i * step)]);
        return result;
    }
}
