using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Hardware;
using UltimateX.Services.Sensors;

namespace UltimateX.App.Infrastructure;

/// <summary>Gesammelte Systemdaten für Gaming Check, Bottleneck-Analyse, Upgrade-Berater, „Can my PC run it?“ und Empfehlungen.</summary>
public sealed record CollectedFacts(
    SystemFacts Facts,
    HardwareSnapshot Hardware,
    IReadOnlyList<RamModuleData> RamModules,
    RamSpeedResult RamSpeed,
    ChannelEstimate Channels,
    PowerPlanInfo? PowerPlan,
    PcieLink? Pcie,
    GpuAdapterInfo? Adapter,
    IReadOnlyList<BenchmarkRun> History)
{
    public string? PrimaryGpuName => Facts.GpuName;

    /// <summary>v1.3: Windows-Systemlaufwerk mit Bustyp und (wenn eindeutig) PCIe-Generation – Grundlage der SSD-Wertung.</summary>
    public SystemDiskInfo? SystemDisk { get; init; }

    /// <summary>v1.3: Höchste gemessene NVMe-Temperatur seit Programmstart (null = kein Sensor).</summary>
    public double? StorageMaxTempC { get; init; }

    public PcProfile ToPcProfile()
    {
        var sysRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var vol = Hardware.Volumes.FirstOrDefault(v => sysRoot.StartsWith(v.Letter, StringComparison.OrdinalIgnoreCase));
        return new PcProfile(
            Facts.CpuName ?? "",
            Facts.CpuCores,
            Facts.GpuName ?? "",
            Facts.VramGb,
            Facts.RamGb,
            vol?.FreeBytes is { } f ? f / 1024.0 / 1024 / 1024 : null,
            Facts.SystemDriveIsSsd,
            Adapter?.DirectXVersion,
            int.TryParse(Hardware.Os.Build, out var b) ? b : null,
            Environment.Is64BitOperatingSystem);
    }
}

/// <summary>Sammelt <see cref="SystemFacts"/> aus Hardware-Erkennung, Sensoren, Benchmark-Verlauf und Windows-Einstellungen.</summary>
public static class FactsCollector
{
    /// <summary>Verbindet den Benchmark-Telemetrie-Sampler mit Live-Monitor und Sensor-Manager (einmal beim Start).</summary>
    public static void InstallLoadSampler()
    {
        LoadMonitor.Sampler = () =>
        {
            var s = MonitorHub.Latest;
            var sm = AppServices.Monitor.SensorManager;
            double? Best(UnifiedSensorId id) => sm.Best(id)?.Current;
            return new LoadSample(
                DateTime.Now,
                CpuLoad: s?.CpuPercent ?? Best(UnifiedSensorId.CpuLoad),
                CpuClockMHz: Best(UnifiedSensorId.CpuClock),
                CpuTempC: Best(UnifiedSensorId.CpuPackageTemp) ?? Best(UnifiedSensorId.CpuCoreTemp) ?? s?.CpuTempC,
                CpuPowerW: Best(UnifiedSensorId.CpuPower),
                GpuLoad: s?.GpuPercent ?? Best(UnifiedSensorId.GpuLoad),
                GpuClockMHz: Best(UnifiedSensorId.GpuClock),
                GpuTempC: Best(UnifiedSensorId.GpuCoreTemp) ?? s?.GpuTempC,
                GpuHotspotC: Best(UnifiedSensorId.GpuHotspotTemp),
                GpuFanPercent: s?.GpuFanPercent,
                GpuPowerW: Best(UnifiedSensorId.GpuPower),
                VramUsedBytes: s?.VramUsedBytes);
        };
    }

    /// <summary>Erhebt alle Fakten (läuft im Hintergrund; dauert einige Sekunden).</summary>
    public static async Task<CollectedFacts> CollectAsync(bool includeProcesses = true)
    {
        return await Task.Run(() =>
        {
            var hw = AppServices.LastHardware ?? AppServices.Hardware.Collect();
            AppServices.LastHardware = hw;
            var modules = AppServices.SystemInfo.GetRamModules();
            var speed = MemoryAnalysis.CheckSpeed(modules);
            var channels = MemoryAnalysis.EstimateChannels(modules);
            var plan = AppServices.SystemInfo.GetActivePowerPlan();
            var adapter = GpuBenchmarkService.DetectAdapter();

            // Primäre GPU: die mit dem meisten VRAM (entspricht dem Adapter des GPU-Benchmarks).
            var gpu = hw.Gpus.OrderByDescending(g => g.VramBytes ?? 0).FirstOrDefault();
            var gpuName = adapter?.Name ?? gpu?.Name;
            var pcie = AppServices.SystemInfo.GetGpuPcieLink(gpu?.Name);
            var history = AppServices.Benchmarks.History();
            var scores = CompleteBenchmarkService.ScoresFromHistory(history);
            var lastCpu = history.Where(r => r.Kind == BenchmarkService.KindCpu).OrderByDescending(r => r.Time).FirstOrDefault();
            var lastGpu = history.Where(r => r.Kind == BenchmarkService.KindGpu).OrderByDescending(r => r.Time).FirstOrDefault();
            var heavy = includeProcesses ? AppServices.SystemInfo.GetHeavyProcesses() : [];
            var latest = MonitorHub.Latest;

            ulong? vramBytes = adapter?.DedicatedVideoMemory is > 0 ? adapter.DedicatedVideoMemory : gpu?.VramBytes;
            int? vramGb = vramBytes is { } vb && vb > 0 ? (int)Math.Round(vb / 1024.0 / 1024 / 1024) : null;
            double? ramGb = hw.Memory.TotalBytes is { } t ? t / 1024.0 / 1024 / 1024 : null;

            var driverAge = gpu?.DriverDate is { } dd ? (int?)(DateTime.Now - dd).TotalDays : null;
            var pingSample = AppServices.History.GetSpeedtests(30).LastOrDefault()?.PingMs;

            var facts = new SystemFacts
            {
                Scores = scores,
                CpuName = hw.Cpu?.Name,
                CpuCores = hw.Cpu?.Cores,
                GpuName = gpuName,
                RamGb = ramGb,
                RamUsedPercent = latest?.RamPercent,
                VramGb = vramGb,
                VramUsedPercent = latest?.VramUsedBytes is { } vu && vramBytes is > 0 ? 100.0 * vu / vramBytes.Value : null,
                RamBelowRatedSpeed = speed.Status switch { CheckStatus.Warning => true, CheckStatus.Pass => false, _ => null },
                RamSingleChannel = channels.Mode switch { ChannelMode.Single => true, ChannelMode.Unknown => null, _ => false },
                SystemDriveIsSsd = AppServices.SystemInfo.SystemDriveIsSsd(),
                CpuMaxTempC = lastCpu?.Telemetry?.CpuTempMax ?? AppServices.Monitor.SensorManager.Best(UnifiedSensorId.CpuPackageTemp)?.Max,
                GpuMaxTempC = lastGpu?.Telemetry?.GpuTempMax ?? AppServices.Monitor.SensorManager.Best(UnifiedSensorId.GpuCoreTemp)?.Max,
                CpuThrottled = lastCpu?.Telemetry?.CpuThrottled,
                GpuThrottled = lastGpu?.Telemetry?.GpuThrottled,
                PowerPlan = plan?.Name,
                PowerPlanIsSaver = plan?.IsSaver,
                HeavyBackgroundProcesses = heavy,
                PcieCurrentWidth = pcie?.CurrentWidth,
                PcieMaxWidth = pcie?.MaxWidth,
                GpuDriverAgeDays = driverAge,
                GameModeOn = AppServices.SystemInfo.GameModeEnabled(),
                HardwareSchedulingOn = AppServices.SystemInfo.HardwareGpuSchedulingEnabled(),
                ResizableBarOn = null, // ohne Herstellerwerkzeug nicht zuverlässig lesbar → N/A
                PingMs = pingSample,
                SecureBootOn = hw.Board.SecureBootState switch { DataState.Ok => true, DataState.NotAvailable or DataState.RequiresAdmin => null, _ => false },
                TpmReady = hw.Board.TpmState switch { DataState.Ok => true, DataState.NotAvailable or DataState.RequiresAdmin => null, _ => false },
            };
            SystemDiskInfo? systemDisk = null;
            try
            {
                systemDisk = SystemDiskProbe.Detect();
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                UltimateX.Core.Logging.AppLog.Warn("Facts", "Systemlaufwerk nicht ermittelbar", ex.Message);
            }
            return new CollectedFacts(facts, hw, modules, speed, channels, plan, pcie, adapter, history)
            {
                SystemDisk = systemDisk,
                StorageMaxTempC = AppServices.Monitor.SensorManager.Best(UnifiedSensorId.NvmeTemp)?.Max,
            };
        });
    }
}
