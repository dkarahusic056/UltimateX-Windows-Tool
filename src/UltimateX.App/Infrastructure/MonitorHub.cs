using System.Windows.Threading;
using UltimateX.Core.Logging;
using UltimateX.Services.Monitoring;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Zentraler 1-Hz-Sampler für Live-Werte (§19, §89). Genau EIN Timer für die ganze App;
/// Seiten abonnieren <see cref="SampleArrived"/>. Historie: 15 Minuten (900 Samples).
/// Die Messung läuft auf einem Threadpool-Thread, damit die UI nie blockiert.
/// </summary>
public static class MonitorHub
{
    public const int MaxSamples = 900;

    private static readonly DispatcherTimer Timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private static readonly List<MonitorSample> History = new(MaxSamples);
    private static bool _sampling;

    public static event Action<MonitorSample>? SampleArrived;

    public static MonitorSample? Latest { get; private set; }

    public static void Start()
    {
        Timer.Tick += async (_, _) =>
        {
            if (_sampling) return; // Überlappung verhindern, falls ein Sample länger dauert
            _sampling = true;
            try
            {
                var s = await Task.Run(() => AppServices.Monitor.Sample());
                Latest = s;
                History.Add(s);
                if (History.Count > MaxSamples) History.RemoveRange(0, History.Count - MaxSamples);
                SampleArrived?.Invoke(s);
            }
            catch (Exception ex)
            {
                AppLog.Warn("Monitor", "Messung fehlgeschlagen", ex.Message);
            }
            finally
            {
                _sampling = false;
            }
        };
        Timer.Start();
    }

    public static void Stop() => Timer.Stop();

    /// <summary>Die letzten <paramref name="count"/> Samples (älteste zuerst).</summary>
    public static IReadOnlyList<MonitorSample> Recent(int count)
        => History.Count <= count ? History.ToList() : History.GetRange(History.Count - count, count);
}
