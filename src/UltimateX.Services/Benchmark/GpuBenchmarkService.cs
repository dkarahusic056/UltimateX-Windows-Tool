using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Benchmark;

/// <summary>Grafikkarte, wie DXGI sie meldet (für Benchmark und „DirectX“-Prüfung).</summary>
public sealed record GpuAdapterInfo(string Name, uint VendorId, ulong DedicatedVideoMemory, string FeatureLevel, int DirectXVersion);

public sealed record GpuStabilityResult(int Checks, long Mismatches, double Seconds)
{
    public bool Passed => Checks > 0 && Mismatches == 0;
}

/// <summary>
/// GPU-Benchmark (v1.2 §3) über Direct3D 11 Compute Shader – ohne Zusatzsoftware:
///  • Compute: FP32-FMA-Ketten (gemessene GFLOPS)
///  • Speicherbandbreite: Kopieren zwischen zwei großen GPU-Puffern (GB/s)
///  • Stabilität: wiederholte, deterministische Berechnung; Ergebnisse werden bitgenau verglichen
/// Takt, Temperatur, Hotspot, Lüfter, Leistungsaufnahme und VRAM-Verwendung stammen aus den Sensoren (sofern vorhanden).
/// Ein 3D-/Spiele-Rendertest (FPS, 1 % Low) ist NICHT enthalten – diese Werte werden als N/A geführt.
/// </summary>
public static unsafe class GpuBenchmarkService
{
    private const int S_OK = 0;
    private const int S_FALSE = 1;
    private const int DXGI_ERROR_NOT_FOUND = unchecked((int)0x887A0002);
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);

    // ------------------------------------------------------------------ Shader (HLSL, cs_5_0)

    private const int ComputeIterations = 2048;
    private const int ComputeGroups = 4096; // × 256 Threads = 1.048.576 Threads je Dispatch
    private const double FlopsPerThread = 8.0 * 2 * ComputeIterations; // 8 FMA je Schleife, FMA = 2 FLOP

    private static readonly string ComputeShader = $$"""
        RWStructuredBuffer<float> o : register(u0);
        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            float a = id.x * 1e-7f, b = 0.5f + a, c = 0.25f, d = 0.125f;
            [loop] for (uint i = 0; i < {{ComputeIterations}}u; i++)
            {
                a = mad(a, 0.999f, 0.0011f); b = mad(b, 0.998f, 0.0021f);
                c = mad(c, 0.997f, 0.0031f); d = mad(d, 0.996f, 0.0041f);
                a = mad(a, 0.9991f, 0.0012f); b = mad(b, 0.9981f, 0.0022f);
                c = mad(c, 0.9971f, 0.0032f); d = mad(d, 0.9961f, 0.0042f);
            }
            o[id.x] = a + b + c + d;
        }
        """;

    private static string CopyShader(uint totalThreads) => $$"""
        RWStructuredBuffer<float4> src : register(u0);
        RWStructuredBuffer<float4> dst : register(u1);
        [numthreads(256, 1, 1)]
        void main(uint3 id : SV_DispatchThreadID)
        {
            [unroll] for (uint k = 0; k < 4; k++)
            {
                uint i = id.x + k * {{totalThreads}}u;
                dst[i] = src[i] * 1.0001f;
            }
        }
        """;

    // ------------------------------------------------------------------ Öffentliche API

    /// <summary>Ermittelt die leistungsstärkste Grafikkarte (meiste dedizierte VRAM) und ihr Direct3D-Feature-Level.</summary>
    public static GpuAdapterInfo? DetectAdapter()
    {
        try
        {
            using var d = D3D.Create();
            return d.Info;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException or COMException)
        {
            AppLog.Warn("GPU", "Direct3D-Gerät nicht verfügbar", ex.Message);
            return null;
        }
    }

    /// <summary>Führt den GPU-Benchmark aus. <paramref name="stabilitySeconds"/> = 0 → ohne Stabilitätstest.</summary>
    public static BenchmarkRun Run(BenchmarkService history, int stabilitySeconds, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        using var monitor = LoadMonitor.Start();
        var watch = Stopwatch.StartNew();
        using var d = D3D.Create();

        progress?.Report(new BenchmarkProgress("Shader werden kompiliert", 3));
        // Alle COM-Objekte starten als Zero und werden im finally freigegeben – auch bei Abbruch, Zeitüberschreitung
        // oder „Gerät entfernt“, sonst bliebe das komplette D3D11-Gerät samt Puffern belegt.
        IntPtr compute = IntPtr.Zero, output = IntPtr.Zero, outputUav = IntPtr.Zero;
        IntPtr copy = IntPtr.Zero, src = IntPtr.Zero, dst = IntPtr.Zero, srcUav = IntPtr.Zero, dstUav = IntPtr.Zero;
        double gflops;
        double? bandwidth = null;
        GpuStabilityResult? stability = null;
        try
        {
            compute = d.CreateComputeShader(ComputeShader);
            var threads = (long)ComputeGroups * 256;
            output = d.CreateStructuredBuffer(threads * 4, 4);
            outputUav = d.CreateUav(output);

            // Aufwärmen (Taktanstieg, Treiber-Kompilierung), dann Messung über ~4 s.
            progress?.Report(new BenchmarkProgress("GPU Compute – Aufwärmen", 8));
            d.RunBatch(compute, [outputUav], ComputeGroups, 3);
            var perDispatch = d.TimeBatch(compute, [outputUav], ComputeGroups, 2) / 2;
            var batch = Math.Clamp((int)(0.25 / Math.Max(perDispatch, 1e-5)), 1, 2000);
            var flops = 0.0;
            var seconds = 0.0;
            while (seconds < 4)
            {
                ct.ThrowIfCancellationRequested();
                seconds += d.TimeBatch(compute, [outputUav], ComputeGroups, batch);
                flops += FlopsPerThread * threads * batch;
                progress?.Report(new BenchmarkProgress("GPU Compute (FP32)", 10 + 35 * Math.Min(1, seconds / 4)));
            }
            gflops = flops / seconds / 1e9;
            D3D.Release(outputUav);
            outputUav = IntPtr.Zero;

            // Speicherbandbreite: 2 × Puffer (je bis zu 256 MB), Größe an den VRAM angepasst.
            var vramMb = d.Info.DedicatedVideoMemory / (1024 * 1024);
            if (vramMb >= 1024)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new BenchmarkProgress("Speicherbandbreite", 50));
                uint totalThreads = vramMb >= 3072 ? 4_194_304u : 1_048_576u; // 256 MB bzw. 64 MB je Puffer
                var elements = (long)totalThreads * 4;
                var bytes = elements * 16;
                copy = d.CreateComputeShader(CopyShader(totalThreads));
                src = d.CreateStructuredBuffer(bytes, 16);
                dst = d.CreateStructuredBuffer(bytes, 16);
                srcUav = d.CreateUav(src);
                dstUav = d.CreateUav(dst);
                var groups = (int)(totalThreads / 256);
                d.RunBatch(copy, [srcUav, dstUav], groups, 2);
                var moved = 0.0;
                var bwSeconds = 0.0;
                while (bwSeconds < 2)
                {
                    ct.ThrowIfCancellationRequested();
                    bwSeconds += d.TimeBatch(copy, [srcUav, dstUav], groups, 10);
                    moved += 2.0 * bytes * 10;
                    progress?.Report(new BenchmarkProgress("Speicherbandbreite", 50 + 15 * Math.Min(1, bwSeconds / 2)));
                }
                bandwidth = moved / bwSeconds / 1e9;
            }

            if (stabilitySeconds > 0)
            {
                progress?.Report(new BenchmarkProgress($"Stabilität ({stabilitySeconds} s Dauerlast)", 66));
                stability = d.Stability(compute, output, ComputeGroups, stabilitySeconds, ct,
                    f => progress?.Report(new BenchmarkProgress($"Stabilität ({stabilitySeconds} s Dauerlast)", 66 + 32 * f)));
            }
        }
        finally
        {
            foreach (var h in new[] { dstUav, srcUav, dst, src, copy, outputUav, output, compute }) D3D.Release(h);
        }

        var telemetry = monitor.Stop();
        watch.Stop();
        var metrics = new List<BenchmarkMetric>
        {
            new("GPU Compute (FP32)", Math.Round(gflops), "GFLOPS"),
        };
        if (bandwidth is { } bw) metrics.Add(new BenchmarkMetric("Speicherbandbreite", Math.Round(bw, 1), "GB/s"));
        if (stability is not null)
        {
            metrics.Add(new BenchmarkMetric("Stabilität: Prüfungen", stability.Checks, ""));
            metrics.Add(new BenchmarkMetric("Stabilität: Abweichungen", stability.Mismatches, ""));
        }
        BenchmarkService.AddTelemetry(metrics, telemetry, cpu: false);
        metrics.Add(new BenchmarkMetric("Testdauer", Math.Round(watch.Elapsed.TotalSeconds), "s"));

        var score = Math.Round(gflops); // Punktzahl des GPU-Tests = gemessene GFLOPS
        var note = new StringBuilder()
            .Append(d.Info.Name).Append(" · ").Append(d.Info.FeatureLevel)
            .Append(" · Thermal Throttling: ").Append(LoadMonitor.ThrottleText(telemetry.GpuThrottled))
            .Append(stability is null ? "" : stability.Passed ? " · Stabilität: PASS" : $" · Stabilität: FAIL ({stability.Mismatches} Abweichungen)")
            .Append(" · FPS / 1 % Low: N/A (kein 3D-Rendertest)")
            .ToString();
        progress?.Report(new BenchmarkProgress("Fertig", 100));
        return history.Record(new BenchmarkRun(BenchmarkService.KindGpu, DateTime.Now, score, metrics, note, telemetry));
    }

    // ------------------------------------------------------------------ Direct3D 11 (COM über Funktionszeiger)

    [StructLayout(LayoutKind.Sequential)]
    private struct BufferDesc
    {
        public uint ByteWidth;
        public int Usage;
        public uint BindFlags;
        public uint CpuAccessFlags;
        public uint MiscFlags;
        public uint StructureByteStride;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryDesc
    {
        public int Query;
        public uint MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MappedSubresource
    {
        public void* Data;
        public uint RowPitch;
        public uint DepthPitch;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public long AdapterLuid;
        public uint Flags;
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, int* featureLevels, uint numLevels,
        uint sdkVersion, IntPtr* device, int* featureLevel, IntPtr* context);

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(Guid* riid, IntPtr* factory);

    [DllImport("d3dcompiler_47.dll", ExactSpelling = true)]
    private static extern int D3DCompile(byte* src, nuint srcSize, byte* sourceName, IntPtr defines, IntPtr include, byte* entry, byte* target,
        uint flags1, uint flags2, IntPtr* code, IntPtr* errors);

    private sealed class D3D : IDisposable
    {
        private IntPtr _device, _context, _query;
        public GpuAdapterInfo Info { get; private set; } = null!;

        private static void* Vt(IntPtr obj, int index) => (*(void***)obj)[index];

        public static void Release(IntPtr obj)
        {
            if (obj != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Vt(obj, 2))(obj);
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0)
                throw new InvalidOperationException(hr == DXGI_ERROR_DEVICE_REMOVED
                    ? "Der Grafiktreiber hat den Test abgebrochen (Gerät entfernt/zurückgesetzt). Das kann auf Instabilität hinweisen."
                    : $"{what} fehlgeschlagen (HRESULT 0x{hr:X8}).");
        }

        public static D3D Create()
        {
            var d = new D3D();
            try
            {
                var (adapter, name, vendor, vram) = PickAdapter();
                int* levels = stackalloc int[] { 0xC100, 0xC000, 0xB100, 0xB000 };
                IntPtr dev, ctx;
                int fl;
                // Mit explizitem Adapter muss der Treibertyp UNKNOWN (0) sein, sonst HARDWARE (1).
                var hr = D3D11CreateDevice(adapter, adapter == IntPtr.Zero ? 1 : 0, IntPtr.Zero, 0, levels, 4, 7, &dev, &fl, &ctx);
                if (hr < 0) hr = D3D11CreateDevice(adapter, adapter == IntPtr.Zero ? 1 : 0, IntPtr.Zero, 0, levels + 2, 2, 7, &dev, &fl, &ctx);
                Release(adapter);
                Check(hr, "Direct3D-11-Gerät erstellen");
                d._device = dev;
                d._context = ctx;
                var flText = $"Feature Level {(fl >> 12) & 0xF}_{(fl >> 8) & 0xF}";
                d.Info = new GpuAdapterInfo(name ?? "Grafikkarte", vendor, vram, flText, fl >= 0xC000 ? 12 : 11);

                var q = new QueryDesc { Query = 0 /* EVENT */ };
                IntPtr query;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, QueryDesc*, IntPtr*, int>)Vt(d._device, 24))(d._device, &q, &query), "Abfrage erstellen");
                d._query = query;
                return d;
            }
            catch
            {
                d.Dispose();
                throw;
            }
        }

        /// <summary>Hardware-Adapter mit dem meisten dedizierten Grafikspeicher (keine Software-/Basic-Render-Adapter).</summary>
        private static (IntPtr Adapter, string? Name, uint Vendor, ulong Vram) PickAdapter()
        {
            var iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387"); // IDXGIFactory1
            IntPtr factory;
            if (CreateDXGIFactory1(&iid, &factory) < 0) return (IntPtr.Zero, null, 0, 0);
            try
            {
                IntPtr best = IntPtr.Zero;
                string? bestName = null;
                uint bestVendor = 0;
                ulong bestVram = 0;
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter;
                    var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Vt(factory, 12))(factory, i, &adapter); // EnumAdapters1
                    if (hr == DXGI_ERROR_NOT_FOUND || hr < 0) break;
                    var desc = default(AdapterDesc1);
                    var size = Marshal.SizeOf<AdapterDesc1>();
                    var raw = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Vt(adapter, 10))(adapter, raw) >= 0) // GetDesc1
                            desc = Marshal.PtrToStructure<AdapterDesc1>(raw);
                    }
                    finally { Marshal.FreeHGlobal(raw); }
                    var software = (desc.Flags & 2) != 0 || desc.VendorId == 0x1414; // Microsoft Basic Render Driver
                    if (!software && (ulong)desc.DedicatedVideoMemory >= bestVram)
                    {
                        Release(best);
                        best = adapter;
                        bestName = desc.Description?.Trim();
                        bestVendor = desc.VendorId;
                        bestVram = desc.DedicatedVideoMemory;
                    }
                    else Release(adapter);
                }
                return (best, bestName, bestVendor, bestVram);
            }
            finally { Release(factory); }
        }

        public IntPtr CreateComputeShader(string hlsl)
        {
            var src = Encoding.ASCII.GetBytes(hlsl);
            var entry = "main\0"u8.ToArray();
            var target = "cs_5_0\0"u8.ToArray();
            var name = "ultimatex\0"u8.ToArray();
            IntPtr code, errors;
            int hr;
            fixed (byte* s = src)
            fixed (byte* e = entry)
            fixed (byte* t = target)
            fixed (byte* n = name)
                hr = D3DCompile(s, (nuint)src.Length, n, IntPtr.Zero, IntPtr.Zero, e, t, 1u << 15 /* OPTIMIZATION_LEVEL3 */, 0, &code, &errors);
            if (hr < 0)
            {
                var msg = errors != IntPtr.Zero
                    ? Marshal.PtrToStringAnsi((IntPtr)((delegate* unmanaged[Stdcall]<IntPtr, void*>)Vt(errors, 3))(errors)) : null;
                Release(errors);
                throw new InvalidOperationException("Shader konnte nicht kompiliert werden: " + (msg ?? $"0x{hr:X8}"));
            }
            Release(errors);
            try
            {
                var ptr = ((delegate* unmanaged[Stdcall]<IntPtr, void*>)Vt(code, 3))(code);
                var len = ((delegate* unmanaged[Stdcall]<IntPtr, nuint>)Vt(code, 4))(code);
                IntPtr shader;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, void*, nuint, IntPtr, IntPtr*, int>)Vt(_device, 18))(_device, ptr, len, IntPtr.Zero, &shader),
                    "Compute Shader erstellen");
                return shader;
            }
            finally { Release(code); }
        }

        public IntPtr CreateStructuredBuffer(long bytes, uint stride, bool staging = false)
        {
            var desc = new BufferDesc
            {
                ByteWidth = checked((uint)bytes),
                Usage = staging ? 3 /* STAGING */ : 0 /* DEFAULT */,
                BindFlags = staging ? 0u : 0x80u /* UNORDERED_ACCESS */,
                CpuAccessFlags = staging ? 0x20000u /* READ */ : 0u,
                MiscFlags = 0x40 /* BUFFER_STRUCTURED */,
                StructureByteStride = stride,
            };
            IntPtr buffer;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, BufferDesc*, IntPtr, IntPtr*, int>)Vt(_device, 3))(_device, &desc, IntPtr.Zero, &buffer),
                "GPU-Puffer anlegen");
            return buffer;
        }

        public IntPtr CreateUav(IntPtr buffer)
        {
            IntPtr uav;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Vt(_device, 8))(_device, buffer, IntPtr.Zero, &uav), "UAV anlegen");
            return uav;
        }

        private void Bind(IntPtr shader, IntPtr[] uavs)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, uint, void>)Vt(_context, 69))(_context, shader, null, 0); // CSSetShader
            fixed (IntPtr* p = uavs)
                ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, uint*, void>)Vt(_context, 68))(_context, 0, (uint)uavs.Length, p, null);
        }

        private void Unbind(int count)
        {
            var empty = stackalloc IntPtr[count];
            ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, IntPtr*, uint*, void>)Vt(_context, 68))(_context, 0, (uint)count, empty, null);
        }

        private void Dispatch(int groups)
            => ((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, void>)Vt(_context, 41))(_context, (uint)groups, 1, 1);

        /// <summary>Wartet, bis die GPU alle bisherigen Befehle abgearbeitet hat (Event-Abfrage).</summary>
        private void WaitIdle()
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)Vt(_context, 28))(_context, _query); // End
            var sw = Stopwatch.StartNew();
            int done;
            while (true)
            {
                var hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void*, uint, uint, int>)Vt(_context, 29))(_context, _query, &done, 4, 0); // GetData
                if (hr == S_OK) return;
                if (hr != S_FALSE) Check(hr, "GPU-Synchronisierung");
                if (sw.Elapsed > TimeSpan.FromSeconds(20)) throw new TimeoutException("Die GPU antwortet nicht (Zeitüberschreitung).");
                Thread.Yield();
            }
        }

        public void RunBatch(IntPtr shader, IntPtr[] uavs, int groups, int count)
        {
            Bind(shader, uavs);
            for (var i = 0; i < count; i++) Dispatch(groups);
            WaitIdle();
            Unbind(uavs.Length);
        }

        /// <summary>Dauer (s) für <paramref name="count"/> Dispatches inkl. Synchronisierung.</summary>
        public double TimeBatch(IntPtr shader, IntPtr[] uavs, int groups, int count)
        {
            Bind(shader, uavs);
            WaitIdle();
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < count; i++) Dispatch(groups);
            WaitIdle();
            sw.Stop();
            Unbind(uavs.Length);
            return sw.Elapsed.TotalSeconds;
        }

        private float[] Read(IntPtr buffer, IntPtr staging, int floats)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Vt(_context, 47))(_context, staging, buffer); // CopyResource
            MappedSubresource map;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, int, uint, MappedSubresource*, int>)Vt(_context, 14))(_context, staging, 0, 1, 0, &map),
                "GPU-Puffer lesen");
            try
            {
                var result = new float[floats];
                new ReadOnlySpan<float>(map.Data, floats).CopyTo(result);
                return result;
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Vt(_context, 15))(_context, staging, 0); // Unmap
            }
        }

        /// <summary>Wiederholte deterministische Berechnung: jede Wiederholung muss bitgenau dasselbe Ergebnis liefern.</summary>
        public GpuStabilityResult Stability(IntPtr shader, IntPtr output, int groups, int seconds, CancellationToken ct, Action<double> progress)
        {
            var floats = groups * 256;
            IntPtr staging = IntPtr.Zero, uav = IntPtr.Zero;
            try
            {
                staging = CreateStructuredBuffer((long)floats * 4, 4, staging: true);
                uav = CreateUav(output);
                RunBatch(shader, [uav], groups, 1);
                var reference = Read(output, staging, floats);
                var checks = 0;
                long mismatches = 0;
                var sw = Stopwatch.StartNew();
                while (sw.Elapsed.TotalSeconds < seconds)
                {
                    ct.ThrowIfCancellationRequested();
                    RunBatch(shader, [uav], groups, 20);
                    var now = Read(output, staging, floats);
                    for (var i = 0; i < floats; i++)
                        if (BitConverter.SingleToInt32Bits(now[i]) != BitConverter.SingleToInt32Bits(reference[i])) mismatches++;
                    checks++;
                    progress(Math.Min(1, sw.Elapsed.TotalSeconds / seconds));
                }
                return new GpuStabilityResult(checks, mismatches, sw.Elapsed.TotalSeconds);
            }
            finally
            {
                Release(uav);
                Release(staging);
            }
        }

        public void Dispose()
        {
            if (_context != IntPtr.Zero)
                ((delegate* unmanaged[Stdcall]<IntPtr, void>)Vt(_context, 110))(_context); // ClearState
            Release(_query);
            Release(_context);
            Release(_device);
            _query = _context = _device = IntPtr.Zero;
        }
    }
}
