using UltimateX.Core.Common;

namespace UltimateX.Services.Benchmark;

/// <summary>Ein Messpunkt während eines Benchmarks. Null = Sensor nicht verfügbar (es wird nichts geschätzt).</summary>
public sealed record LoadSample(
    DateTime Time,
    double? CpuLoad = null,
    double? CpuClockMHz = null,
    double? CpuTempC = null,
    double? CpuPowerW = null,
    double? GpuLoad = null,
    double? GpuClockMHz = null,
    double? GpuTempC = null,
    double? GpuHotspotC = null,
    double? GpuFanPercent = null,
    double? GpuPowerW = null,
    ulong? VramUsedBytes = null);

/// <summary>Zusammenfassung der Telemetrie eines Laufs.</summary>
public sealed record LoadSummary(
    int Samples,
    double? CpuLoadAvg, double? CpuClockAvg, double? CpuClockMax, double? CpuTempAvg, double? CpuTempMax, double? CpuPowerMax,
    double? GpuLoadAvg, double? GpuClockAvg, double? GpuClockMax, double? GpuTempAvg, double? GpuTempMax, double? GpuHotspotMax,
    double? GpuFanMax, double? GpuPowerMax, ulong? VramUsedMax,
    bool? CpuThrottled, bool? GpuThrottled)
{
    public static LoadSummary Empty { get; } = new(0, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
}

/// <summary>
/// Sammelt während eines Tests einmal pro Sekunde Sensorwerte (Quelle: Sensor-Manager der App, siehe <see cref="Sampler"/>)
/// und erkennt Drosselung: Der Takt fällt im letzten Drittel um mehr als 10 % gegenüber dem ersten Drittel,
/// während die Temperatur nahe der Grenze liegt. Ohne Takt- und Temperatursensor → „nicht bestimmbar“ (null).
/// </summary>
public sealed class LoadMonitor : IDisposable
{
    /// <summary>Wird von der App gesetzt (verbindet Sensor-Manager und Live-Monitor). Ohne Sampler: keine Telemetrie.</summary>
    public static Func<LoadSample>? Sampler { get; set; }

    private readonly List<LoadSample> _samples = [];
    private readonly Timer? _timer;
    private readonly object _gate = new();

    private LoadMonitor(TimeSpan interval)
    {
        if (Sampler is null) return;
        _timer = new Timer(_ => Take(), null, TimeSpan.Zero, interval);
    }

    public static LoadMonitor Start(double intervalSeconds = 1) => new(TimeSpan.FromSeconds(intervalSeconds));

    private void Take()
    {
        try
        {
            var s = Sampler?.Invoke();
            if (s is null) return;
            lock (_gate) _samples.Add(s);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or ObjectDisposedException)
        {
            // Ein fehlender Messpunkt ist unkritisch.
        }
    }

    public IReadOnlyList<LoadSample> Samples
    {
        get { lock (_gate) return _samples.ToList(); }
    }

    public LoadSummary Stop()
    {
        _timer?.Dispose();
        return Summarize(Samples);
    }

    public void Dispose() => _timer?.Dispose();

    public static LoadSummary Summarize(IReadOnlyList<LoadSample> s)
    {
        if (s.Count == 0) return LoadSummary.Empty;
        static double? Avg(IEnumerable<double?> v) { var l = v.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Average(), 1); }
        static double? Max(IEnumerable<double?> v) { var l = v.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : Math.Round(l.Max(), 1); }

        var vram = s.Where(x => x.VramUsedBytes is not null).Select(x => x.VramUsedBytes!.Value).DefaultIfEmpty().Max();
        return new LoadSummary(
            s.Count,
            Avg(s.Select(x => x.CpuLoad)), Avg(s.Select(x => x.CpuClockMHz)), Max(s.Select(x => x.CpuClockMHz)),
            Avg(s.Select(x => x.CpuTempC)), Max(s.Select(x => x.CpuTempC)), Max(s.Select(x => x.CpuPowerW)),
            Avg(s.Select(x => x.GpuLoad)), Avg(s.Select(x => x.GpuClockMHz)), Max(s.Select(x => x.GpuClockMHz)),
            Avg(s.Select(x => x.GpuTempC)), Max(s.Select(x => x.GpuTempC)), Max(s.Select(x => x.GpuHotspotC)),
            Max(s.Select(x => x.GpuFanPercent)), Max(s.Select(x => x.GpuPowerW)), vram == 0 ? null : vram,
            DetectThrottling(s.Select(x => x.CpuClockMHz).ToList(), s.Select(x => x.CpuTempC).ToList(), 90),
            DetectThrottling(s.Select(x => x.GpuClockMHz).ToList(), s.Select(x => x.GpuHotspotC ?? x.GpuTempC).ToList(), s.Any(x => x.GpuHotspotC is not null) ? 100 : 85));
    }

    /// <summary>
    /// Drosselung: Takt im letzten Drittel &lt; 90 % des ersten Drittels UND Höchsttemperatur ≥ Grenze.
    /// Ohne mindestens 6 Takt- und Temperaturwerte → null (nicht bestimmbar).
    /// </summary>
    public static bool? DetectThrottling(IReadOnlyList<double?> clocks, IReadOnlyList<double?> temps, double tempLimit)
    {
        var c = clocks.Where(x => x is > 0).Select(x => x!.Value).ToList();
        var t = temps.Where(x => x is > 0).Select(x => x!.Value).ToList();
        if (c.Count < 6 || t.Count < 3) return null;
        var third = c.Count / 3;
        var early = c.Take(third).Average();
        var late = c.Skip(c.Count - third).Average();
        return late < early * 0.9 && t.Max() >= tempLimit;
    }

    public static string ThrottleText(bool? throttled) => throttled switch
    {
        true => "erkannt",
        false => "nicht erkannt",
        _ => "N/A",
    };

    public static DataState ThrottleState(bool? throttled) => throttled switch
    {
        true => DataState.Problem,
        false => DataState.Ok,
        _ => DataState.NotAvailable,
    };
}
