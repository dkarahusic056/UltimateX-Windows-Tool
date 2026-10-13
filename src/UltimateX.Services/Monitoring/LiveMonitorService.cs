using System.Diagnostics;
using System.Net.NetworkInformation;
using UltimateX.Services.Native;
using UltimateX.Services.Sensors;

namespace UltimateX.Services.Monitoring;

public sealed record MonitorSample(
    DateTime Time,
    double? CpuPercent,
    double? RamPercent,
    ulong? RamUsedBytes,
    double? DiskActivePercent,
    double DownloadBitsPerSecond,
    double UploadBitsPerSecond,
    double? GpuPercent = null,
    ulong? VramUsedBytes = null,
    int? GpuTempC = null,
    int? GpuFanPercent = null,
    int? CpuTempC = null,
    string? CpuTempSource = null,
    string? GpuTempSource = null,
    int? AcpiTempC = null);

/// <summary>
/// Live-Werte mit minimalem Overhead (§19, §89): ein Sample pro Sekunde,
/// nur günstige Win32-Aufrufe (GetSystemTimes, GlobalMemoryStatusEx, Interface-Zähler).
///
/// GPU-Werte kommen aus <see cref="GpuService"/> (Leistungsindikatoren + NVML), Temperaturen aus dem
/// <see cref="SensorHub"/> (Grafikkernel, ACPI, HWiNFO/LibreHardwareMonitor falls vorhanden).
/// ULTIMATE X lädt selbst keinen Kernel-Treiber (WinRing0: CVE-2020-14979). Siehe docs/WINDOWS_APIS.md.
/// </summary>
public sealed class LiveMonitorService : IDisposable
{
    private ulong _lastIdle, _lastKernel, _lastUser;
    private long _lastRx, _lastTx;
    private DateTime _lastNetTime;
    private PerformanceCounter? _diskIdle;
    private readonly GpuService _gpu = new();
    private bool _diskCounterFailed;
    private readonly SensorHub _sensors = new();
    private IReadOnlyList<SensorReading> _lastSensors = [];
    private int _sensorTick;

    /// <summary>Letzter vollständiger Sensorstand (alle 2 s aktualisiert).</summary>
    public IReadOnlyList<SensorReading> Sensors => _lastSensors;
    public IReadOnlyList<SensorProviderStatus> SensorProviders => _sensors.Providers;

    /// <summary>Einheitliches Sensormodell mit automatischem Quellen-Failover (v1.1 Phase 2).</summary>
    public SensorManager SensorManager { get; } = new();

    private PerformanceCounter? _cpuPerf, _cpuFreq;
    private bool _cpuClockFailed;

    public LiveMonitorService()
    {
        // Baseline setzen, damit das erste Sample sinnvolle Deltas liefert.
        ReadCpu();
        (_lastRx, _lastTx) = ReadNetworkTotals();
        _lastNetTime = DateTime.UtcNow;
    }

    public MonitorSample Sample()
    {
        var cpu = ReadCpu();
        var mem = NativeMethods.GetMemoryStatus();

        var now = DateTime.UtcNow;
        var (rx, tx) = ReadNetworkTotals();
        var seconds = Math.Max(0.001, (now - _lastNetTime).TotalSeconds);
        // Negative Deltas (Adapter neu verbunden) ignorieren.
        var down = rx >= _lastRx ? (rx - _lastRx) * 8 / seconds : 0;
        var up = tx >= _lastTx ? (tx - _lastTx) * 8 / seconds : 0;
        (_lastRx, _lastTx, _lastNetTime) = (rx, tx, now);

        var gpu = _gpu.Sample();
        if (_sensorTick++ % 2 == 0)
        {
            _lastSensors = _sensors.Read();
            SensorManager.Update(_lastSensors.Concat(NativeReadings(cpu, gpu)).ToList());
        }
        // CPU-Temperatur nur aus echten CPU-Sensoren (HWiNFO/LibreHardwareMonitor) – ACPI-Zonen gelten nicht als CPU-Wert.
        var cpuTemp = SensorManager.Best(UnifiedSensorId.CpuPackageTemp) ?? SensorManager.Best(UnifiedSensorId.CpuCoreTemp);
        var acpi = _lastSensors.Where(r => r.Source == "ACPI-Thermalzone").MaxBy(r => r.Value);
        int? gpuTemp = gpu.TemperatureC;
        string? gpuSource = gpuTemp is null ? null : gpu.Source;
        if (SensorManager.Best(UnifiedSensorId.GpuCoreTemp) is { } gt && (gpuTemp is null || gt.Source < SensorSourceKind.WindowsNative))
            (gpuTemp, gpuSource) = ((int)Math.Round(gt.Current), gt.SourceName);
        return new MonitorSample(
            DateTime.Now,
            cpu,
            mem?.dwMemoryLoad,
            mem is { } m ? m.ullTotalPhys - m.ullAvailPhys : null,
            ReadDiskActive(),
            down,
            up,
            gpu.UtilizationPercent,
            gpu.DedicatedUsedBytes,
            gpuTemp,
            gpu.FanPercent,
            cpuTemp is null ? null : (int)Math.Round(cpuTemp.Current),
            cpuTemp?.SourceName,
            gpuSource,
            acpi is null ? null : (int)Math.Round(acpi.Value));
    }

    /// <summary>
    /// Werte, die Windows selbst liefert (ohne Zusatzprogramm): CPU-Last (GetSystemTimes), effektiver CPU-Takt
    /// (Leistungsindikator „Processor Information“ wie im Task-Manager), GPU-Last (GPU Engine) und – bei NVIDIA –
    /// Temperatur/Lüfter über die signierte NVML des Treibers.
    /// </summary>
    private IEnumerable<SensorReading> NativeReadings(double? cpu, GpuSample gpu)
    {
        const string src = "Windows";
        if (cpu is { } c) yield return new SensorReading("CPU", "CPU Gesamt", SensorKind.Load, Math.Round(c, 1), "%", src);
        if (ReadCpuClock() is { } mhz) yield return new SensorReading("CPU", "CPU Clock (effektiv)", SensorKind.Clock, Math.Round(mhz), "MHz", src);
        if (gpu.UtilizationPercent is { } g) yield return new SensorReading("GPU", "GPU Gesamt", SensorKind.Load, Math.Round(g, 1), "%", src);
        var kernelTemp = _lastSensors.Any(r => r.Source == "Windows-Grafikkernel" && r.Kind == SensorKind.Temperature);
        if (gpu.TemperatureC is { } t && gpu.Source is not null && !kernelTemp)
            yield return new SensorReading("GPU (NVIDIA)", "GPU Temperature", SensorKind.Temperature, t, "°C", src);
        if (gpu.FanPercent is { } f && gpu.Source is not null)
            yield return new SensorReading("GPU (NVIDIA)", "GPU Fan", SensorKind.Fan, f, "%", src);
    }

    private double? ReadCpuClock()
    {
        if (_cpuClockFailed) return null;
        try
        {
            _cpuPerf ??= new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", readOnly: true);
            _cpuFreq ??= new PerformanceCounter("Processor Information", "Processor Frequency", "_Total", readOnly: true);
            var perf = _cpuPerf.NextValue();
            var nominal = _cpuFreq.NextValue();
            if (nominal <= 0 || perf <= 0) return null;
            var mhz = nominal * perf / 100.0;
            return mhz is > 100 and < 10000 ? mhz : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _cpuClockFailed = true;
            return null;
        }
    }

    private double? ReadCpu()
    {
        if (!NativeMethods.GetSystemTimes(out var idleFt, out var kernelFt, out var userFt)) return null;
        ulong idle = idleFt.Value, kernel = kernelFt.Value, user = userFt.Value;
        var dIdle = idle - _lastIdle;
        var total = (kernel - _lastKernel) + (user - _lastUser); // Kernel-Zeit enthält Idle-Zeit
        (_lastIdle, _lastKernel, _lastUser) = (idle, kernel, user);
        if (total == 0) return null;
        return Math.Clamp(100.0 * (total - dIdle) / total, 0, 100);
    }

    private static (long Rx, long Tx) ReadNetworkTotals()
    {
        long rx = 0, tx = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var s = ni.GetIPStatistics();
                rx += s.BytesReceived;
                tx += s.BytesSent;
            }
        }
        catch (NetworkInformationException)
        {
        }
        return (rx, tx);
    }

    private double? ReadDiskActive()
    {
        if (_diskCounterFailed) return null;
        try
        {
            // Performance-Counter-Namen werden von .NET über die englischen Namen aufgelöst.
            _diskIdle ??= new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", readOnly: true);
            var idle = _diskIdle.NextValue();
            return Math.Clamp(100.0 - idle, 0, 100);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            _diskCounterFailed = true; // nicht verfügbar → nicht erneut versuchen
            return null;
        }
    }

    public void Dispose()
    {
        _diskIdle?.Dispose();
        _cpuPerf?.Dispose();
        _cpuFreq?.Dispose();
        _gpu.Dispose();
    }
}
