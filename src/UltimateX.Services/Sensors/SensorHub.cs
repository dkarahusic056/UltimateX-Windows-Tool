using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using UltimateX.Services.Common;

namespace UltimateX.Services.Sensors;

public enum SensorKind { Temperature, Fan, Power, Load, Clock, Voltage, Other }

/// <summary>Ein gemessener Sensorwert mit Herkunft – jeder Wert ist einer echten Quelle zugeordnet.</summary>
public sealed record SensorReading(string Hardware, string Name, SensorKind Kind, double Value, string Unit, string Source);

/// <summary>Zustand einer Sensorquelle für die Anzeige („woher kommen die Werte?“).</summary>
public sealed record SensorProviderStatus(string Name, bool Active, int Readings, string Hint);

/// <summary>
/// Sensor-Hub (§6, §19, §20): sammelt Temperaturen, Lüfter und Leistung aus Quellen, die OHNE eigenen
/// Kernel-Treiber auskommen.
///
///  1. Windows-Grafikkernel (D3DKMT, dieselbe Quelle wie der Task-Manager): GPU-Temperatur und Lüfter
///     herstellerunabhängig für AMD, Intel und NVIDIA – sofern der Treiber (WDDM 2.4+) sie meldet.
///  2. ACPI-Thermalzonen (Leistungsindikator „Thermal Zone Information“): Firmware-Sensoren, meist Mainboard
///     oder CPU-nah. Werden ehrlich als „ACPI-Thermalzone“ gekennzeichnet, nicht als CPU-Kerntemperatur.
///  3. HWiNFO (Shared Memory, falls der Benutzer HWiNFO nutzt und „Shared Memory Support“ aktiviert hat).
///  4. LibreHardwareMonitor / OpenHardwareMonitor (deren WMI-Schnittstelle, falls das Programm läuft).
///
/// 3 und 4 liefern echte CPU-Kerntemperaturen: Diese Programme bringen einen eigenen, vom Benutzer bewusst
/// installierten Treiber mit. ULTIMATE X liest nur deren veröffentlichte Werte – es installiert und lädt
/// selbst keinen Treiber (THREAT_MODEL T-24).
/// </summary>
public sealed class SensorHub
{
    private readonly D3dkmtGpuSensors _gpu = new();
    private readonly ThermalZoneSensors _acpi = new();
    private DateTime _lastWmiProbe = DateTime.MinValue;
    private string? _wmiNamespace;

    public IReadOnlyList<SensorProviderStatus> Providers { get; private set; } = [];

    /// <summary>Liest alle verfügbaren Quellen. Dauer typischerweise &lt; 50 ms; Fehler einer Quelle stören die anderen nicht.</summary>
    public IReadOnlyList<SensorReading> Read(bool includeExternal = true)
    {
        var all = new List<SensorReading>();
        var status = new List<SensorProviderStatus>();

        var gpu = Safe(_gpu.Read);
        all.AddRange(gpu);
        status.Add(new("Windows-Grafikkernel (GPU)", gpu.Count > 0, gpu.Count,
            gpu.Count > 0 ? "GPU-Temperatur direkt vom Grafiktreiber (wie im Task-Manager)."
                          : "Der Grafiktreiber meldet keine Temperatur (ältere Treiber, integrierte GPUs oder Remote-Desktop)."));

        var acpi = Safe(_acpi.Read);
        all.AddRange(acpi);
        status.Add(new("ACPI-Thermalzonen (Firmware)", acpi.Count > 0, acpi.Count,
            acpi.Count > 0 ? "Firmware-Sensor des Mainboards – nicht zwingend die CPU-Kerntemperatur."
                           : "Die Firmware stellt keine Thermalzone bereit."));

        if (includeExternal)
        {
            var hw = Safe(HwInfoSharedMemory.Read);
            all.AddRange(hw);
            status.Add(new("HWiNFO (Shared Memory)", hw.Count > 0, hw.Count,
                hw.Count > 0 ? "Werte von HWiNFO inkl. CPU-Kerntemperaturen."
                             : "Optional: HWiNFO starten und unter Einstellungen „Shared Memory Support“ aktivieren."));

            var lhm = Safe(ReadHardwareMonitorWmi);
            all.AddRange(lhm);
            status.Add(new("LibreHardwareMonitor", lhm.Count > 0, lhm.Count,
                lhm.Count > 0 ? $"Werte über WMI ({_wmiNamespace})."
                              : "Optional: LibreHardwareMonitor starten – die Werte werden automatisch übernommen."));
        }

        Providers = status;
        return all;
    }

    /// <summary>
    /// Beste verfügbare CPU-Temperatur. Bevorzugt echte Paket-/Tctl-Werte aus HWiNFO/LibreHardwareMonitor;
    /// ACPI-Zonen werden NICHT als CPU-Temperatur ausgegeben.
    /// </summary>
    public static SensorReading? BestCpuTemperature(IEnumerable<SensorReading> readings)
    {
        var cpu = readings.Where(r => r.Kind == SensorKind.Temperature && r.Source != ThermalZoneSensors.SourceName
                                      && IsCpuHardware(r.Hardware)).ToList();
        string[] priority = ["CPU Package", "CPU (Tctl/Tdie)", "Core (Tctl/Tdie)", "Tctl/Tdie", "CPU Die (average)", "Core Max", "Package", "CPU"];
        foreach (var p in priority)
        {
            var hit = cpu.FirstOrDefault(r => r.Name.Contains(p, StringComparison.OrdinalIgnoreCase));
            if (hit is not null) return hit;
        }
        return null;
    }

    public static SensorReading? BestGpuTemperature(IEnumerable<SensorReading> readings)
    {
        var list = readings.Where(r => r.Kind == SensorKind.Temperature).ToList();
        // 1. Grafikkernel (gleiche Quelle wie Task-Manager), 2. externe Tools „GPU Temperature“/„GPU Core“.
        return list.Where(r => r.Source == D3dkmtGpuSensors.SourceName).MaxBy(r => r.Value)
               ?? list.FirstOrDefault(r => IsGpuHardware(r.Hardware)
                                            && (r.Name.Contains("GPU Temperature", StringComparison.OrdinalIgnoreCase)
                                                || r.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsCpuHardware(string hw) =>
        hw.Contains("CPU", StringComparison.OrdinalIgnoreCase) || hw.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("Intel Core", StringComparison.OrdinalIgnoreCase) || hw.Contains("Processor", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuHardware(string hw) =>
        hw.Contains("GPU", StringComparison.OrdinalIgnoreCase) || hw.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || hw.Contains("Arc", StringComparison.Ordinal);

    private static IReadOnlyList<SensorReading> Safe(Func<IReadOnlyList<SensorReading>> read)
    {
        try { return read(); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return [];
        }
    }

    /// <summary>LibreHardwareMonitor/OpenHardwareMonitor veröffentlichen ihre Sensoren als WMI-Klassen.</summary>
    private IReadOnlyList<SensorReading> ReadHardwareMonitorWmi()
    {
        // Namespace nur alle 30 s neu suchen – eine Abfrage auf einen fehlenden Namespace ist teuer.
        if (_wmiNamespace is null)
        {
            if (DateTime.UtcNow - _lastWmiProbe < TimeSpan.FromSeconds(30)) return [];
            _lastWmiProbe = DateTime.UtcNow;
            foreach (var ns in new[] { @"root\LibreHardwareMonitor", @"root\OpenHardwareMonitor" })
            {
                if (Wmi.Query("SELECT Identifier FROM Hardware", ns, 3).Ok) { _wmiNamespace = ns; break; }
            }
            if (_wmiNamespace is null) return [];
        }

        var hw = Wmi.Query("SELECT Identifier, Name FROM Hardware", _wmiNamespace, 3);
        var sensors = Wmi.Query("SELECT Name, Value, SensorType, Parent FROM Sensor", _wmiNamespace, 3);
        if (!hw.Ok || !sensors.Ok)
        {
            _wmiNamespace = null; // Programm wurde beendet
            return [];
        }
        var names = hw.Rows.Where(r => r.Str("Identifier") is not null)
                           .GroupBy(r => r.Str("Identifier")!)
                           .ToDictionary(g => g.Key, g => g.First().Str("Name") ?? g.Key, StringComparer.Ordinal);
        var source = _wmiNamespace.Contains("Libre", StringComparison.Ordinal) ? "LibreHardwareMonitor" : "OpenHardwareMonitor";
        var list = new List<SensorReading>();
        foreach (var r in sensors.Rows)
        {
            if (r["Value"] is not float and not double) continue;
            var value = Convert.ToDouble(r["Value"], System.Globalization.CultureInfo.InvariantCulture);
            if (double.IsNaN(value) || double.IsInfinity(value)) continue;
            var (kind, unit) = (r.Str("SensorType") ?? "") switch
            {
                "Temperature" => (SensorKind.Temperature, "°C"),
                "Fan" => (SensorKind.Fan, "RPM"),
                "Power" => (SensorKind.Power, "W"),
                "Load" => (SensorKind.Load, "%"),
                "Clock" => (SensorKind.Clock, "MHz"),
                "Voltage" => (SensorKind.Voltage, "V"),
                _ => (SensorKind.Other, ""),
            };
            if (kind == SensorKind.Other) continue;
            if (kind == SensorKind.Temperature && (value <= 0 || value > 150)) continue;
            var parent = r.Str("Parent") ?? "";
            list.Add(new SensorReading(names.GetValueOrDefault(parent, parent), r.Str("Name") ?? "?", kind, value, unit, source));
        }
        return list;
    }
}

/// <summary>
/// GPU-Temperatur/Lüfter über den Windows-Grafikkernel (gdi32 D3DKMTQueryAdapterInfo, KMTQAITYPE_ADAPTERPERFDATA).
/// Öffentliche, dokumentierte API ab Windows 10 2004 / WDDM 2.4 – herstellerunabhängig, ohne Adminrechte.
/// </summary>
internal sealed class D3dkmtGpuSensors
{
    public const string SourceName = "Windows-Grafikkernel";

    private const int KMTQAITYPE_ADAPTERREGISTRYINFO = 8;
    private const int KMTQAITYPE_ADAPTERPERFDATA = 62;
    private const int KMTQAITYPE_ADAPTERPERFDATA_CAPS = 63;

    private bool _unsupported;

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO { public uint hAdapter; public LUID AdapterLuid; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATA
    {
        public uint PhysicalAdapterIndex;
        public ulong MemoryFrequency;
        public ulong MaxMemoryFrequency;
        public ulong MaxMemoryFrequencyOC;
        public ulong MemoryBandwidth;
        public ulong PCIEBandwidth;
        public uint FanRPM;
        public uint Power;        // Zehntel-Prozent der maximalen Leistung
        public uint Temperature;  // Zehntel-Grad Celsius
        public byte PowerStateOverride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTER_PERFDATACAPS
    {
        public uint PhysicalAdapterIndex;
        public ulong MaxMemoryBandwidth;
        public ulong MaxPCIEBandwidth;
        public uint MaxFanRPM;
        public uint TemperatureMax;
        public uint TemperatureWarning;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct D3DKMT_ADAPTERREGISTRYINFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string AdapterString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string BiosString;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DacType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ChipType;
    }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);

    public IReadOnlyList<SensorReading> Read()
    {
        if (_unsupported) return [];
        var result = new List<SensorReading>();
        var e = new D3DKMT_ENUMADAPTERS2();
        try
        {
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0 || e.NumAdapters > 64) return result;
        }
        catch (EntryPointNotFoundException) { _unsupported = true; return result; }

        var size = Marshal.SizeOf<D3DKMT_ADAPTERINFO>();
        var buffer = Marshal.AllocHGlobal(size * (int)e.NumAdapters);
        try
        {
            e.pAdapters = buffer;
            if (D3DKMTEnumAdapters2(ref e) != 0) return result;
            for (var i = 0; i < e.NumAdapters; i++)
            {
                var info = Marshal.PtrToStructure<D3DKMT_ADAPTERINFO>(buffer + i * size);
                try
                {
                    ReadAdapter(info.hAdapter, result);
                }
                finally
                {
                    var close = new D3DKMT_CLOSEADAPTER { hAdapter = info.hAdapter };
                    D3DKMTCloseAdapter(ref close);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
        return result;
    }

    private static void ReadAdapter(uint handle, List<SensorReading> result)
    {
        var caps = new D3DKMT_ADAPTER_PERFDATACAPS();
        if (!Query(handle, KMTQAITYPE_ADAPTERPERFDATA_CAPS, ref caps)) return;
        var perf = new D3DKMT_ADAPTER_PERFDATA();
        if (!Query(handle, KMTQAITYPE_ADAPTERPERFDATA, ref perf)) return;

        var reg = new D3DKMT_ADAPTERREGISTRYINFO();
        var name = Query(handle, KMTQAITYPE_ADAPTERREGISTRYINFO, ref reg) && !string.IsNullOrWhiteSpace(reg.AdapterString)
            ? reg.AdapterString.Trim() : "GPU";

        // Nur melden, wenn der Treiber die Temperatur unterstützt (TemperatureMax > 0) und der Wert plausibel ist.
        var tempC = perf.Temperature / 10.0;
        if (caps.TemperatureMax > 0 && tempC > 0 && tempC < 150)
            result.Add(new SensorReading(name, "GPU Temperature", SensorKind.Temperature, Math.Round(tempC, 1), "°C", SourceName));
        if (caps.MaxFanRPM > 0 && perf.FanRPM is > 0 and < 20000)
            result.Add(new SensorReading(name, "GPU Fan", SensorKind.Fan, perf.FanRPM, "RPM", SourceName));
        if (perf.Power is > 0 and <= 1500)
            result.Add(new SensorReading(name, "GPU Power (relativ)", SensorKind.Load, perf.Power / 10.0, "%", SourceName));
    }

    private static bool Query<T>(uint handle, int type, ref T data) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(data, ptr, false);
            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = handle, Type = type, pPrivateDriverData = ptr, PrivateDriverDataSize = (uint)size };
            if (D3DKMTQueryAdapterInfo(ref q) != 0) return false;
            data = Marshal.PtrToStructure<T>(ptr);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}

/// <summary>ACPI-Thermalzonen über den Leistungsindikator „Thermal Zone Information“ (ohne Adminrechte lesbar).</summary>
internal sealed class ThermalZoneSensors
{
    public const string SourceName = "ACPI-Thermalzone";
    private bool _failed;

    public IReadOnlyList<SensorReading> Read()
    {
        if (_failed) return [];
        var list = new List<SensorReading>();
        try
        {
            if (!PerformanceCounterCategory.Exists("Thermal Zone Information")) { _failed = true; return list; }
            var data = new PerformanceCounterCategory("Thermal Zone Information").ReadCategory();
            var hp = data.Contains("High Precision Temperature");
            var key = hp ? "High Precision Temperature" : "Temperature";
            if (!data.Contains(key)) return list;
            foreach (InstanceData inst in data[key].Values)
            {
                var kelvin = hp ? inst.RawValue / 10.0 : inst.RawValue;
                var c = Math.Round(kelvin - 273.15, 1);
                if (c is <= 0 or >= 150) continue; // Platzhalterwerte der Firmware ausblenden
                var zone = inst.InstanceName.Replace(@"\_TZ.", "", StringComparison.Ordinal).Trim('\\');
                list.Add(new SensorReading("Mainboard (ACPI)", "Thermalzone " + zone, SensorKind.Temperature, c, "°C", SourceName));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _failed = true;
        }
        return list;
    }
}

/// <summary>
/// Liest die öffentlich dokumentierte Shared-Memory-Schnittstelle von HWiNFO („Global\HWiNFO_SENS_SM2“), nur lesend.
/// Struktur (pack 1): Header mit Offsets/Größen der Sensor- und Messwert-Abschnitte; Größen werden aus dem Header
/// genommen und gegen die View-Größe geprüft, damit fremde/defekte Daten nie zu Lesefehlern führen.
/// </summary>
internal static class HwInfoSharedMemory
{
    private const string MapName = @"Global\HWiNFO_SENS_SM2";
    private const uint Signature = 0x53695748; // "HWiS"

    public static IReadOnlyList<SensorReading> Read()
    {
        MemoryMappedFile mmf;
        try { mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read); }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException) { return []; }

        using (mmf)
        using (var view = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read))
        {
            var cap = view.Capacity;
            if (cap < 44 || view.ReadUInt32(0) != Signature) return [];
            var sensorOffset = view.ReadUInt32(20);
            var sensorSize = view.ReadUInt32(24);
            var sensorCount = view.ReadUInt32(28);
            var readingOffset = view.ReadUInt32(32);
            var readingSize = view.ReadUInt32(36);
            var readingCount = view.ReadUInt32(40);
            if (sensorSize < 264 || readingSize < 316 || sensorCount > 1024 || readingCount > 16384) return [];
            if (sensorOffset + (long)sensorSize * sensorCount > cap || readingOffset + (long)readingSize * readingCount > cap) return [];

            var sensorNames = new string[sensorCount];
            for (var i = 0; i < sensorCount; i++)
            {
                long b = sensorOffset + (long)i * sensorSize;
                var user = ReadString(view, b + 136, 128);
                sensorNames[i] = user.Length > 0 ? user : ReadString(view, b + 8, 128);
            }

            var list = new List<SensorReading>();
            for (var i = 0; i < readingCount; i++)
            {
                long b = readingOffset + (long)i * readingSize;
                var type = view.ReadUInt32(b);
                var sensorIndex = view.ReadUInt32(b + 4);
                var kind = type switch
                {
                    1 => SensorKind.Temperature,
                    2 => SensorKind.Voltage,
                    3 => SensorKind.Fan,
                    5 => SensorKind.Power,
                    6 => SensorKind.Clock,
                    7 => SensorKind.Load,
                    _ => SensorKind.Other,
                };
                if (kind == SensorKind.Other) continue;
                var label = ReadString(view, b + 140, 128);
                if (label.Length == 0) label = ReadString(view, b + 12, 128);
                var unit = ReadString(view, b + 268, 16);
                var value = view.ReadDouble(b + 284);
                if (double.IsNaN(value) || double.IsInfinity(value)) continue;
                if (kind == SensorKind.Temperature && (value <= 0 || value > 150)) continue;
                var hw = sensorIndex < sensorCount ? sensorNames[sensorIndex] : "HWiNFO";
                list.Add(new SensorReading(hw, label, kind, Math.Round(value, 1), unit, "HWiNFO"));
            }
            return list;
        }
    }

    private static string ReadString(MemoryMappedViewAccessor view, long offset, int max)
    {
        var bytes = new byte[max];
        view.ReadArray(offset, bytes, 0, max);
        var len = Array.IndexOf(bytes, (byte)0);
        if (len < 0) len = max;
        // HWiNFO schreibt ANSI bzw. ab 7.x UTF-8 – UTF-8 bevorzugen, bei ungültigen Bytes Latin-1.
        try { return new UTF8Encoding(false, true).GetString(bytes, 0, len).Trim(); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes, 0, len).Trim(); }
    }
}
