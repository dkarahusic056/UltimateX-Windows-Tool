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
public sealed record BenchmarkRun(string Kind, DateTime Time, double Score, IReadOnlyList<BenchmarkMetric> Metrics, string? Note = null);

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

    // ---------------------------------------------------------------- CPU

    public BenchmarkRun RunCpu(IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        var seconds = 2.0;
        var threads = Environment.ProcessorCount;
        var tests = new (string Name, Func<WorkState, long> Unit)[]
        {
            ("Ganzzahl (Primzahlsieb)", PrimeSieveUnit),
            ("Gleitkomma (Mandelbrot)", MandelbrotUnit),
            ("Krypto (SHA-256)", ShaUnit),
        };

        var metrics = new List<BenchmarkMetric>();
        var single = new List<double>();
        var multi = new List<double>();
        var step = 0;
        foreach (var (name, unit) in tests)
        {
            progress?.Report(new BenchmarkProgress($"{name} – 1 Kern", 100.0 * step++ / (tests.Length * 2)));
            var s = Measure(unit, 1, seconds, ct);
            progress?.Report(new BenchmarkProgress($"{name} – {threads} Threads", 100.0 * step++ / (tests.Length * 2)));
            var m = Measure(unit, threads, seconds, ct);
            single.Add(s);
            multi.Add(m);
            metrics.Add(new BenchmarkMetric($"{name}, 1 Kern", Math.Round(s, 1), "Einheiten/s"));
            metrics.Add(new BenchmarkMetric($"{name}, alle Kerne", Math.Round(m, 1), "Einheiten/s"));
        }

        var singleScore = GeoMean(single) * 10;
        var multiScore = GeoMean(multi) * 10;
        metrics.Insert(0, new BenchmarkMetric("Single-Core-Punkte", Math.Round(singleScore), "Punkte"));
        metrics.Insert(1, new BenchmarkMetric("Multi-Core-Punkte", Math.Round(multiScore), "Punkte"));
        metrics.Add(new BenchmarkMetric("Skalierung", Math.Round(multiScore / Math.Max(1, singleScore), 2), "×"));
        progress?.Report(new BenchmarkProgress("Fertig", 100));
        return Save(new BenchmarkRun(KindCpu, DateTime.Now, Math.Round(multiScore), metrics,
            $"{threads} logische Prozessoren · je Test {seconds:0} s"));
    }

    private sealed class WorkState
    {
        public readonly bool[] Sieve = new bool[250_000];
        public readonly byte[] Data = RandomNumberGenerator.GetBytes(64 * 1024);
        public readonly byte[] Hash = new byte[32];
        public double Sink;
    }

    private static double Measure(Func<WorkState, long> unit, int threads, double seconds, CancellationToken ct)
    {
        var counts = new long[threads];
        var stop = Stopwatch.StartNew();
        var duration = TimeSpan.FromSeconds(seconds);
        var workers = Enumerable.Range(0, threads).Select(i => new Thread(() =>
        {
            var st = new WorkState();
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
        progress?.Report(new BenchmarkProgress("Lesebandbreite", 40));
        var readGbs = ReadBandwidth(bytes, ct);
        progress?.Report(new BenchmarkProgress("Latenz (Zufallszugriffe)", 70));
        var latencyNs = Latency(bytes, ct);
        progress?.Report(new BenchmarkProgress("Fertig", 100));

        var score = Math.Round(Math.Sqrt(copyGbs * readGbs) * 100 * Math.Clamp(100 / Math.Max(1, latencyNs), 0.2, 5));
        return Save(new BenchmarkRun(KindMemory, DateTime.Now, score,
        [
            new("Kopieren", Math.Round(copyGbs, 1), "GB/s"),
            new("Lesen", Math.Round(readGbs, 1), "GB/s"),
            new("Latenz", Math.Round(latencyNs, 1), "ns"),
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

    public BenchmarkRun RunDisk(string root, long sizeBytes, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
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
                rndRead = Random4K(h, buffer[..small], sizeBytes, write: false, TimeSpan.FromSeconds(4), ct);
                progress?.Report(new BenchmarkProgress("4K zufällig schreiben", 82));
                rndWrite = Random4K(h, buffer[..small], sizeBytes, write: true, TimeSpan.FromSeconds(4), ct);
            }
            metrics.Add(new("Sequentiell lesen", Math.Round(seqRead / 1e6), "MB/s"));
            metrics.Add(new("Sequentiell schreiben", Math.Round(seqWrite / 1e6), "MB/s"));
            metrics.Add(new("4K zufällig lesen (QD1)", Math.Round(rndRead), "IOPS"));
            metrics.Add(new("4K zufällig schreiben (QD1)", Math.Round(rndWrite), "IOPS"));
            metrics.Add(new("4K lesen", Math.Round(rndRead * small / 1e6, 1), "MB/s"));
            var score = Math.Round(Math.Sqrt(seqRead / 1e6 * seqWrite / 1e6) + Math.Sqrt(rndRead * rndWrite) / 10);
            progress?.Report(new BenchmarkProgress("Fertig", 100));
            return Save(new BenchmarkRun(KindDisk, DateTime.Now, score, metrics,
                $"{drive.Name} · Testdatei {Format.Bytes((ulong)sizeBytes)} · ohne Windows-Cache"));
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
        db.AddBenchmark(new BenchmarkRecord(run.Time, run.Kind, run.Score, JsonSerializer.Serialize(new StoredRun(run.Metrics, run.Note))));
        AppLog.Info("Benchmark", $"{run.Kind}-Benchmark abgeschlossen", $"Punkte {run.Score}");
        return run;
    }

    private sealed record StoredRun(IReadOnlyList<BenchmarkMetric> Metrics, string? Note);

    public IReadOnlyList<BenchmarkRun> History()
    {
        var list = new List<BenchmarkRun>();
        foreach (var r in db.GetBenchmarks())
        {
            StoredRun? s = null;
            try { s = JsonSerializer.Deserialize<StoredRun>(r.Details); } catch (JsonException) { }
            list.Add(new BenchmarkRun(r.Kind, r.Time, r.Score, s?.Metrics ?? [], s?.Note));
        }
        return list;
    }
}
