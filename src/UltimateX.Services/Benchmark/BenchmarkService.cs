using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Data;

namespace UltimateX.Services.Benchmark;

public sealed record BenchmarkMetric(string Name, double Value, string Unit);

/// <summary>Ergebnis eines Testlaufs. Punkte sind relative Einheiten – vergleichbar nur mit eigenen früheren Läufen.</summary>
public sealed record BenchmarkRun(string Kind, DateTime Time, double Score, IReadOnlyList<BenchmarkMetric> Metrics, string? Note = null,
    LoadSummary? Telemetry = null)
{
    /// <summary>Messwert per Name (null, wenn nicht vorhanden).</summary>
    public double? Metric(string name) => Metrics.FirstOrDefault(m => m.Name == name)?.Value;
}

/// <summary>Optionen des CPU-Benchmarks (v1.2): optional eine längere Dauerlast zur Erkennung von Drosselung.</summary>
public sealed record CpuBenchmarkOptions(int SecondsPerTest = 2, int SustainedSeconds = 0);

/// <summary>Testmodi des Datenträger-Benchmarks (v1.2 §6). Große Schreibtests nur im Modus „Extended“.</summary>
public enum DiskTestMode { Quick, Normal, Extended }

public sealed record BenchmarkProgress(string Step, double Percent);

/// <summary>
/// Lokale Benchmarks (§103) – alles läuft offline, ohne Fremdsoftware und ohne Adminrechte.
///
///  CPU:  drei Lasten (Ganzzahl: Primzahlsieb, Gleitkomma: Mandelbrot, Krypto: SHA-256), je einmal auf
///        einem Kern und auf allen logischen Prozessoren. Punkte = Arbeitseinheiten pro Sekunde (geometrisches Mittel).
///  RAM:  Kopierbandbreite (alle Kerne) und Zugriffslatenz (zufällige Zeigerkette, größer als jeder Cache).
///  Disk: sequentiell schreiben/lesen und 4K-Zufallszugriffe mit QD1, am Windows-Cache vorbei (FILE_FLAG_NO_BUFFERING),
///        mit Zufallsdaten (SSD-Komprimierung verfälscht sonst Werte). Die Testdatei wird immer gelöscht.
///
/// Es gibt bewusst keine „Online-Rangliste“: ohne geprüfte Vergleichsdaten wäre ein Prozentrang erfunden.
/// </summary>
public sealed class BenchmarkService(HistoryDatabase db)
{
    public const string KindCpu = "CPU";
    public const string KindMemory = "RAM";
    public const string KindDisk = "Datenträger";
    public const string KindGpu = "GPU";
    public const string KindNetwork = "Netzwerk";
    public const string KindComplete = "Complete";
    public const string KindMemTest = "RAM-Test";

    // Metriknamen, die die Gesamtwertung (PerformanceScore) auswertet.
    public const string MetricSingle = "Single-Core-Punkte";
    public const string MetricMulti = "Multi-Core-Punkte";
    public const string MetricRamRead = "Lesen";
    public const string MetricRamLatency = "Latenz";
    public const string MetricDiskSeqRead = "Sequentiell lesen";
    public const string MetricDisk4kRead = "4K zufällig lesen (QD1)";

    // ---------------------------------------------------------------- CPU

    public BenchmarkRun RunCpu(IProgress<BenchmarkProgress>? progress, CancellationToken ct) => RunCpu(new CpuBenchmarkOptions(), progress, ct);

    /// <summary>
    /// CPU-Benchmark v1.2: sieben Lasten (Ganzzahl, Gleitkomma, Krypto/SHA-256, Kompression, Dekompression, AES-Verschlüsselung,
    /// Rendering/Raytracing) je auf einem Kern und auf allen logischen Prozessoren, optional gefolgt von einer Dauerlast.
    /// Takt, Auslastung, Temperatur und Leistungsaufnahme stammen – sofern vorhanden – aus den Sensoren.
    /// </summary>
    public BenchmarkRun RunCpu(CpuBenchmarkOptions options, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var seconds = Math.Clamp(options.SecondsPerTest, 1, 10);
        var threads = Environment.ProcessorCount;
        var tests = new (string Name, Func<WorkState, long> Unit)[]
        {
            ("Ganzzahl (Primzahlsieb)", PrimeSieveUnit),
            ("Gleitkomma (Mandelbrot)", MandelbrotUnit),
            ("Krypto (SHA-256)", ShaUnit),
            ("Kompression (Deflate)", CompressUnit),
            ("Dekompression (Deflate)", DecompressUnit),
            ("Verschlüsselung (AES-256-GCM)", AesUnit),
            ("Rendering (Raytracing)", RenderUnit),
        };

        var metrics = new List<BenchmarkMetric>();
        var single = new List<double>();
        var multi = new List<double>();
        var step = 0;
        var totalSteps = tests.Length * 2 + (options.SustainedSeconds > 0 ? 1 : 0);
        using var monitor = LoadMonitor.Start();
        var watch = Stopwatch.StartNew();
        foreach (var (name, unit) in tests)
        {
            progress?.Report(new BenchmarkProgress($"{name} – 1 Kern", 100.0 * step++ / totalSteps));
            var s = Measure(unit, 1, seconds, ct);
            progress?.Report(new BenchmarkProgress($"{name} – {threads} Threads", 100.0 * step++ / totalSteps));
            var m = Measure(unit, threads, seconds, ct);
            single.Add(s);
            multi.Add(m);
            metrics.Add(new BenchmarkMetric($"{name}, 1 Kern", Math.Round(s, 1), "Einheiten/s"));
            metrics.Add(new BenchmarkMetric($"{name}, alle Kerne", Math.Round(m, 1), "Einheiten/s"));
        }

        double? sustainedRatio = null;
        if (options.SustainedSeconds > 0)
        {
            progress?.Report(new BenchmarkProgress($"Dauerlast ({options.SustainedSeconds} s, alle Kerne)", 100.0 * step / totalSteps));
            sustainedRatio = Sustained(threads, options.SustainedSeconds, ct,
                f => progress?.Report(new BenchmarkProgress($"Dauerlast ({options.SustainedSeconds} s, alle Kerne)", 100.0 * (step + f) / totalSteps)));
        }
        var telemetry = monitor.Stop();
        watch.Stop();

        var singleScore = GeoMean(single) * 10;
        var multiScore = GeoMean(multi) * 10;
        metrics.Insert(0, new BenchmarkMetric(MetricSingle, Math.Round(singleScore), "Punkte"));
        metrics.Insert(1, new BenchmarkMetric(MetricMulti, Math.Round(multiScore), "Punkte"));
        metrics.Add(new BenchmarkMetric("Multithreading-Skalierung", Math.Round(multiScore / Math.Max(1, singleScore), 2), "×"));
        if (sustainedRatio is { } r) metrics.Add(new BenchmarkMetric("Dauerlast: Leistung am Ende", Math.Round(r * 100), "%"));
        AddTelemetry(metrics, telemetry, cpu: true);
        metrics.Add(new BenchmarkMetric("Testdauer", Math.Round(watch.Elapsed.TotalSeconds), "s"));
        progress?.Report(new BenchmarkProgress("Fertig", 100));

        var na = new List<string>();
        if (telemetry.CpuClockAvg is null) na.Add("Takt");
        if (telemetry.CpuTempMax is null) na.Add("Temperatur");
        if (telemetry.CpuPowerMax is null) na.Add("Leistungsaufnahme");
        var note = $"{threads} logische Prozessoren · je Test {seconds:0} s · Testversion 1.2 · Thermal Throttling: {LoadMonitor.ThrottleText(telemetry.CpuThrottled)}"
                   + (na.Count > 0 ? $" · N/A: {string.Join(", ", na)} (kein Sensor)" : "");
        return Save(new BenchmarkRun(KindCpu, DateTime.Now, Math.Round(multiScore), metrics, note, telemetry));
    }

    /// <summary>Hängt verfügbare Sensorwerte als Messwerte an (fehlende Sensoren werden ausgelassen, nie geschätzt).</summary>
    internal static void AddTelemetry(List<BenchmarkMetric> metrics, LoadSummary t, bool cpu)
    {
        void Add(string name, double? v, string unit) { if (v is { } x) metrics.Add(new BenchmarkMetric(name, Math.Round(x, 1), unit)); }
        if (cpu)
        {
            Add("CPU-Auslastung (Ø)", t.CpuLoadAvg, "%");
            Add("Takt (Ø)", t.CpuClockAvg, "MHz");
            Add("Takt (max.)", t.CpuClockMax, "MHz");
            Add("Temperatur (Ø)", t.CpuTempAvg, "°C");
            Add("Temperatur (max.)", t.CpuTempMax, "°C");
            Add("Leistungsaufnahme (max.)", t.CpuPowerMax, "W");
        }
        else
        {
            Add("GPU-Auslastung (Ø)", t.GpuLoadAvg, "%");
            Add("GPU-Takt (Ø)", t.GpuClockAvg, "MHz");
            Add("GPU-Takt (max.)", t.GpuClockMax, "MHz");
            Add("GPU-Temperatur (Ø)", t.GpuTempAvg, "°C");
            Add("GPU-Temperatur (max.)", t.GpuTempMax, "°C");
            Add("Hotspot-Temperatur (max.)", t.GpuHotspotMax, "°C");
            Add("Lüfter (max.)", t.GpuFanMax, "%");
            Add("Leistungsaufnahme (max.)", t.GpuPowerMax, "W");
            if (t.VramUsedMax is { } v) metrics.Add(new BenchmarkMetric("VRAM-Verwendung (max.)", Math.Round(v / 1024.0 / 1024 / 1024, 2), "GB"));
        }
    }

    /// <summary>Dauerlast: misst den Durchsatz in Fenstern und liefert das Verhältnis letztes / erstes Fenster.</summary>
    private static double? Sustained(int threads, int seconds, CancellationToken ct, Action<double> progress)
    {
        var window = Math.Max(5, seconds / 6);
        var windows = new List<double>();
        var stop = Stopwatch.StartNew();
        while (stop.Elapsed.TotalSeconds < seconds)
        {
            ct.ThrowIfCancellationRequested();
            windows.Add(Measure(MixedUnit, threads, window, ct));
            progress(Math.Min(1, stop.Elapsed.TotalSeconds / seconds));
        }
        return windows.Count >= 2 && windows[0] > 0 ? windows[^1] / windows[0] : null;
    }

    private static long MixedUnit(WorkState s)
    {
        s.Round++;
        return (s.Round % 3) switch { 0 => MandelbrotUnit(s), 1 => PrimeSieveUnit(s), _ => AesUnit(s) };
    }

    private sealed class WorkState : IDisposable
    {
        public readonly bool[] Sieve = new bool[250_000];
        public readonly byte[] Data = RandomNumberGenerator.GetBytes(64 * 1024);
        public readonly byte[] Hash = new byte[32];
        public readonly byte[] Text = CompressibleText(64 * 1024);
        public readonly MemoryStream Packed = new(64 * 1024);
        public readonly byte[] Compressed;
        public readonly byte[] Unpacked = new byte[64 * 1024];
        public readonly AesGcm Aes = new(RandomNumberGenerator.GetBytes(32), 16);
        public readonly byte[] Nonce = new byte[12];
        public readonly byte[] Tag = new byte[16];
        public readonly byte[] Cipher = new byte[64 * 1024];
        public double Sink;
        public long Round;

        public WorkState()
        {
            using var ms = new MemoryStream();
            using (var z = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
                z.Write(Text);
            Compressed = ms.ToArray();
        }

        public void Dispose()
        {
            Aes.Dispose();
            Packed.Dispose();
        }
    }

    /// <summary>Text-ähnliche, gut komprimierbare Daten (deterministisch).</summary>
    private static byte[] CompressibleText(int size)
    {
        string[] words = ["ultimate", "windows", "control", "benchmark", "prozessor", "speicher", "daten", "leistung", "test", "system", "datei", "netzwerk"];
        var rng = new Random(42);
        var sb = new System.Text.StringBuilder(size + 32);
        while (sb.Length < size) sb.Append(words[rng.Next(words.Length)]).Append(rng.Next(10) == 0 ? ".\n" : " ");
        return System.Text.Encoding.ASCII.GetBytes(sb.ToString(0, size));
    }

    private static long CompressUnit(WorkState s)
    {
        s.Packed.SetLength(0);
        using (var z = new System.IO.Compression.DeflateStream(s.Packed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            z.Write(s.Text);
        s.Sink += s.Packed.Length;
        return s.Packed.Length;
    }

    private static long DecompressUnit(WorkState s)
    {
        using var input = new MemoryStream(s.Compressed, writable: false);
        using var z = new System.IO.Compression.DeflateStream(input, System.IO.Compression.CompressionMode.Decompress);
        var total = 0;
        int read;
        while ((read = z.Read(s.Unpacked, total, s.Unpacked.Length - total)) > 0 && total < s.Unpacked.Length) total += read;
        s.Sink += total;
        return total;
    }

    private static long AesUnit(WorkState s)
    {
        s.Nonce[0]++;
        s.Aes.Encrypt(s.Nonce, s.Data, s.Cipher, s.Tag);
        s.Sink += s.Tag[0];
        return s.Tag[1];
    }

    /// <summary>Kleiner Raytracer (32×32 Pixel, drei Kugeln, diffuse Beleuchtung) – rendering-typische Gleitkommalast.</summary>
    private static long RenderUnit(WorkState s)
    {
        const int size = 32;
        ReadOnlySpan<double> spheres = [0, 0, -3, 1, 1.5, 0.5, -4, 0.7, -1.4, -0.3, -2.5, 0.6];
        double lx = 0.577, ly = 0.577, lz = 0.577;
        long lit = 0;
        for (var py = 0; py < size; py++)
            for (var px = 0; px < size; px++)
            {
                double dx = (px - size / 2.0) / size, dy = (py - size / 2.0) / size, dz = -1;
                var inv = 1 / Math.Sqrt(dx * dx + dy * dy + dz * dz);
                dx *= inv; dy *= inv; dz *= inv;
                var best = double.MaxValue;
                var hit = -1;
                for (var i = 0; i < spheres.Length; i += 4)
                {
                    double ox = -spheres[i], oy = -spheres[i + 1], oz = -spheres[i + 2], r = spheres[i + 3];
                    var b = ox * dx + oy * dy + oz * dz;
                    var c = ox * ox + oy * oy + oz * oz - r * r;
                    var disc = b * b - c;
                    if (disc < 0) continue;
                    var t = -b - Math.Sqrt(disc);
                    if (t > 0 && t < best) { best = t; hit = i; }
                }
                if (hit < 0) continue;
                double hx = dx * best - spheres[hit], hy = dy * best - spheres[hit + 1], hz = dz * best - spheres[hit + 2];
                var n = 1 / Math.Sqrt(hx * hx + hy * hy + hz * hz);
                var shade = Math.Max(0, (hx * lx + hy * ly + hz * lz) * n);
                lit += (long)(shade * 255);
            }
        s.Sink += lit;
        return lit;
    }

    private static double Measure(Func<WorkState, long> unit, int threads, double seconds, CancellationToken ct)
    {
        var counts = new long[threads];
        var stop = Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(seconds);
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            using var st = new WorkState();
            unit(st); // Aufwärmen (JIT, Caches)
            long n = 0;
            while (stop.Elapsed < duration && !ct.IsCancellationRequested) { unit(st); n++; }
            counts[i] = n;
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal }).ToList();
        stop.Restart();
        workers.ForEach(t => t.Start());
        workers.ForEach(t => t.Join());
        ct.ThrowIfCancellationRequested();
        return counts.Sum() / stop.Elapsed.TotalSeconds;
    }

    private static long PrimeSieveUnit(WorkState s)
    {
        var a = s.Sieve;
        Array.Clear(a);
        long primes = 0;
        for (var i = 2; i < a.Length; i++)
        {
            if (a[i]) continue;
            primes++;
            for (long j = (long)i * i; j < a.Length; j += i) a[j] = true;
        }
        s.Sink += primes;
        return primes;
    }

    private static long MandelbrotUnit(WorkState s)
    {
        long total = 0;
        const int size = 48;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                double cr = -2.0 + 2.5 * x / size, ci = -1.25 + 2.5 * y / size, zr = 0, zi = 0;
                var n = 0;
                while (n < 200 && zr * zr + zi * zi < 4)
                {
                    var t = zr * zr - zi * zi + cr;
                    zi = 2 * zr * zi + ci;
                    zr = t;
                    n++;
                }
                total += n;
            }
        s.Sink += total;
        return total;
    }

    private static long ShaUnit(WorkState s)
    {
        SHA256.HashData(s.Data, s.Hash);
        s.Data[0] ^= s.Hash[0];
        return s.Hash[1];
    }

    private static double GeoMean(IReadOnlyCollection<double> values)
        => values.Count == 0 || values.Any(v => v <= 0) ? 0 : Math.Exp(values.Average(Math.Log));

    // ---------------------------------------------------------------- RAM

    public BenchmarkRun RunMemory(IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var mem = Native.NativeMethods.GetMemoryStatus();
        var avail = mem?.ullAvailPhys ?? 0;
        // Puffergröße: 256 MiB je Quelle/Ziel, bei wenig freiem RAM kleiner – niemals das System auslagern lassen.
        var bytes = avail > 2UL * 1024 * 1024 * 1024 ? 256L * 1024 * 1024 : 64L * 1024 * 1024;

        progress?.Report(new BenchmarkProgress("Kopierbandbreite", 5));
        var copyGbs = CopyBandwidth(bytes, ct);
        progress?.Report(new BenchmarkProgress("Schreibbandbreite", 30));
        var writeGbs = WriteBandwidth(bytes, ct);
        progress?.Report(new BenchmarkProgress("Lesebandbreite", 50));
        var readGbs = ReadBandwidth(bytes, ct);
        progress?.Report(new BenchmarkProgress("Latenz (Zufallszugriffe)", 70));
        var latencyNs = Latency(bytes, ct);
        progress?.Report(new BenchmarkProgress("Fertig", 100));

        var score = Math.Round(Math.Sqrt(copyGbs * readGbs) * 100 * Math.Clamp(100 / Math.Max(1, latencyNs), 0.2, 5));
        return Save(new BenchmarkRun(KindMemory, DateTime.Now, score,
        [
            new("Kopieren", Math.Round(copyGbs, 1), "GB/s"),
            new(MetricRamRead, Math.Round(readGbs, 1), "GB/s"),
            new("Schreiben", Math.Round(writeGbs, 1), "GB/s"),
            new(MetricRamLatency, Math.Round(latencyNs, 1), "ns"),
        ], $"Testgröße {Format.Bytes((ulong)bytes)} · alle Kerne für Bandbreite, 1 Kern für Latenz"));
    }

    private static double CopyBandwidth(long bytes, CancellationToken ct)
    {
        var src = GC.AllocateUninitializedArray<byte>((int)bytes);
        var dst = GC.AllocateUninitializedArray<byte>((int)bytes);
        Random.Shared.NextBytes(src.AsSpan(0, 4096));
        var threads = Math.Min(Environment.ProcessorCount, 16);
        var chunk = bytes / threads;
        // Aufwärmen: Seiten einmal anfassen (sonst misst man Page-Faults statt RAM).
        src.AsSpan().CopyTo(dst);
        var best = 0.0;
        for (var round = 0; round < 3; round++)
        {
            ct.ThrowIfCancellationRequested();
            var sw = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
                src.AsSpan((int)(i * chunk), (int)chunk).CopyTo(dst.AsSpan((int)(i * chunk), (int)chunk)));
            sw.Stop();
            // Kopieren bewegt jedes Byte zweimal (lesen + schreiben).
            best = Math.Max(best, 2.0 * chunk * threads / sw.Elapsed.TotalSeconds / 1e9);
        }
        return best;
    }

    private static double WriteBandwidth(long bytes, CancellationToken ct)
    {
        var data = new long[bytes / 8];
        var threads = Math.Min(Environment.ProcessorCount, 16);
        var chunk = data.Length / threads;
        data.AsSpan().Fill(1); // Seiten anfassen
        var best = 0.0;
        for (var round = 0; round < 3; round++)
        {
            ct.ThrowIfCancellationRequested();
            var value = round + 2L;
            var sw = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i => data.AsSpan(i * chunk, chunk).Fill(value));
            sw.Stop();
            best = Math.Max(best, (double)chunk * threads * 8 / sw.Elapsed.TotalSeconds / 1e9);
        }
        GC.KeepAlive(data);
        return best;
    }

    private static double ReadBandwidth(long bytes, CancellationToken ct)
    {
        var data = new long[bytes / 8];
        Array.Fill(data, 1L);
        var threads = Math.Min(Environment.ProcessorCount, 16);
        var chunk = data.Length / threads;
        var best = 0.0;
        long sink = 0;
        for (var round = 0; round < 3; round++)
        {
            ct.ThrowIfCancellationRequested();
            var sums = new long[threads];
            var sw = Stopwatch.StartNew();
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
            {
                var span = data.AsSpan(i * chunk, chunk);
                var v = System.Numerics.Vector<long>.Zero;
                var vecs = MemoryMarshal.Cast<long, System.Numerics.Vector<long>>(span);
                foreach (var x in vecs) v += x;
                sums[i] = System.Numerics.Vector.Sum(v);
            });
            sw.Stop();
            sink += sums.Sum();
            best = Math.Max(best, (double)chunk * threads * 8 / sw.Elapsed.TotalSeconds / 1e9);
        }
        GC.KeepAlive(sink);
        return best;
    }

    private static double Latency(long bytes, CancellationToken ct)
    {
        // Zufällige zyklische Permutation (Sattolo) → jede Leseadresse hängt von der vorherigen ab: misst echte Latenz.
        var n = (int)Math.Min(bytes / 4, 32 * 1024 * 1024);
        var next = new int[n];
        for (var i = 0; i < n; i++) next[i] = i;
        var rng = new Random(12345);
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i);
            (next[i], next[j]) = (next[j], next[i]);
        }
        ct.ThrowIfCancellationRequested();
        const int steps = 10_000_000;
        var p = 0;
        for (var i = 0; i < 100_000; i++) p = next[p];
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < steps; i++) p = next[p];
        sw.Stop();
        GC.KeepAlive(p);
        return sw.Elapsed.TotalNanoseconds / steps;
    }

    // ---------------------------------------------------------------- Datenträger

    public sealed record DiskTarget(string Root, string Label, ulong FreeBytes);

    /// <summary>Lokale, feste Laufwerke mit genug freiem Platz.</summary>
    public static IReadOnlyList<DiskTarget> GetDiskTargets()
        => DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
            .Select(d => new DiskTarget(d.RootDirectory.FullName, $"{d.Name} {d.VolumeLabel}".Trim(), (ulong)d.AvailableFreeSpace))
            .ToList();

    /// <summary>Testgröße und Dauer der Zufallszugriffe je Modus (Quick 256 MB/2 s, Normal 1 GB/4 s, Extended 4 GB/8 s).</summary>
    public static (long SizeBytes, int RandomSeconds) DiskModeParameters(DiskTestMode mode) => mode switch
    {
        DiskTestMode.Quick => (256L * 1024 * 1024, 2),
        DiskTestMode.Extended => (4096L * 1024 * 1024, 8),
        _ => (1024L * 1024 * 1024, 4),
    };

    public BenchmarkRun RunDisk(string root, DiskTestMode mode, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var (size, seconds) = DiskModeParameters(mode);
        return RunDisk(root, size, progress, ct, seconds, mode.ToString());
    }

    public BenchmarkRun RunDisk(string root, long sizeBytes, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
        => RunDisk(root, sizeBytes, progress, ct, 4, null);

    private BenchmarkRun RunDisk(string root, long sizeBytes, IProgress<BenchmarkProgress>? progress, CancellationToken ct, int randomSeconds, string? modeName)
    {
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
            throw new InvalidOperationException("Nur lokale, feste Laufwerke können getestet werden.");
        // Mindestens doppelte Testgröße + 2 GB müssen frei bleiben.
        if (drive.AvailableFreeSpace < sizeBytes * 2 + 2L * 1024 * 1024 * 1024)
            throw new InvalidOperationException($"Zu wenig freier Speicher auf {drive.Name} für einen Test mit {Format.Bytes((ulong)sizeBytes)}.");

        var dir = string.Equals(Path.GetPathRoot(Environment.SystemDirectory), drive.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppPaths.UserDataRoot, "bench")
            : Path.Combine(drive.RootDirectory.FullName, "UltimateX-Benchmark");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"bench-{Guid.NewGuid():N}.tmp");
        const int block = 4 * 1024 * 1024;
        const int small = 4096;

        // Ausgerichteter Puffer (NO_BUFFERING verlangt sektorausgerichtete Adressen): gepinntes Array + Offset.
        var raw = GC.AllocateArray<byte>(block + 4096, pinned: true);
        var addr = (long)Marshal.UnsafeAddrOfPinnedArrayElement(raw, 0);
        var offset = (int)((4096 - addr % 4096) % 4096);
        var buffer = raw.AsMemory(offset, block);
        RandomNumberGenerator.Fill(buffer.Span);

        const FileOptions noBuffering = (FileOptions)0x20000000; // FILE_FLAG_NO_BUFFERING
        var metrics = new List<BenchmarkMetric>();
        try
        {
            double seqWrite, seqRead, rndRead, rndWrite;
            using (var h = File.OpenHandle(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                       FileOptions.WriteThrough | noBuffering, preallocationSize: sizeBytes))
            {
                progress?.Report(new BenchmarkProgress("Sequentiell schreiben", 5));
                seqWrite = Sequential(h, buffer, sizeBytes, write: true, ct, p => progress?.Report(new("Sequentiell schreiben", 5 + p * 30)));
                progress?.Report(new BenchmarkProgress("Sequentiell lesen", 35));
                seqRead = Sequential(h, buffer, sizeBytes, write: false, ct, p => progress?.Report(new("Sequentiell lesen", 35 + p * 30)));
                progress?.Report(new BenchmarkProgress("4K zufällig lesen", 65));
                rndRead = Random4K(h, buffer[..small], sizeBytes, write: false, TimeSpan.FromSeconds(randomSeconds), ct);
                progress?.Report(new BenchmarkProgress("4K zufällig schreiben", 82));
                rndWrite = Random4K(h, buffer[..small], sizeBytes, write: true, TimeSpan.FromSeconds(randomSeconds), ct);
            }
            metrics.Add(new(MetricDiskSeqRead, Math.Round(seqRead / 1e6), "MB/s"));
            metrics.Add(new("Sequentiell schreiben", Math.Round(seqWrite / 1e6), "MB/s"));
            metrics.Add(new(MetricDisk4kRead, Math.Round(rndRead), "IOPS"));
            metrics.Add(new("4K zufällig schreiben (QD1)", Math.Round(rndWrite), "IOPS"));
            metrics.Add(new("4K lesen", Math.Round(rndRead * small / 1e6, 1), "MB/s"));
            metrics.Add(new("4K schreiben", Math.Round(rndWrite * small / 1e6, 1), "MB/s"));
            if (rndRead > 0) metrics.Add(new("Zugriffszeit (lesen)", Math.Round(1000.0 / rndRead, 3), "ms"));
            if (rndWrite > 0) metrics.Add(new("Zugriffszeit (schreiben)", Math.Round(1000.0 / rndWrite, 3), "ms"));
            // v1.3: Laufwerk eindeutig kennzeichnen (Performance Score wertet nur das Systemlaufwerk).
            if (UltimateX.Core.Analysis.DiskRunIdentity.Encode(drive.Name) is { } driveId)
                metrics.Add(new(UltimateX.Core.Analysis.DiskRunIdentity.MetricDriveId, driveId, ""));
            var score = Math.Round(Math.Sqrt(seqRead / 1e6 * seqWrite / 1e6) + Math.Sqrt(rndRead * rndWrite) / 10);
            progress?.Report(new BenchmarkProgress("Fertig", 100));
            return Save(new BenchmarkRun(KindDisk, DateTime.Now, score, metrics,
                $"{drive.Name} · Testdatei {Format.Bytes((ulong)sizeBytes)} · ohne Windows-Cache" + (modeName is null ? "" : $" · Modus {modeName}")));
        }
        finally
        {
            try { File.Delete(file); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLog.Warn("Benchmark", "Testdatei konnte nicht gelöscht werden", file); }
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static double Sequential(SafeFileHandle h, Memory<byte> buffer, long size, bool write, CancellationToken ct, Action<double> progress)
    {
        var sw = Stopwatch.StartNew();
        long pos = 0;
        while (pos < size)
        {
            ct.ThrowIfCancellationRequested();
            if (write) RandomAccess.Write(h, buffer.Span, pos);
            else if (RandomAccess.Read(h, buffer.Span, pos) <= 0) break;
            pos += buffer.Length;
            progress((double)pos / size);
        }
        return pos / sw.Elapsed.TotalSeconds;
    }

    private static double Random4K(SafeFileHandle h, Memory<byte> buffer, long size, bool write, TimeSpan duration, CancellationToken ct)
    {
        var blocks = size / buffer.Length;
        var rng = new Random();
        long ops = 0;
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < duration)
        {
            ct.ThrowIfCancellationRequested();
            var pos = rng.NextInt64(blocks) * buffer.Length;
            if (write) RandomAccess.Write(h, buffer.Span, pos);
            else RandomAccess.Read(h, buffer.Span, pos);
            ops++;
        }
        return ops / sw.Elapsed.TotalSeconds;
    }

    // ---------------------------------------------------------------- Verlauf

    private BenchmarkRun Save(BenchmarkRun run)
    {
        db.AddBenchmark(new BenchmarkRecord(run.Time, run.Kind, run.Score, JsonSerializer.Serialize(new StoredRun(run.Metrics, run.Note, run.Telemetry))));
        AppLog.Info("Benchmark", $"{run.Kind}-Benchmark abgeschlossen", $"Punkte {run.Score}");
        return run;
    }

    private sealed record StoredRun(IReadOnlyList<BenchmarkMetric> Metrics, string? Note, LoadSummary? Telemetry = null);

    /// <summary>Speichert einen extern erzeugten Lauf (GPU, Netzwerk, Complete, RAM-Test) im gemeinsamen Verlauf.</summary>
    public BenchmarkRun Record(BenchmarkRun run) => Save(run);

    public IReadOnlyList<BenchmarkRun> History()
    {
        var list = new List<BenchmarkRun>();
        foreach (var r in db.GetBenchmarks())
        {
            StoredRun? s = null;
            try { s = JsonSerializer.Deserialize<StoredRun>(r.Details); } catch (JsonException) { }
            list.Add(new BenchmarkRun(r.Kind, r.Time, r.Score, s?.Metrics ?? [], s?.Note, s?.Telemetry));
        }
        return list;
    }
}
