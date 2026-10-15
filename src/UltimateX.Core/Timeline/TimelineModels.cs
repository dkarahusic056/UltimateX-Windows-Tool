namespace UltimateX.Core.Timeline;

/// <summary>Art eines Ereignisses auf der Zeitleiste „Was ist passiert?“.</summary>
public enum TimelineKind
{
    WindowsUpdate,
    WindowsUpdateFailed,
    ProgramInstall,
    ProgramUninstall,
    DriverInstall,
    BlueScreen,
    UnexpectedRestart,
    AppCrash,
    Startup,
    Shutdown,
    Snapshot,
    Backup,
}

/// <summary>Ein Ereignis. <see cref="DateOnly"/> = nur das Datum ist bekannt (z. B. InstallDate aus der Registry).</summary>
public sealed record TimelineEvent(DateTime Time, TimelineKind Kind, string Title, string Detail, string Source, bool DateOnly = false)
{
    public string KindLabel => TimelineRules.Label(Kind);
}

/// <summary>Ursachen-Hinweis: ein Absturz kurz nach einer Treiber- oder Update-Installation.</summary>
public sealed record CauseHint(TimelineEvent Crash, TimelineEvent Change, double HoursAfter, int OtherChangesInWindow)
{
    public string Text =>
        $"{TimelineRules.Label(Crash.Kind)} am {Crash.Time:dd.MM.yyyy HH:mm} – {HoursAfter:0} h nach {TimelineRules.Label(Change.Kind)}: {Change.Title}";
}

/// <summary>Einordnung und Korrelationsregeln der Zeitleiste (testbar, ohne Systemzugriff).</summary>
public static class TimelineRules
{
    /// <summary>Zeitfenster nach einer Änderung, in dem ein erster Absturz als möglicher Zusammenhang markiert wird.</summary>
    public static readonly TimeSpan CorrelationWindow = TimeSpan.FromHours(48);

    /// <summary>Zeitraum vor der Änderung, der „ruhig“ gewesen sein muss (sonst bestand das Problem schon vorher).</summary>
    public static readonly TimeSpan QuietBefore = TimeSpan.FromDays(7);

    public static string Label(TimelineKind kind) => kind switch
    {
        TimelineKind.WindowsUpdate => "Windows-Update",
        TimelineKind.WindowsUpdateFailed => "Update fehlgeschlagen",
        TimelineKind.ProgramInstall => "Programm installiert",
        TimelineKind.ProgramUninstall => "Programm entfernt",
        TimelineKind.DriverInstall => "Treiber installiert",
        TimelineKind.BlueScreen => "Bluescreen",
        TimelineKind.UnexpectedRestart => "Unerwarteter Neustart",
        TimelineKind.AppCrash => "Programmabsturz",
        TimelineKind.Startup => "Start",
        TimelineKind.Shutdown => "Herunterfahren",
        TimelineKind.Snapshot => "Snapshot",
        TimelineKind.Backup => "Sicherung",
        _ => kind.ToString(),
    };

    /// <summary>Segoe-Fluent-Icons-Glyph je Art.</summary>
    public static string Glyph(TimelineKind kind) => kind switch
    {
        TimelineKind.WindowsUpdate or TimelineKind.WindowsUpdateFailed => "\uE895",
        TimelineKind.ProgramInstall => "\uE710",
        TimelineKind.ProgramUninstall => "\uE74D",
        TimelineKind.DriverInstall => "\uE964",
        TimelineKind.BlueScreen => "\uEA39",
        TimelineKind.UnexpectedRestart => "\uE7BA",
        TimelineKind.AppCrash => "\uE783",
        TimelineKind.Startup => "\uE7E8",
        TimelineKind.Shutdown => "\uE7E8",
        TimelineKind.Snapshot => "\uE722",
        TimelineKind.Backup => "\uE8F1",
        _ => "\uE946",
    };

    public static bool IsSystemCrash(TimelineKind k) => k is TimelineKind.BlueScreen or TimelineKind.UnexpectedRestart;

    public static bool IsCrash(TimelineKind k) => IsSystemCrash(k) || k == TimelineKind.AppCrash;

    /// <summary>Änderungen, die als Auslöser in Frage kommen.</summary>
    public static bool IsChange(TimelineKind k) => k is TimelineKind.DriverInstall or TimelineKind.WindowsUpdate;

    public static bool IsProblem(TimelineKind k) => IsCrash(k) || k == TimelineKind.WindowsUpdateFailed;

    /// <summary>
    /// Markiert den jeweils ERSTEN Absturz je Gruppe (Systemabsturz = Bluescreen/unerwarteter Neustart, Programmabsturz)
    /// innerhalb von 48 h nach einer Treiber- oder Update-Installation – aber nur, wenn es in den 7 Tagen vor der Änderung
    /// keinen Absturz derselben Gruppe gab. Mehrere Änderungen vor demselben Absturz: die zeitlich nächste wird genannt.
    /// Das ist ein Hinweis auf einen möglichen Zusammenhang, kein Beweis.
    /// </summary>
    public static IReadOnlyList<CauseHint> Correlate(IEnumerable<TimelineEvent> events)
    {
        var list = events.Where(e => !e.DateOnly).OrderBy(e => e.Time).ToList();
        var changes = list.Where(e => IsChange(e.Kind)).ToList();
        var crashes = list.Where(e => IsCrash(e.Kind)).ToList();
        var hints = new List<CauseHint>();
        var used = new HashSet<TimelineEvent>(ReferenceEqualityComparer.Instance);

        foreach (var group in new[] { true, false }) // true = Systemabstürze, false = Programmabstürze
        {
            var groupCrashes = crashes.Where(c => IsSystemCrash(c.Kind) == group).ToList();
            foreach (var change in changes)
            {
                var quiet = !groupCrashes.Any(c => c.Time < change.Time && c.Time >= change.Time - QuietBefore);
                if (!quiet) continue;
                var first = groupCrashes.FirstOrDefault(c => c.Time > change.Time && c.Time <= change.Time + CorrelationWindow);
                if (first is null || used.Contains(first)) continue;
                // Nächstliegende Änderung vor diesem Absturz bestimmen (gleiche Regel „ruhig davor“).
                var inWindow = changes.Where(c => c.Time < first.Time && first.Time - c.Time <= CorrelationWindow).ToList();
                var closest = inWindow.OrderByDescending(c => c.Time).First();
                used.Add(first);
                hints.Add(new CauseHint(first, closest, (first.Time - closest.Time).TotalHours, inWindow.Count - 1));
            }
        }
        return hints.OrderByDescending(h => h.Crash.Time).ToList();
    }

    /// <summary>Filter für die Ansicht: Arten (leer = alle) und Suchtext in Titel/Detail/Quelle (ohne Groß-/Kleinschreibung).</summary>
    public static IEnumerable<TimelineEvent> Filter(IEnumerable<TimelineEvent> events, IReadOnlySet<TimelineKind>? kinds, string? search)
    {
        var q = search?.Trim();
        return events.Where(e => (kinds is null || kinds.Count == 0 || kinds.Contains(e.Kind))
            && (string.IsNullOrEmpty(q)
                || e.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || e.Detail.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || e.Source.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                || Label(e.Kind).Contains(q, StringComparison.CurrentCultureIgnoreCase)));
    }

    /// <summary>Gruppierung nach Kalendertag (neueste zuerst; innerhalb des Tages neueste zuerst).</summary>
    public static IReadOnlyList<(DateTime Day, IReadOnlyList<TimelineEvent> Events)> GroupByDay(IEnumerable<TimelineEvent> events)
        => events.GroupBy(e => e.Time.Date)
            .OrderByDescending(g => g.Key)
            .Select(g => (g.Key, (IReadOnlyList<TimelineEvent>)g.OrderByDescending(e => e.Time).ToList()))
            .ToList();

    /// <summary>
    /// Doppelte Programminstallationen entfernen: liegt für dasselbe Programm am selben Tag ein MsiInstaller-Ereignis vor,
    /// entfällt der reine Registry-Eintrag (nur Datum).
    /// </summary>
    public static List<TimelineEvent> DeduplicateInstalls(IEnumerable<TimelineEvent> events)
    {
        var all = events.ToList();
        var exact = all.Where(e => e.Kind == TimelineKind.ProgramInstall && !e.DateOnly)
            .Select(e => (e.Time.Date, Name: Normalize(e.Title))).ToHashSet();
        return all.Where(e => !(e.Kind == TimelineKind.ProgramInstall && e.DateOnly && exact.Contains((e.Time.Date, Normalize(e.Title))))).ToList();

        static string Normalize(string s) => s.Trim().ToLowerInvariant();
    }

    /// <summary>MsiInstaller-Text „Product: Name -- Installation completed successfully.“ → „Name“ (sprachunabhängig, best effort).</summary>
    public static string MsiProductName(string raw)
    {
        var s = raw.Trim();
        var colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && colon < 20) s = s[(colon + 1)..].Trim();
        var dash = s.IndexOf(" -- ", StringComparison.Ordinal);
        if (dash > 0) s = s[..dash].Trim();
        return s.Length > 200 ? s[..200] : s;
    }
}
