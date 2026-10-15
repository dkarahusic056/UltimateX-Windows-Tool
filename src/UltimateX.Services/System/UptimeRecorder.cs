using System.Text.Json;
using UltimateX.Core.Common;
using UltimateX.Core.Lifetime;
using UltimateX.Core.Logging;

namespace UltimateX.Services.SystemTools;

/// <summary>
/// Eigene Laufzeit-Mitschrift (v1.3 „PC Lifetime“): ULTIMATE X protokolliert Start, Stop und alle 5 Minuten einen Heartbeat in
/// %LOCALAPPDATA%\UltimateX\uptime-log.json. Stürzt die App ab oder wird Windows hart ausgeschaltet, endet der Abschnitt beim
/// letzten Heartbeat. Liegt zwischen zwei Heartbeats mehr als die doppelte Taktzeit (Energiesparen/Ruhezustand), wird der alte
/// Abschnitt beim letzten Heartbeat geschlossen und ein neuer begonnen – so zählen Schlafphasen nicht als Laufzeit.
/// Rein lokal, keine Telemetrie. Aufruf: <see cref="Start"/> beim App-Start, <see cref="Stop"/> beim Beenden.
/// </summary>
public sealed class UptimeRecorder : IDisposable
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);

    /// <summary>Höchstzahl gespeicherter Abschnitte (älteste werden verworfen; reicht für mehrere Jahre Normalbetrieb).</summary>
    public const int MaxSessions = 5_000;

    private readonly object _gate = new();
    private readonly Func<DateTime> _utcNow;
    private Timer? _timer;
    private Guid? _current;
    private DateTime _lastBeatUtc;

    public UptimeRecorder(string? filePath = null, Func<DateTime>? utcNow = null)
    {
        FilePath = filePath ?? Path.Combine(AppPaths.UserDataRoot, "uptime-log.json");
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public string FilePath { get; }

    public Guid? CurrentSessionId { get { lock (_gate) return _current; } }

    /// <summary>Beginnt einen neuen Abschnitt und startet den 5-Minuten-Heartbeat. Mehrfacher Aufruf ist harmlos.</summary>
    public void Start(bool startTimer = true)
    {
        lock (_gate)
        {
            if (_current is not null) return;
            var now = _utcNow();
            var list = LoadUnlocked();
            var id = Guid.NewGuid();
            list.Add(new UptimeSession(id, now, null, now));
            SaveUnlocked(list);
            _current = id;
            _lastBeatUtc = now;
            if (startTimer) _timer = new Timer(_ => Heartbeat(), null, HeartbeatInterval, HeartbeatInterval);
        }
        AppLog.Info("Uptime", "Laufzeit-Mitschrift gestartet");
    }

    /// <summary>Heartbeat (wird vom Timer aufgerufen; öffentlich für Tests).</summary>
    public void Heartbeat()
    {
        lock (_gate)
        {
            if (_current is not { } id) return;
            var now = _utcNow();
            var list = LoadUnlocked();
            var idx = list.FindIndex(s => s.Id == id);
            if (idx < 0)
            {
                // Datei wurde gelöscht/ersetzt: neuen Abschnitt beginnen.
                id = Guid.NewGuid();
                list.Add(new UptimeSession(id, now, null, now));
                _current = id;
            }
            else if (now - _lastBeatUtc > HeartbeatInterval * 2.2 || now < _lastBeatUtc)
            {
                // Lücke (Energiesparen/Ruhezustand oder Uhrzeit verstellt): alten Abschnitt beim letzten Heartbeat schließen.
                list[idx] = list[idx] with { StopUtc = list[idx].LastHeartbeatUtc ?? list[idx].StartUtc };
                id = Guid.NewGuid();
                list.Add(new UptimeSession(id, now, null, now));
                _current = id;
            }
            else
            {
                list[idx] = list[idx] with { LastHeartbeatUtc = now };
            }
            _lastBeatUtc = now;
            SaveUnlocked(list);
        }
    }

    /// <summary>Schließt den aktuellen Abschnitt (App-Ende, Abmelden, Herunterfahren).</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_current is not { } id) return;
            var now = _utcNow();
            var list = LoadUnlocked();
            var idx = list.FindIndex(s => s.Id == id);
            if (idx >= 0)
            {
                // Nach einer unbemerkten Schlafphase nur bis zum letzten Heartbeat zählen.
                var stop = now - _lastBeatUtc > HeartbeatInterval * 2.2 ? (list[idx].LastHeartbeatUtc ?? list[idx].StartUtc) : now;
                list[idx] = list[idx] with { StopUtc = stop, LastHeartbeatUtc = stop };
                SaveUnlocked(list);
            }
            _current = null;
        }
        AppLog.Info("Uptime", "Laufzeit-Mitschrift beendet");
    }

    public IReadOnlyList<UptimeSession> Load()
    {
        lock (_gate) return LoadUnlocked();
    }

    public UptimeSummary Summarize() => RuntimeCalculator.Summarize(Load(), _utcNow(), CurrentSessionId);

    public void Dispose() => Stop();

    private List<UptimeSession> LoadUnlocked()
    {
        try
        {
            if (!File.Exists(FilePath)) return [];
            return JsonSerializer.Deserialize<List<UptimeSession>>(File.ReadAllText(FilePath))?
                .Select(s => s with
                {
                    StartUtc = DateTime.SpecifyKind(s.StartUtc, DateTimeKind.Utc),
                    StopUtc = s.StopUtc is { } st ? DateTime.SpecifyKind(st, DateTimeKind.Utc) : null,
                    LastHeartbeatUtc = s.LastHeartbeatUtc is { } hb ? DateTime.SpecifyKind(hb, DateTimeKind.Utc) : null,
                })
                .ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppLog.Warn("Uptime", "Laufzeit-Mitschrift nicht lesbar", ex.Message);
            return [];
        }
    }

    private void SaveUnlocked(List<UptimeSession> list)
    {
        try
        {
            if (list.Count > MaxSessions) list.RemoveRange(0, list.Count - MaxSessions);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Uptime", "Laufzeit-Mitschrift nicht gespeichert", ex.Message);
        }
    }
}
