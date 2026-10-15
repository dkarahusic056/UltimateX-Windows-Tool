using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;

namespace UltimateX.Services.Benchmark;

public sealed record MemTestProgress(int Pass, int Passes, MemoryPattern Pattern, string Phase, double Percent, long ErrorCount, ulong TestedBytes);

public sealed record MemTestResult(
    MemTestMode Mode,
    DateTime Started,
    TimeSpan Duration,
    ulong TestedBytes,
    ulong PhysicalBytes,
    int PassesPlanned,
    int PassesCompleted,
    IReadOnlyList<MemoryPattern> Patterns,
    long ErrorCount,
    IReadOnlyList<MemoryError> Errors,
    bool Completed,
    CheckStatus Status,
    string Note)
{
    public double TestedPercent => PhysicalBytes == 0 ? 0 : 100.0 * TestedBytes / PhysicalBytes;
}

/// <summary>Ergebnis der Windows-Speicherdiagnose (Boot-Test) aus dem Ereignisprotokoll.</summary>
public sealed record BootMemoryTestResult(DateTime Time, bool ErrorsFound, string Message);

/// <summary>
/// RAM-STABILITÄTSTEST (v1.2 §5) – getrennt vom RAM-Benchmark.
///  • Quick / Standard / Extended laufen innerhalb von Windows: Ein Anteil des freien Arbeitsspeichers wird belegt,
///    mit Testmustern beschrieben und anschließend geprüft. Windows selbst, Treiber und andere Programme belegen den Rest –
///    dieser Teil kann innerhalb von Windows grundsätzlich nicht getestet werden. Fehleradressen sind virtuelle Adressen.
///  • Der Boot-Test nutzt die Windows-Speicherdiagnose (mdsched.exe) außerhalb des laufenden Windows, ähnlich MemTest86;
///    das Ergebnis wird nach dem Neustart aus dem Ereignisprotokoll gelesen.
/// </summary>
public sealed class MemoryTestService
{
    private const int ChunkLongs = 32 * 1024 * 1024; // 256 MB je Block
    private const int MaxReportedErrors = 200;

    public MemTestResult Run(MemTestMode mode, IProgress<MemTestProgress>? progress, CancellationToken ct)
    {
        var plan = MemTestPlan.For(mode);
        var status = Native.NativeMethods.GetMemoryStatus();
        var physical = status?.ullTotalPhys ?? 0;
        var avail = status?.ullAvailPhys ?? 0;
        // Mindestens 1,5 GB für Windows frei lassen; nie mehr als den Planwert des freien Speichers belegen.
        var target = (ulong)(avail * plan.MemoryFraction);
        var reserve = 1536UL * 1024 * 1024;
        if (avail > reserve) target = Math.Min(target, avail - reserve);
        else target = 0;

        var started = DateTime.Now;
        var sw = Stopwatch.StartNew();
        var chunks = new List<ulong[]>();
        var errors = new List<MemoryError>();
        long errorCount = 0;
        var passesDone = 0;
        var completed = false;
        ulong tested = 0;
        try
        {
            // Speicher in 256-MB-Blöcken belegen (fixiert, damit Adressen stabil bleiben).
            while (tested + (ulong)ChunkLongs * 8 <= target)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    chunks.Add(GC.AllocateUninitializedArray<ulong>(ChunkLongs, pinned: true));
                    tested += (ulong)ChunkLongs * 8;
                }
                catch (OutOfMemoryException)
                {
                    break;
                }
                progress?.Report(new MemTestProgress(0, plan.Passes, plan.Patterns[0], "Speicher wird belegt", 0, 0, tested));
            }
            if (chunks.Count == 0) throw new InvalidOperationException("Es ist zu wenig freier Arbeitsspeicher für den Test vorhanden.");

            var bases = chunks.Select(c => (ulong)Marshal.UnsafeAddrOfPinnedArrayElement(c, 0).ToInt64()).ToArray();
            var totalSteps = plan.Passes * plan.Patterns.Count * 2;
            var step = 0;
            var gate = new object();
            var threads = Math.Max(1, Math.Min(Environment.ProcessorCount, chunks.Count));
            for (var pass = 1; pass <= plan.Passes; pass++)
            {
                var seed = MemoryPatterns.SplitMix((ulong)pass * 7919UL + (ulong)started.Ticks);
                foreach (var pattern in plan.Patterns)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new MemTestProgress(pass, plan.Passes, pattern, "Schreiben", 100.0 * step++ / totalSteps, errorCount, tested));
                    Parallel.For(0, chunks.Count, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct },
                        i => MemoryPatterns.Fill(chunks[i], pattern, (long)i * ChunkLongs, seed));

                    progress?.Report(new MemTestProgress(pass, plan.Passes, pattern, "Prüfen", 100.0 * step++ / totalSteps, errorCount, tested));
                    var p = pass;
                    Parallel.For(0, chunks.Count, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct }, i =>
                    {
                        var local = new List<MemoryError>();
                        var found = MemoryPatterns.Verify(chunks[i], pattern, (long)i * ChunkLongs, seed, p,
                            idx => bases[i] + (ulong)idx * 8, local, MaxReportedErrors);
                        if (found == 0) return;
                        lock (gate)
                        {
                            errorCount += found;
                            foreach (var e in local)
                                if (errors.Count < MaxReportedErrors) errors.Add(e);
                        }
                    });
                }
                passesDone = pass;
            }
            completed = true;
        }
        catch (OperationCanceledException)
        {
            // Abbruch durch den Benutzer – Teilergebnis wird zurückgegeben.
        }
        finally
        {
            chunks.Clear();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        sw.Stop();

        var verdict = MemoryPatterns.Evaluate(errorCount, errors, completed);
        var note = $"{Format.Bytes(tested)} von {Format.Bytes(physical)} getestet ({(physical == 0 ? 0 : 100.0 * tested / physical):0} %). "
                   + "Speicher, den Windows und laufende Programme belegen, kann innerhalb von Windows nicht geprüft werden – dafür den Boot-Test verwenden.";
        AppLog.Info("MemTest", $"RAM-Test {mode} beendet", $"Fehler={errorCount} Durchläufe={passesDone}/{plan.Passes} Getestet={tested}");
        return new MemTestResult(mode, started, sw.Elapsed, tested, physical, plan.Passes, passesDone, plan.Patterns, errorCount, errors, completed, verdict, note);
    }

    /// <summary>Letzte Ergebnisse der Windows-Speicherdiagnose (Ereignis 1201 = keine Fehler, 1202 = Fehler gefunden).</summary>
    public static IReadOnlyList<BootMemoryTestResult> GetBootTestResults(int max = 5)
    {
        var list = new List<BootMemoryTestResult>();
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results'] and (EventID=1201 or EventID=1202 or EventID=1101 or EventID=1102)]]")
            {
                ReverseDirection = true,
            };
            using var reader = new EventLogReader(query);
            for (var ev = reader.ReadEvent(); ev is not null && list.Count < max; ev = reader.ReadEvent())
            {
                using (ev)
                {
                    string? message = null;
                    try { message = ev.FormatDescription(); } catch (EventLogException) { }
                    var errorsFound = ev.Id is 1202 or 1102;
                    list.Add(new BootMemoryTestResult(ev.TimeCreated ?? DateTime.MinValue, errorsFound,
                        message ?? (errorsFound ? "Die Windows-Speicherdiagnose hat Hardwarefehler gefunden." : "Die Windows-Speicherdiagnose hat keine Fehler gefunden.")));
                }
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            AppLog.Warn("MemTest", "Ergebnisse der Speicherdiagnose nicht lesbar", ex.Message);
        }
        return list;
    }
}
