namespace UltimateX.Core.Lifetime;

/// <summary>Ein Abschnitt der eigenen Laufzeit-Mitschrift (ULTIMATE X lief von Start bis Stop bzw. bis zum letzten Heartbeat).</summary>
public sealed record UptimeSession(Guid Id, DateTime StartUtc, DateTime? StopUtc, DateTime? LastHeartbeatUtc);

/// <summary>Zusammenfassung der eigenen Laufzeit-Mitschrift.</summary>
public sealed record UptimeSummary(TimeSpan Total, int Sessions, int SessionsWithoutStop, DateTime? FirstStartUtc);

/// <summary>Art eines Ereignisses aus dem System-Ereignisprotokoll, das für Start/Laufzeit relevant ist.</summary>
public enum BootLogKind
{
    /// <summary>Betriebssystem gestartet (Kernel-General 12 bzw. EventLog 6005).</summary>
    Start,
    /// <summary>Sauber heruntergefahren (Kernel-General 13 bzw. EventLog 6006).</summary>
    Stop,
    /// <summary>Energiesparen/Ruhezustand beginnt (Kernel-Power 42).</summary>
    SleepEnter,
    /// <summary>Fortsetzen nach Energiesparen/Ruhezustand/Schnellstart (Kernel-Power 107, Power-Troubleshooter 1).</summary>
    Resume,
    /// <summary>„System läuft seit … Sekunden“ (EventLog 6013, täglich) – belegt, dass der PC zu diesem Zeitpunkt lief.</summary>
    Alive,
    /// <summary>Unerwartetes Herunterfahren gemeldet (EventLog 6008) bzw. Kernel-Power 41.</summary>
    Unexpected,
}

public sealed record BootLogEntry(DateTime Utc, BootLogKind Kind);

/// <summary>
/// Ergebnis der Auswertung des Ereignisprotokolls. Alle Werte sind Untergrenzen („mindestens“): das Protokoll ist in der Größe
/// begrenzt und reicht nur bis <see cref="EarliestUtc"/> zurück; Abschnitte ohne sauberes Ende zählen nur bis zum letzten Lebenszeichen.
/// </summary>
public sealed record BootHistorySummary(
    int Starts,
    TimeSpan Runtime,
    int UnexpectedShutdowns,
    int SessionsWithoutStop,
    DateTime? EarliestUtc);

/// <summary>Laufzeit-Berechnungen (testbar, ohne Systemzugriff).</summary>
public static class RuntimeCalculator
{
    /// <summary>
    /// Gesamtlaufzeit aus Start/Stop-Paaren der eigenen Mitschrift.
    /// Fehlender Stopp (Absturz, Stromausfall, Abmelden) → Ende = letzter Heartbeat (ohne Heartbeat: 0).
    /// Die laufende Sitzung (<paramref name="currentSessionId"/>) zählt bis <paramref name="nowUtc"/>.
    /// Überlappende Abschnitte (z. B. zwei Instanzen) werden zusammengeführt, Lücken zwischen Abschnitten nicht gezählt.
    /// </summary>
    public static UptimeSummary Summarize(IEnumerable<UptimeSession> sessions, DateTime nowUtc, Guid? currentSessionId = null)
    {
        var intervals = new List<(DateTime From, DateTime To)>();
        int count = 0, withoutStop = 0;
        DateTime? first = null;
        foreach (var s in sessions)
        {
            count++;
            if (first is null || s.StartUtc < first) first = s.StartUtc;
            DateTime end;
            if (s.StopUtc is { } stop) end = stop;
            else if (currentSessionId is { } cur && s.Id == cur) end = nowUtc;
            else
            {
                withoutStop++;
                end = s.LastHeartbeatUtc ?? s.StartUtc;
            }
            if (end > nowUtc) end = nowUtc; // Uhrzeit-Sprünge in die Zukunft nicht mitzählen
            if (end > s.StartUtc) intervals.Add((s.StartUtc, end));
        }
        return new UptimeSummary(MergedLength(intervals), count, withoutStop, first);
    }

    /// <summary>Summe der Länge aller Intervalle, Überlappungen nur einmal gezählt.</summary>
    public static TimeSpan MergedLength(IEnumerable<(DateTime From, DateTime To)> intervals)
    {
        var total = TimeSpan.Zero;
        DateTime? curFrom = null, curTo = null;
        foreach (var (from, to) in intervals.Where(i => i.To > i.From).OrderBy(i => i.From))
        {
            if (curTo is null || from > curTo)
            {
                if (curFrom is { } f && curTo is { } t) total += t - f;
                curFrom = from;
                curTo = to;
            }
            else if (to > curTo)
            {
                curTo = to;
            }
        }
        if (curFrom is { } lf && curTo is { } lt) total += lt - lf;
        return total;
    }

    /// <summary>
    /// Starts und Laufzeit aus dem System-Ereignisprotokoll.
    ///  - Laufzeit zählt nur aktive Phasen: Start/Fortsetzen → Herunterfahren/Energiesparen.
    ///  - Start ohne vorheriges sauberes Ende: der alte Abschnitt zählt nur bis zum letzten Lebenszeichen (6013/anderes Ereignis).
    ///  - Ein offener Abschnitt am Ende ist die aktuelle Sitzung und zählt bis <paramref name="nowUtc"/>.
    /// </summary>
    public static BootHistorySummary SummarizeBootLog(IEnumerable<BootLogEntry> entries, DateTime nowUtc, DateTime? earliestLogUtc = null)
    {
        var sorted = entries.OrderBy(e => e.Utc).ThenBy(e => e.Kind == BootLogKind.Start ? 0 : 1).ToList();
        var runtime = TimeSpan.Zero;
        int starts = 0, unexpected = 0, withoutStop = 0;
        DateTime? running = null;   // aktiver Abschnitt seit …
        DateTime? lastAlive = null; // letztes Lebenszeichen im aktiven Abschnitt
        var paused = false;

        void AddUntil(DateTime end)
        {
            if (running is { } r && end > r) runtime += end - r;
        }

        foreach (var e in sorted)
        {
            switch (e.Kind)
            {
                case BootLogKind.Start:
                    starts++;
                    if (running is not null)
                    {
                        // Kein sauberes Ende protokolliert (Absturz, Strom weg, Schnellstart): nur bis zum letzten Lebenszeichen zählen.
                        withoutStop++;
                        if (lastAlive is { } la) AddUntil(la);
                    }
                    running = e.Utc;
                    lastAlive = e.Utc;
                    paused = false;
                    break;
                case BootLogKind.Stop:
                    if (running is not null) AddUntil(e.Utc);
                    running = null;
                    lastAlive = null;
                    paused = false;
                    break;
                case BootLogKind.SleepEnter:
                    if (running is not null) AddUntil(e.Utc);
                    running = null;
                    lastAlive = null;
                    paused = true;
                    break;
                case BootLogKind.Resume:
                    // Fortsetzen ohne protokollierten Schlafbeginn (z. B. Schnellstart): die Zeit davor war der PC aus bzw. im
                    // Ruhezustand – der alte Abschnitt zählt nur bis zum letzten Lebenszeichen (konservativ, Untergrenze).
                    // Doppelte Meldung desselben Fortsetzens (Kernel-Power 107 + Power-Troubleshooter 1) wird ignoriert.
                    if (running is { } r0 && e.Utc - r0 < TimeSpan.FromMinutes(2) && lastAlive == r0) break;
                    if (running is not null && lastAlive is { } before) AddUntil(before);
                    running = e.Utc;
                    lastAlive = e.Utc;
                    paused = false;
                    break;
                case BootLogKind.Alive:
                    if (running is not null) lastAlive = e.Utc;
                    else if (!paused)
                    {
                        // Lebenszeichen ohne bekannten Start (Protokoll beginnt mitten in einer Sitzung): ab hier zählen.
                        running = e.Utc;
                        lastAlive = e.Utc;
                    }
                    break;
                case BootLogKind.Unexpected:
                    unexpected++;
                    break;
            }
        }
        if (running is not null) AddUntil(nowUtc);

        var earliest = earliestLogUtc;
        if (sorted.Count > 0 && (earliest is null || sorted[0].Utc < earliest)) earliest = sorted[0].Utc;
        return new BootHistorySummary(starts, runtime, unexpected, withoutStop, earliest);
    }

    /// <summary>Kompakte Dauer: „1.234 h (51 T 10 h)“ – Stunden als Hauptwert, für Lebensdauerangaben üblich.</summary>
    public static string HoursText(TimeSpan span, System.Globalization.CultureInfo culture)
    {
        var hours = (long)Math.Floor(span.TotalHours);
        var days = (long)Math.Floor(span.TotalDays);
        return days >= 1
            ? string.Format(culture, "{0:N0} h ({1:N0} T {2} h)", hours, days, span.Hours)
            : string.Format(culture, "{0:N0} h {1} min", hours, span.Minutes);
    }
}
