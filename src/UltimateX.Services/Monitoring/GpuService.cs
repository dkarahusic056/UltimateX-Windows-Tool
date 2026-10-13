using System.Diagnostics;
using System.Runtime.InteropServices;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Services.Monitoring;

public sealed record GpuSample(double? UtilizationPercent, ulong? DedicatedUsedBytes, int? TemperatureC, int? FanPercent, string? Source);

/// <summary>
/// GPU-Werte ohne Kernel-Treiber (§6, §19, §20):
///  - Auslastung und belegter VRAM über die Windows-Leistungsindikatoren "GPU Engine" / "GPU Adapter Memory"
///    (dieselbe Quelle wie der Task-Manager, herstellerunabhängig, ab Windows 10 1709).
///  - Temperatur und Lüfter für NVIDIA über NVML (nvml.dll des installierten NVIDIA-Treibers).
///    Die DLL wird nur geladen, wenn sie aus System32 stammt und gültig von NVIDIA signiert ist.
///  - AMD/Intel-Temperaturen: keine sichere, öffentliche Benutzer-API → "nicht verfügbar".
/// </summary>
public sealed class GpuService : IDisposable
{
    private readonly Dictionary<string, CounterSample> _prevEngine = new(StringComparer.Ordinal);
    private bool _countersFailed;
    private Nvml? _nvml;
    private bool _nvmlTried;

    public GpuSample Sample()
    {
        double? util = null;
        ulong? vram = null;
        if (!_countersFailed)
        {
            try
            {
                util = ReadEngineUtilization();
                vram = ReadDedicatedUsage();
            }
            catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                _countersFailed = true;
            }
        }

        if (!_nvmlTried)
        {
            _nvmlTried = true;
            _nvml = Nvml.TryLoad();
        }
        int? temp = null, fan = null;
        if (_nvml is not null)
        {
            (temp, fan) = _nvml.ReadFirstGpu();
        }
        return new GpuSample(util, vram, temp, fan, _nvml is not null ? "NVIDIA NVML" : null);
    }

    /// <summary>
    /// Task-Manager-Logik: je Adapter und Engine-Typ die Auslastung aller Prozesse summieren,
    /// davon das Maximum (z. B. 3D) als Gesamtauslastung.
    /// </summary>
    private double? ReadEngineUtilization()
    {
        var cat = new PerformanceCounterCategory("GPU Engine");
        var data = cat.ReadCategory();
        if (!data.Contains("Utilization Percentage")) return null;
        var samples = data["Utilization Percentage"];

        var sums = new Dictionary<string, double>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (InstanceData inst in samples.Values)
        {
            var name = inst.InstanceName;
            seen.Add(name);
            var cur = inst.Sample;
            if (_prevEngine.TryGetValue(name, out var prev))
            {
                var value = CounterSample.Calculate(prev, cur);
                var luid = Between(name, "luid_", "_phys");
                var eng = name[(name.LastIndexOf("engtype_", StringComparison.Ordinal) + 8)..];
                var key = luid + "|" + eng;
                sums[key] = sums.GetValueOrDefault(key) + value;
            }
            _prevEngine[name] = cur;
        }
        foreach (var gone in _prevEngine.Keys.Where(k => !seen.Contains(k)).ToList()) _prevEngine.Remove(gone);
        return sums.Count == 0 ? null : Math.Clamp(sums.Values.Max(), 0, 100);
    }

    private static ulong? ReadDedicatedUsage()
    {
        var cat = new PerformanceCounterCategory("GPU Adapter Memory");
        var data = cat.ReadCategory();
        if (!data.Contains("Dedicated Usage")) return null;
        ulong max = 0;
        foreach (InstanceData inst in data["Dedicated Usage"].Values)
            max = Math.Max(max, (ulong)Math.Max(0, inst.RawValue));
        return max == 0 ? null : max;
    }

    private static string Between(string s, string start, string end)
    {
        var a = s.IndexOf(start, StringComparison.Ordinal);
        if (a < 0) return "";
        a += start.Length;
        var b = s.IndexOf(end, a, StringComparison.Ordinal);
        return b < 0 ? s[a..] : s[a..b];
    }

    public void Dispose() => _nvml?.Dispose();

    /// <summary>Minimaler, sicherer NVML-Zugriff (nur lesend).</summary>
    private sealed class Nvml : IDisposable
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetCountFn(out uint count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHandleFn(uint index, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetTempFn(IntPtr device, int sensor, out uint temp);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetFanFn(IntPtr device, out uint speed);

        private readonly IntPtr _lib;
        private readonly InitFn _shutdown;
        private readonly GetHandleFn _getHandle;
        private readonly GetCountFn _getCount;
        private readonly GetTempFn _getTemp;
        private readonly GetFanFn? _getFan;

        private Nvml(IntPtr lib)
        {
            _lib = lib;
            T Fn<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
            _shutdown = Fn<InitFn>("nvmlShutdown");
            _getCount = Fn<GetCountFn>("nvmlDeviceGetCount_v2");
            _getHandle = Fn<GetHandleFn>("nvmlDeviceGetHandleByIndex_v2");
            _getTemp = Fn<GetTempFn>("nvmlDeviceGetTemperature");
            _getFan = NativeLibrary.TryGetExport(lib, "nvmlDeviceGetFanSpeed", out var fanPtr)
                ? Marshal.GetDelegateForFunctionPointer<GetFanFn>(fanPtr) : null;
        }

        public static Nvml? TryLoad()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "nvml.dll");
            if (!File.Exists(path)) return null;

            // Nur eine gültig von NVIDIA signierte DLL aus System32 laden (kein DLL-Hijacking, keine Fremd-DLLs).
            var sig = SignatureVerifier.Verify(path);
            if (sig.Status != SignatureStatus.Valid || sig.Publisher?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) != true)
            {
                AppLog.Warn("Gpu", "nvml.dll nicht geladen (Signatur nicht gültig oder nicht von NVIDIA)", sig.StatusText);
                return null;
            }

            IntPtr lib = IntPtr.Zero;
            try
            {
                lib = NativeLibrary.Load(path);
                var init = Marshal.GetDelegateForFunctionPointer<InitFn>(NativeLibrary.GetExport(lib, "nvmlInit_v2"));
                if (init() != 0) { NativeLibrary.Free(lib); return null; }
                return new Nvml(lib);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                if (lib != IntPtr.Zero) NativeLibrary.Free(lib);
                return null;
            }
        }

        public (int? Temp, int? Fan) ReadFirstGpu()
        {
            if (_getCount(out var count) != 0 || count == 0) return (null, null);
            if (_getHandle(0, out var dev) != 0) return (null, null);
            int? temp = _getTemp(dev, 0, out var t) == 0 ? (int)t : null;
            int? fan = _getFan is not null && _getFan(dev, out var f) == 0 ? (int)f : null;
            return (temp, fan);
        }

        public void Dispose()
        {
            try { _shutdown(); } catch (SEHException) { }
            NativeLibrary.Free(_lib);
        }
    }
}
