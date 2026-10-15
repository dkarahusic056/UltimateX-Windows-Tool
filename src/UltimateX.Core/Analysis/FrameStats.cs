namespace UltimateX.Core.Analysis;

/// <summary>
/// Momentaufnahme der Bildrate. Alle Werte null = keine Frames vom beobachteten Prozess (keine Quelle → N/A).
/// </summary>
/// <param name="Fps">Frames der letzten Sekunde (bezogen auf den jüngsten Frame).</param>
/// <param name="FrametimeMs">Mittlere Frametime der letzten Sekunde.</param>
/// <param name="OnePercentLowFps">„1 % Low“: Mittel der langsamsten 1 % aller Frametimes im gleitenden Fenster, als FPS.</param>
/// <param name="FramesInWindow">Anzahl Frames im gleitenden Fenster (Standard 10 s).</param>
public sealed record FrameSnapshot(double? Fps, double? FrametimeMs, double? OnePercentLowFps, int FramesInWindow)
{
    public static FrameSnapshot Empty { get; } = new(null, null, null, 0);

    public bool HasData => Fps is not null;
}

/// <summary>
/// Bildraten-Statistik aus Present-Zeitstempeln (v1.3 In-Game-Overlay). Reine Logik ohne Windows-Zugriff → testbar.
///
/// - Jeder Present-Aufruf eines Prozesses ist ein Frame; die Frametime ist der Abstand zum vorherigen Present.
/// - FPS = Anzahl Frames in der letzten Sekunde (vor dem jüngsten Frame). Solange noch keine volle Sekunde vorliegt,
///   wird 1000 / mittlere Frametime gemeldet.
/// - 1 % Low = 1000 / Mittelwert der langsamsten 1 % Frametimes (mindestens ein Frame) im gleitenden Fenster.
///   Unter 10 Frames im Fenster wird kein 1 %-Low gemeldet (statistisch nicht aussagekräftig).
/// - Lücken über <see cref="MaxGapSeconds"/> (Pause, Ladebildschirm, Fenster minimiert) werden nicht als Frametime gezählt.
/// - Liegt der jüngste Frame länger als <see cref="StaleSeconds"/> zurück, gibt es keine Werte (N/A statt einer alten Zahl).
///
/// Zeitstempel sind Sekunden auf einer beliebigen, monoton wachsenden Achse. Thread-sicher
/// (Frames kommen vom ETW-Thread, gelesen wird von der Oberfläche).
/// </summary>
public sealed class FrameStats
{
    private readonly object _gate = new();
    private readonly Queue<(double T, double DtMs)> _frames = new();
    private double? _lastT;
    private double? _firstT;

    public FrameStats(double windowSeconds = 10, double staleSeconds = 2.5, double maxGapSeconds = 1.0)
    {
        if (windowSeconds < 1) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
        WindowSeconds = windowSeconds;
        StaleSeconds = staleSeconds;
        MaxGapSeconds = maxGapSeconds;
    }

    public double WindowSeconds { get; }
    public double StaleSeconds { get; }
    public double MaxGapSeconds { get; }

    /// <summary>Registriert einen Present-Aufruf zum Zeitpunkt <paramref name="timestampSeconds"/>.</summary>
    public void AddPresent(double timestampSeconds)
    {
        if (double.IsNaN(timestampSeconds) || double.IsInfinity(timestampSeconds)) return;
        lock (_gate)
        {
            if (_lastT is { } last)
            {
                var dt = timestampSeconds - last;
                if (dt <= 0) return; // doppelt oder (ETW-Puffer) leicht vertauscht → verwerfen
                if (dt <= MaxGapSeconds) _frames.Enqueue((timestampSeconds, dt * 1000.0));
                else _firstT = timestampSeconds; // Pause: neu beginnen, die Lücke ist keine Frametime
            }
            else
            {
                _firstT = timestampSeconds;
            }
            _lastT = timestampSeconds;
            TrimLocked(timestampSeconds - WindowSeconds);
        }
    }

    /// <summary>Verwirft alle Daten (z. B. wenn ein anderer Prozess in den Vordergrund kommt).</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _frames.Clear();
            _lastT = null;
            _firstT = null;
        }
    }

    /// <summary>Statistik zum Zeitpunkt <paramref name="nowSeconds"/> (gleiche Zeitachse wie <see cref="AddPresent"/>).</summary>
    public FrameSnapshot Snapshot(double nowSeconds)
    {
        lock (_gate)
        {
            if (_lastT is not { } last || nowSeconds - last > StaleSeconds)
                return FrameSnapshot.Empty;
            TrimLocked(last - WindowSeconds);
            if (_frames.Count == 0) return FrameSnapshot.Empty;

            var since = last - 1.0;
            var lastSecond = _frames.Where(f => f.T > since).Select(f => f.DtMs).ToList();
            if (lastSecond.Count == 0) return FrameSnapshot.Empty;

            var meanMs = lastSecond.Average();
            var coveredSeconds = last - (_firstT ?? last);
            double fps = coveredSeconds >= 1.0 ? lastSecond.Count : 1000.0 / meanMs;

            double? low = null;
            if (_frames.Count >= 10)
            {
                var worst = _frames.Select(f => f.DtMs).OrderByDescending(x => x).ToList();
                var n = Math.Max(1, (int)Math.Ceiling(worst.Count * 0.01));
                low = 1000.0 / worst.Take(n).Average();
            }
            return new FrameSnapshot(fps, meanMs, low, _frames.Count);
        }
    }

    private void TrimLocked(double cutoff)
    {
        while (_frames.Count > 0 && _frames.Peek().T <= cutoff) _frames.Dequeue();
    }
}
