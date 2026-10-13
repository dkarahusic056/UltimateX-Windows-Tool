using System.Diagnostics;

namespace UltimateX.Services.Sensors;

/// <summary>Sensorquellen in Prioritätsreihenfolge (kleinerer Wert = höhere Priorität, v1.1 §Failover).</summary>
public enum SensorSourceKind
{
    HwInfo = 0,
    LibreHardwareMonitor = 1,
    WindowsNative = 2,
    AcpiThermal = 3,
}

/// <summary>
/// Zustand einer Quelle:
/// Connected = liefert Werte und mindestens einer wird verwendet;
/// Available = liefert Werte, alle werden aber von einer höher priorisierten Quelle überdeckt;
/// Inactive = Programm läuft, stellt aber keine Werte bereit (z. B. HWiNFO ohne Shared Memory);
/// NotAvailable = nicht installiert bzw. nicht gestartet.
/// </summary>
public enum SensorSourceState { Connected, Available, Inactive, NotAvailable }

/// <summary>Einheitliches Sensormodell – unabhängig davon, aus welcher Quelle der Wert stammt.</summary>
public enum UnifiedSensorId
{
    CpuPackageTemp,
    CpuCoreTemp,
    CpuLoad,
    CpuClock,
    CpuPower,
    CpuFan,
    GpuCoreTemp,
    GpuHotspotTemp,
    GpuMemoryTemp,
    GpuLoad,
    GpuClock,
    GpuPower,
    GpuFan,
    MainboardTemp,
    VrmTemp,
    ChipsetTemp,
    NvmeTemp,
    SystemFan,
}

public sealed record SensorSourceStatus(
    SensorSourceKind Kind,
    string Name,
    SensorSourceState State,
    int SensorCount,
    int UsedSensors,
    int HardwareCount,
    DateTime? LastUpdate,
    string Detail)
{
    /// <summary>Technische Statuszeile wie „HWiNFO Sensor Source: NOT AVAILABLE“.</summary>
    public string StateText => State switch
    {
        SensorSourceState.Connected => "CONNECTED",
        SensorSourceState.Available => "AVAILABLE",
        SensorSourceState.Inactive => "INACTIVE",
        _ => "NOT AVAILABLE",
    };

    public string Headline => $"{Name} Sensor Source: {StateText}";
}

/// <summary>Ein vereinheitlichter Sensor mit Statistik seit Programmstart (Current/Min/Average/Max).</summary>
public sealed record UnifiedSensor(
    UnifiedSensorId Id,
    string Code,
    string Label,
    string Hardware,
    double Current,
    double Min,
    double Average,
    double Max,
    string Unit,
    SensorSourceKind Source,
    string SourceName,
    DateTime LastUpdate,
    long Samples);

/// <summary>
/// Sensor Source Manager (v1.1 Phase 2): ordnet die Rohwerte aller Quellen dem einheitlichen Modell zu,
/// wählt je Sensor automatisch die beste verfügbare Quelle (HWiNFO → LibreHardwareMonitor → Windows nativ → ACPI)
/// und führt Min/Durchschnitt/Max. Es werden ausschließlich gemessene Werte weitergegeben – fehlt eine Quelle,
/// fehlt der Sensor, statt dass ein Wert geschätzt wird. Thread-sicher (Aufruf aus dem Monitor-Thread, Lesen aus der UI).
/// </summary>
public sealed class SensorManager
{
    private sealed class Stat
    {
        public double Min = double.MaxValue, Max = double.MinValue, Sum, Last;
        public long Count;
        public DateTime LastUpdate;

        public void Add(double v, DateTime t)
        {
            Last = v;
            if (v < Min) Min = v;
            if (v > Max) Max = v;
            Sum += v;
            Count++;
            LastUpdate = t;
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Stat> _stats = new(StringComparer.Ordinal);
    private readonly Dictionary<SensorSourceKind, DateTime> _lastSeen = [];
    private IReadOnlyList<UnifiedSensor> _sensors = [];
    private IReadOnlyList<SensorSourceStatus> _sources = Enum.GetValues<SensorSourceKind>()
        .Select(k => new SensorSourceStatus(k, SourceDisplayName(k), SensorSourceState.NotAvailable, 0, 0, 0, null, "Noch nicht abgefragt")).ToList();
    private DateTime _lastProcessProbe = DateTime.MinValue;
    private bool _hwInfoRunning, _lhmRunning;

    /// <summary>Optionaler Prozess-Check (für Tests ersetzbar).</summary>
    public Func<string[], bool> IsProcessRunning { get; init; } = DefaultIsProcessRunning;

    public IReadOnlyList<UnifiedSensor> Sensors { get { lock (_gate) return _sensors; } }

    public IReadOnlyList<SensorSourceStatus> Sources { get { lock (_gate) return _sources; } }

    public DateTime? LastUpdate { get; private set; }

    public static string SourceDisplayName(SensorSourceKind k) => k switch
    {
        SensorSourceKind.HwInfo => "HWiNFO",
        SensorSourceKind.LibreHardwareMonitor => "LibreHardwareMonitor",
        SensorSourceKind.WindowsNative => "Windows Native Sensors",
        _ => "ACPI Thermal Zones",
    };

    public static SensorSourceKind KindOf(string source) => source switch
    {
        "HWiNFO" => SensorSourceKind.HwInfo,
        "LibreHardwareMonitor" or "OpenHardwareMonitor" => SensorSourceKind.LibreHardwareMonitor,
        ThermalZoneSensors.SourceName => SensorSourceKind.AcpiThermal,
        _ => SensorSourceKind.WindowsNative,
    };

    /// <summary>Technischer Code des einheitlichen Modells, z. B. CPU_PACKAGE_TEMP.</summary>
    public static string CodeOf(UnifiedSensorId id) => id switch
    {
        UnifiedSensorId.CpuPackageTemp => "CPU_PACKAGE_TEMP",
        UnifiedSensorId.CpuCoreTemp => "CPU_CORE_TEMP",
        UnifiedSensorId.CpuLoad => "CPU_LOAD",
        UnifiedSensorId.CpuClock => "CPU_CLOCK",
        UnifiedSensorId.CpuPower => "CPU_POWER",
        UnifiedSensorId.CpuFan => "CPU_FAN",
        UnifiedSensorId.GpuCoreTemp => "GPU_CORE_TEMP",
        UnifiedSensorId.GpuHotspotTemp => "GPU_HOTSPOT_TEMP",
        UnifiedSensorId.GpuMemoryTemp => "GPU_MEMORY_TEMP",
        UnifiedSensorId.GpuLoad => "GPU_LOAD",
        UnifiedSensorId.GpuClock => "GPU_CLOCK",
        UnifiedSensorId.GpuPower => "GPU_POWER",
        UnifiedSensorId.GpuFan => "GPU_FAN",
        UnifiedSensorId.MainboardTemp => "MAINBOARD_TEMP",
        UnifiedSensorId.VrmTemp => "VRM_TEMP",
        UnifiedSensorId.ChipsetTemp => "CHIPSET_TEMP",
        UnifiedSensorId.NvmeTemp => "NVME_TEMP",
        _ => "SYSTEM_FAN",
    };

    public static string LabelOf(UnifiedSensorId id) => id switch
    {
        UnifiedSensorId.CpuPackageTemp => "CPU Package",
        UnifiedSensorId.CpuCoreTemp => "CPU-Kern",
        UnifiedSensorId.CpuLoad => "CPU-Last",
        UnifiedSensorId.CpuClock => "CPU-Takt",
        UnifiedSensorId.CpuPower => "CPU-Leistung",
        UnifiedSensorId.CpuFan => "CPU-Lüfter",
        UnifiedSensorId.GpuCoreTemp => "GPU-Kern",
        UnifiedSensorId.GpuHotspotTemp => "GPU Hotspot",
        UnifiedSensorId.GpuMemoryTemp => "GPU-Speicher",
        UnifiedSensorId.GpuLoad => "GPU-Last",
        UnifiedSensorId.GpuClock => "GPU-Takt",
        UnifiedSensorId.GpuPower => "GPU-Leistung",
        UnifiedSensorId.GpuFan => "GPU-Lüfter",
        UnifiedSensorId.MainboardTemp => "Mainboard",
        UnifiedSensorId.VrmTemp => "VRM",
        UnifiedSensorId.ChipsetTemp => "Chipsatz",
        UnifiedSensorId.NvmeTemp => "Laufwerk",
        _ => "Systemlüfter",
    };

    /// <summary>
    /// Ordnet einen Rohwert dem einheitlichen Modell zu. Unbekannte Sensoren liefern null und werden nicht verwendet
    /// (sie bleiben in der Rohansicht des Live Monitors sichtbar).
    /// </summary>
    public static UnifiedSensorId? Classify(SensorReading r)
    {
        var n = r.Name;
        var hw = r.Hardware;
        bool N(string s) => n.Contains(s, StringComparison.OrdinalIgnoreCase);
        bool H(string s) => hw.Contains(s, StringComparison.OrdinalIgnoreCase);
        var cpu = IsCpu(hw);
        var gpu = IsGpu(hw) || (!cpu && N("GPU"));
        var drive = H("S.M.A.R.T") || H("NVMe") || H("SSD") || H("Drive") || H("HDD") || N("Drive Temperature") || N("Composite");

        if (r.Source == ThermalZoneSensors.SourceName)
            return r.Kind == SensorKind.Temperature ? UnifiedSensorId.MainboardTemp : null;

        switch (r.Kind)
        {
            case SensorKind.Temperature:
                if (gpu)
                {
                    if (N("Hot Spot") || N("Hotspot") || N("Junction") && !N("Memory")) return UnifiedSensorId.GpuHotspotTemp;
                    if (N("Memory") || N("VRAM") || N("Mem ")) return UnifiedSensorId.GpuMemoryTemp;
                    if (N("GPU Temperature") || N("GPU Core") || n.Equals("GPU", StringComparison.OrdinalIgnoreCase) || N("Edge") || N("GPU Temp"))
                        return UnifiedSensorId.GpuCoreTemp;
                    return null;
                }
                if (cpu)
                {
                    if (N("CCD") || N("Core #") || N("P-core") || N("E-core")) return UnifiedSensorId.CpuCoreTemp;
                    if (N("Package") || N("Tctl") || N("Tdie") || N("CPU Die (average)") || n.Equals("CPU", StringComparison.OrdinalIgnoreCase))
                        return UnifiedSensorId.CpuPackageTemp;
                    if (N("Core") || N("CCD")) return UnifiedSensorId.CpuCoreTemp;
                    return null;
                }
                if (drive) return UnifiedSensorId.NvmeTemp;
                if (N("VRM") || N("MOS") || N("VR ")) return UnifiedSensorId.VrmTemp;
                if (N("Chipset") || N("PCH")) return UnifiedSensorId.ChipsetTemp;
                if (N("Motherboard") || N("Mainboard") || N("System") || N("SYSTIN") || N("Temperature #")) return UnifiedSensorId.MainboardTemp;
                // CPU-Sockel-Sensoren des Mainboard-Chips sind keine CPU-Kerntemperatur → als Mainboard führen.
                if (N("CPU")) return UnifiedSensorId.MainboardTemp;
                return null;

            case SensorKind.Fan:
                if (gpu || N("GPU")) return UnifiedSensorId.GpuFan;
                if (N("CPU")) return UnifiedSensorId.CpuFan;
                return UnifiedSensorId.SystemFan;

            case SensorKind.Power:
                if (cpu && (N("Package") || N("PPT") || N("CPU Power") || n.Equals("CPU", StringComparison.OrdinalIgnoreCase)))
                    return UnifiedSensorId.CpuPower;
                if (gpu && (N("Total Board Power") || N("GPU Power") || N("Board Power") || N("GPU Package") || N("GPU Core") || N("TGP")))
                    return UnifiedSensorId.GpuPower;
                return null;

            case SensorKind.Clock:
                if (N("Bus") || N("Ratio") || N("Uncore") || N("Ring") || N("Memory") || N("Video") || N("Shader") || N("SoC")) return null;
                if (cpu && (N("Core") || N("Effective") || N("CPU Clock") || N("Frequenz"))) return UnifiedSensorId.CpuClock;
                if (gpu && (N("GPU Clock") || N("GPU Core") || n.Equals("Core", StringComparison.OrdinalIgnoreCase))) return UnifiedSensorId.GpuClock;
                return null;

            case SensorKind.Load:
                if (N("relativ")) return null;
                if (cpu && (N("Total") || N("CPU Gesamt") || N("CPU Usage") || N("Utility"))) return UnifiedSensorId.CpuLoad;
                if (gpu && (N("GPU Core") || N("GPU Utilization") || N("GPU Load") || N("GPU D3D Usage") || N("GPU Gesamt") || N("Core Load")))
                    return UnifiedSensorId.GpuLoad;
                return null;
        }
        return null;
    }

    internal static bool IsCpu(string hw) =>
        hw.Contains("CPU", StringComparison.OrdinalIgnoreCase) || hw.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("Intel Core", StringComparison.OrdinalIgnoreCase) || hw.Contains("Processor", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("Xeon", StringComparison.OrdinalIgnoreCase) || hw.Contains("Threadripper", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("Prozessor", StringComparison.OrdinalIgnoreCase);

    internal static bool IsGpu(string hw) =>
        hw.Contains("GPU", StringComparison.OrdinalIgnoreCase) || hw.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("GeForce", StringComparison.OrdinalIgnoreCase) || hw.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)
        || hw.Contains("Arc ", StringComparison.Ordinal) || hw.Contains("Graphics", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Verarbeitet einen neuen Satz Rohwerte. <paramref name="readings"/> enthält alle Quellen gemischt;
    /// die Zuordnung zur Quelle erfolgt über <see cref="SensorReading.Source"/>.
    /// </summary>
    public void Update(IReadOnlyList<SensorReading> readings, DateTime? now = null)
    {
        var t = now ?? DateTime.Now;
        if ((t - _lastProcessProbe).Duration() > TimeSpan.FromSeconds(10))
        {
            _lastProcessProbe = t;
            _hwInfoRunning = SafeRunning(["HWiNFO64", "HWiNFO32", "HWiNFO", "HWiNFOARM64"]);
            _lhmRunning = SafeRunning(["LibreHardwareMonitor", "OpenHardwareMonitor"]);
        }

        var classified = readings
            .Select(r => (Reading: r, Id: Classify(r), Kind: KindOf(r.Source)))
            .ToList();

        foreach (var k in classified.Select(c => c.Kind).Distinct()) _lastSeen[k] = t;

        // Failover: je Sensor-ID die Quelle mit der höchsten Priorität, die einen Wert liefert.
        var chosen = new List<(SensorReading Reading, UnifiedSensorId Id, SensorSourceKind Kind)>();
        foreach (var group in classified.Where(c => c.Id is not null).GroupBy(c => c.Id!.Value))
        {
            var best = group.Min(c => c.Kind);
            chosen.AddRange(group.Where(c => c.Kind == best).Select(c => (c.Reading, group.Key, c.Kind)));
        }

        var result = new List<UnifiedSensor>(chosen.Count);
        lock (_gate)
        {
            foreach (var (r, id, kind) in chosen)
            {
                var key = $"{id}|{kind}|{r.Hardware}|{r.Name}";
                if (!_stats.TryGetValue(key, out var st)) _stats[key] = st = new Stat();
                st.Add(r.Value, t);
                result.Add(new UnifiedSensor(id, CodeOf(id), LabelFor(id, r), r.Hardware, r.Value, st.Min, st.Sum / st.Count, st.Max,
                    r.Unit, kind, SourceDisplayName(kind), t, st.Count));
            }
            _sensors = result.OrderBy(s => s.Id).ThenBy(s => s.Label, StringComparer.OrdinalIgnoreCase).ToList();

            var sources = new List<SensorSourceStatus>();
            foreach (var kind in Enum.GetValues<SensorSourceKind>())
            {
                var all = classified.Where(c => c.Kind == kind).ToList();
                var used = chosen.Count(c => c.Kind == kind);
                var hwCount = all.Select(c => c.Reading.Hardware).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                DateTime? seen = _lastSeen.TryGetValue(kind, out var ls) ? ls : null;
                SensorSourceState state;
                string detail;
                if (all.Count > 0)
                {
                    state = used > 0 ? SensorSourceState.Connected : SensorSourceState.Available;
                    detail = used > 0 ? $"{used} von {all.Count} Werten verwendet" : "Werte vorhanden, aber durch eine höher priorisierte Quelle abgedeckt";
                }
                else
                {
                    (state, detail) = kind switch
                    {
                        SensorSourceKind.HwInfo when _hwInfoRunning =>
                            (SensorSourceState.Inactive, "HWiNFO läuft, stellt aber keine Werte bereit: Einstellungen → „Shared Memory Support“ aktivieren und das Sensorfenster geöffnet lassen."),
                        SensorSourceKind.HwInfo =>
                            (SensorSourceState.NotAvailable, "HWiNFO ist nicht gestartet. Optional: HWiNFO starten und „Shared Memory Support“ aktivieren."),
                        SensorSourceKind.LibreHardwareMonitor when _lhmRunning =>
                            (SensorSourceState.Inactive, "LibreHardwareMonitor läuft, die WMI-Schnittstelle liefert aber keine Daten. LibreHardwareMonitor als Administrator starten."),
                        SensorSourceKind.LibreHardwareMonitor =>
                            (SensorSourceState.NotAvailable, "LibreHardwareMonitor ist nicht gestartet. Optional: Programm starten – die Werte werden automatisch übernommen."),
                        SensorSourceKind.AcpiThermal =>
                            (SensorSourceState.NotAvailable, "Die Firmware stellt keine ACPI-Thermalzone bereit."),
                        _ => (SensorSourceState.NotAvailable, "Windows meldet keine Sensorwerte."),
                    };
                }
                sources.Add(new SensorSourceStatus(kind, SourceDisplayName(kind), state, all.Count, used, hwCount, seen, detail));
            }
            _sources = sources;
            LastUpdate = t;
        }
    }

    /// <summary>Bester Wert eines Sensors (bei mehreren Instanzen, z. B. Kernen: das Maximum).</summary>
    public UnifiedSensor? Best(UnifiedSensorId id)
    {
        var list = Sensors.Where(s => s.Id == id).ToList();
        return list.Count == 0 ? null : list.MaxBy(s => s.Current);
    }

    /// <summary>Min/Max/Durchschnitt zurücksetzen.</summary>
    public void ResetStatistics()
    {
        lock (_gate) _stats.Clear();
    }

    private static string LabelFor(UnifiedSensorId id, SensorReading r)
    {
        // Mehrfach vorhandene Sensoren (Kerne, Lüfter, Laufwerke) behalten ihren Originalnamen zur Unterscheidung.
        return id is UnifiedSensorId.CpuCoreTemp or UnifiedSensorId.CpuClock or UnifiedSensorId.SystemFan
                   or UnifiedSensorId.NvmeTemp or UnifiedSensorId.MainboardTemp or UnifiedSensorId.CpuFan or UnifiedSensorId.GpuFan
            ? $"{LabelOf(id)} · {r.Name}"
            : LabelOf(id);
    }

    private bool SafeRunning(string[] names)
    {
        try { return IsProcessRunning(names); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
    }

    private static bool DefaultIsProcessRunning(string[] names)
    {
        foreach (var n in names)
        {
            var procs = Process.GetProcessesByName(n);
            var found = procs.Length > 0;
            foreach (var p in procs) p.Dispose();
            if (found) return true;
        }
        return false;
    }
}
