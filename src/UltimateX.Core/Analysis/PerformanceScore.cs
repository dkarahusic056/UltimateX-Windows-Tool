namespace UltimateX.Core.Analysis;

/// <summary>
/// Referenzwerte der Punkteskalen (100 Punkte ≈ aktuelle Spitzenklasse für Endanwender, Stand 2026).
/// Die Werte sind Richtwerte (ESTIMATED) und bewusst zentral abgelegt; sie gelten für die Messmethoden von ULTIMATE X,
/// nicht für fremde Benchmarks. Die Einstufung wird in der Oberfläche als Schätzung gekennzeichnet.
///
/// v1.3 – Neukalibrierung: v1.2 nutzte CPU-Referenzen (2 600 / 40 000), die nicht zur Punkteskala des eigenen CPU-Tests
/// passten (ein i9-13900K erreicht dort ca. 77 000 / 1 565 000 Punkte) – die CPU stand dadurch immer bei 100/100.
/// Kalibriert wurde an echten Messwerten eines i9-13900K / RTX 4080 / 64 GB DDR5-6000 (Dual Channel):
/// CPU ≈ 84, GPU ≈ 81, RAM ≈ 67, NVMe Gen4 (≈ 7 000 MB/s, ≈ 16 000 IOPS) ≈ 90.
/// Die Messgrößen selbst sind unverändert – ältere Läufe im Verlauf bleiben gültig, nur ihre Bewertung ändert sich.
/// Werte oberhalb der Referenz werden bei 100 gekappt (Sättigung nur oberhalb der Spitzenklasse).
/// </summary>
public static class ScoreReferences
{
    /// <summary>
    /// CPU Einkern (Punkte des ULTIMATE-X-CPU-Tests, geometrisches Mittel aus 7 Lasten × 10). ESTIMATED:
    /// Spitzenklasse 2026 (z. B. Ryzen 9 9950X3D / Core Ultra 9 285K und Nachfolger) liegt einkernig ca. 25 % über dem
    /// i9-13900K (77 079 Punkte) → ≈ 96 000. Skala: Referenz / 3 = 0 Punkte (eine CPU mit einem Drittel der Einkernleistung
    /// der Spitzenklasse ist für aktuelle Spiele/Software klar unterdimensioniert).
    /// </summary>
    public const double CpuSingle = 96_000;
    public const double CpuSingleRange = 3;

    /// <summary>
    /// CPU alle Kerne. ESTIMATED: Spitzenklasse 2026 (16+ Kerne / 32 Threads) ca. 28 % über dem i9-13900K (1 565 122 Punkte)
    /// → ≈ 2 000 000. Skala: Referenz / 8 = 0 Punkte (z. B. ältere 4-Kerner ohne SMT).
    /// </summary>
    public const double CpuMulti = 2_000_000;
    public const double CpuMultiRange = 8;

    /// <summary>
    /// GPU: gemessene FP32-Rechenleistung (GFLOPS). ESTIMATED: RTX 5090 ≈ 105 TFLOPS laut Hersteller; der eigene
    /// Direct3D-Compute-Test erreicht auf aktuellen GeForce-Karten ungefähr den Herstellerwert (RTX 4080: 49 328 gemessen
    /// bei 48,7 TFLOPS Nennwert) → 105 000. Skala: Referenz / 100 = 0 Punkte.
    /// </summary>
    public const double GpuGflops = 105_000;
    public const double GpuGflopsRange = 100;

    /// <summary>
    /// GPU: gemessene Speicherbandbreite (GB/s). ESTIMATED: RTX 5090 1 792 GB/s Nennwert; der Test erreicht ca. 85 % davon
    /// (RTX 4080: 614 von 717 GB/s) → ≈ 1 550. Skala: Referenz / 30 = 0 Punkte.
    /// </summary>
    public const double GpuBandwidth = 1_550;
    public const double GpuBandwidthRange = 30;

    /// <summary>
    /// RAM: Lesebandbreite (GB/s) dieses Tests. ESTIMATED: Der Test erreicht ca. 55–60 % der theoretischen Bandbreite
    /// (DDR5-6000 Dual Channel: 53,8 von 96 GB/s); Spitzenklasse 2026 (DDR5-8000+ Dual Channel / CUDIMM) → ≈ 80 GB/s.
    /// Skala: Referenz / 4 = 0 Punkte (≈ 20 GB/s, z. B. Single Channel DDR4).
    /// </summary>
    public const double RamReadGbs = 80;
    public const double RamReadRange = 4;

    /// <summary>
    /// RAM: Zugriffslatenz (ns) dieses Tests (Zeigerkette über 128 MB inkl. TLB-Fehlgriffe – liegt typisch 15–30 ns über
    /// AIDA64-Werten). ESTIMATED: gut abgestimmte Spitzensysteme ≈ 70 ns. 106 ns (DDR5-6000 auf Raptor Lake) ist ehrlich
    /// gesagt hoch. Skala: Referenz × 3 = 0 Punkte (≈ 210 ns).
    /// </summary>
    public const double RamLatencyNs = 70;
    public const double RamLatencyRange = 3;

    /// <summary>
    /// Datenträger sequentiell lesen (MB/s, 4-MB-Blöcke, QD1, ohne Windows-Cache). ESTIMATED: PCIe-5.0-SSDs (12–14,5 GB/s
    /// Nennwert) erreichen mit dieser Methode ca. 10–11 GB/s → 10 500; eine Gen4-SSD mit ≈ 7 000 MB/s erhält ≈ 90.
    /// Skala: Referenz / 70 = 0 Punkte (≈ 150 MB/s, typische Festplatte).
    /// </summary>
    public const double DiskSeqReadMbs = 10_500;
    public const double DiskSeqReadRange = 70;

    /// <summary>
    /// Datenträger 4K zufällig lesen, QD1 (IOPS). ESTIMATED: beste NAND-SSDs ≈ 40 µs Zugriffszeit → ≈ 25 000 IOPS;
    /// typische Gen4-SSDs 14 000–18 000, SATA-SSDs 8 000–10 000. Skala: Referenz / 200 = 0 Punkte (Festplatte).
    /// </summary>
    public const double Disk4kReadIops = 25_000;
    public const double Disk4kReadRange = 200;

    /// <summary>RAM-Wertung: Anteil der Bandbreite (Rest: Latenz). Latenz zählt für Spiele spürbar mit.</summary>
    public const double RamBandwidthWeight = 0.6;
}

/// <summary>Teilwertungen (0–100) – null = nicht gemessen bzw. nicht verfügbar.</summary>
public sealed record ComponentScores(int? Cpu, int? Gpu, int? Ram, int? Storage, int? Network, int? Cooling)
{
    public static ComponentScores Empty { get; } = new(null, null, null, null, null, null);
}

public sealed record PerformanceResult(ComponentScores Scores, int? Total, PerformanceTier? Tier)
{
    public string TierText => Tier?.Label() ?? "N/A";
}

/// <summary>Berechnet aus Messwerten die Teilwertungen und den ULTIMATE X PERFORMANCE SCORE.</summary>
public static class PerformanceScore
{
    // Gewichtung der Teilwertungen im Gesamtwert.
    public const double WeightCpu = 25, WeightGpu = 30, WeightRam = 15, WeightStorage = 15, WeightNetwork = 5, WeightCooling = 10;

    public static int? CpuScore(double? singlePoints, double? multiPoints)
    {
        if (singlePoints is not > 0 || multiPoints is not > 0) return null;
        // Spiele und Alltag hängen stärker an der Einkernleistung; Rendering/Kompression an allen Kernen.
        var s = ScoreMath.LogScale(singlePoints.Value, ScoreReferences.CpuSingle, ScoreReferences.CpuSingleRange);
        var m = ScoreMath.LogScale(multiPoints.Value, ScoreReferences.CpuMulti, ScoreReferences.CpuMultiRange);
        return ScoreMath.Clamp(0.5 * s + 0.5 * m);
    }

    public static int? GpuScore(double? gflops, double? bandwidthGbs)
    {
        if (gflops is not > 0) return null;
        var c = ScoreMath.LogScale(gflops.Value, ScoreReferences.GpuGflops, ScoreReferences.GpuGflopsRange);
        if (bandwidthGbs is not > 0) return c;
        var b = ScoreMath.LogScale(bandwidthGbs.Value, ScoreReferences.GpuBandwidth, ScoreReferences.GpuBandwidthRange);
        return ScoreMath.Clamp(0.75 * c + 0.25 * b);
    }

    public static int? RamScore(double? readGbs, double? latencyNs)
    {
        if (readGbs is not > 0) return null;
        var bw = ScoreMath.LogScale(readGbs.Value, ScoreReferences.RamReadGbs, ScoreReferences.RamReadRange);
        if (latencyNs is not > 0) return bw;
        var lat = ScoreMath.LogScaleInverse(latencyNs.Value, ScoreReferences.RamLatencyNs, ScoreReferences.RamLatencyRange);
        const double w = ScoreReferences.RamBandwidthWeight;
        return ScoreMath.Clamp(w * bw + (1 - w) * lat);
    }

    public static int? StorageScore(double? seqReadMbs, double? random4kReadIops)
    {
        if (seqReadMbs is not > 0) return null;
        var seq = ScoreMath.LogScale(seqReadMbs.Value, ScoreReferences.DiskSeqReadMbs, ScoreReferences.DiskSeqReadRange);
        if (random4kReadIops is not > 0) return seq;
        var rnd = ScoreMath.LogScale(random4kReadIops.Value, ScoreReferences.Disk4kReadIops, ScoreReferences.Disk4kReadRange);
        return ScoreMath.Clamp(0.5 * seq + 0.5 * rnd);
    }

    /// <summary>
    /// Kühlung aus den Höchsttemperaturen unter Last. CPU: ≤ 70 °C = 100, ≥ 100 °C = 0; GPU: ≤ 70 °C = 100, ≥ 95 °C = 0.
    /// Erkannte Drosselung (Thermal Throttling) begrenzt die Wertung auf 50.
    /// </summary>
    public static int? CoolingScore(double? cpuMaxTempC, double? gpuMaxTempC, bool throttled)
    {
        int? cpu = cpuMaxTempC is > 0 ? ScoreMath.Linear(cpuMaxTempC.Value, 100, 70) : null;
        int? gpu = gpuMaxTempC is > 0 ? ScoreMath.Linear(gpuMaxTempC.Value, 95, 70) : null;
        var avg = ScoreMath.WeightedAverage([(cpu, 1), (gpu, 1)]);
        if (avg is null) return null;
        return throttled ? Math.Min(avg.Value, 50) : avg;
    }

    public static PerformanceResult Combine(ComponentScores s)
    {
        var total = ScoreMath.WeightedAverage(
        [
            (s.Cpu, WeightCpu), (s.Gpu, WeightGpu), (s.Ram, WeightRam),
            (s.Storage, WeightStorage), (s.Network, WeightNetwork), (s.Cooling, WeightCooling),
        ]);
        // Ohne CPU- und GPU-Wertung wäre die Einstufung nicht aussagekräftig.
        PerformanceTier? tier = total is { } t && s.Cpu is not null && s.Gpu is not null ? ScoreMath.TierOf(t) : null;
        return new PerformanceResult(s, total, tier);
    }
}
