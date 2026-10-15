using UltimateX.Services.Benchmark;
using UltimateX.Services.Data;
using UltimateX.Services.Diagnostics;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Network;
using UltimateX.Services.Security;
using UltimateX.Services.Sensors;

namespace UltimateX.Services.Reports.Pdf;

/// <summary>Abschnitte des PC-Berichts (in der Vorschau einzeln abwählbar).</summary>
public sealed record PcReportOptions
{
    public bool Hardware { get; init; } = true;
    public bool Sensors { get; init; } = true;
    public bool Network { get; init; } = true;
    public bool Security { get; init; } = true;
    public bool Drivers { get; init; } = true;
    public bool WindowsHealth { get; init; } = true;
    public bool ComponentTests { get; init; } = true;
    public bool Benchmarks { get; init; } = true;
    public bool CrashAnalysis { get; init; } = true;

    /// <summary>v1.2: Bewertungen (Performance Score, Gaming Score, RAM-Test, Laufwerke, Netzwerk) und Empfehlungen.</summary>
    public bool Analysis { get; init; } = true;

    /// <summary>
    /// Datenschutzmodus (Standard: AN): maskiert öffentliche/lokale IP-Adressen, MAC-Adressen, Benutzer- und Computername,
    /// Seriennummern sowie Gateway/DNS. Abschalten erfordert in der App eine Windows-Hello-Bestätigung.
    /// </summary>
    public bool PrivacyMode { get; init; } = true;
}

/// <summary>Alle Daten des Berichts – werden vor dem Layout vollständig erhoben (keine Systemzugriffe im Layout).</summary>
public sealed record PcReportData
{
    public required string ReportId { get; init; }
    public required DateTime Created { get; init; }
    public required string AppVersion { get; init; }
    public string? UserName { get; init; }
    public HardwareSnapshot? Hardware { get; init; }
    public HealthScoreResult? Score { get; init; }
    public IReadOnlyList<UnifiedSensor> Sensors { get; init; } = [];
    public IReadOnlyList<SensorSourceStatus> SensorSources { get; init; } = [];
    public IReadOnlyList<double> CpuHistory { get; init; } = [];
    public IReadOnlyList<double> RamHistory { get; init; } = [];
    public IReadOnlyList<double> GpuHistory { get; init; } = [];
    public IReadOnlyList<AdapterInfo> Adapters { get; init; } = [];
    public IReadOnlyList<SpeedtestRecord> Speedtests { get; init; } = [];
    public IReadOnlyList<SecurityItem> Security { get; init; } = [];
    public IReadOnlyList<PnpDevice> ProblemDevices { get; init; } = [];
    public IReadOnlyList<ServiceStatus> Services { get; init; } = [];
    public RebootStatus? Reboot { get; init; }
    public CrashSummary? Crashes { get; init; }
    public ComponentTestRun? ComponentTests { get; init; }
    public IReadOnlyList<BenchmarkRun> Benchmarks { get; init; } = [];
    public CrashReport? CrashReport { get; init; }
    public IReadOnlyList<SmartDisk> Smart { get; init; } = [];
    public ReportAnalysis? Analysis { get; init; }
}

/// <summary>v1.2: Bewertungen und Empfehlungen für den Bericht (keine Passwörter oder sonstigen Geheimnisse).</summary>
public sealed record ReportAnalysis(
    Core.Analysis.PerformanceResult Performance,
    int? GamingScore,
    int? NetworkScore,
    string? MemoryTest,
    Core.Common.DataState MemoryTestState,
    IReadOnlyList<(string Drive, string Verdict, Core.Common.DataState State)> Drives,
    IReadOnlyList<Core.Analysis.Finding> Problems,
    IReadOnlyList<string> Recommendations);
