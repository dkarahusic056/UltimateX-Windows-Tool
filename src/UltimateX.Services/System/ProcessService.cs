using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using UltimateX.Core.Security;

namespace UltimateX.Services.SystemTools;

public sealed record ProcessInfo(
    int Pid,
    string Name,
    string? Path,
    double CpuPercent,
    long WorkingSetBytes,
    SignatureInfo? Signature);

/// <summary>
/// Prozessliste mit Signaturstatus (§77). Nur lesend – Prozesse werden nicht beendet.
/// Unbekannte oder unsignierte Prozesse werden NICHT als Malware bezeichnet; die Liste zeigt nur Fakten.
/// Signaturen werden je Pfad zwischengespeichert, damit die Abfrage ressourcenschonend bleibt (§89).
/// </summary>
public sealed class ProcessService
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> _prevCpu = new();
    private readonly Dictionary<string, SignatureInfo> _sigCache = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ProcessInfo> Snapshot(bool verifySignatures)
    {
        var now = DateTime.UtcNow;
        var cores = Environment.ProcessorCount;
        var list = new List<ProcessInfo>();
        var alive = new HashSet<int>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                int pid;
                try { pid = p.Id; } catch (InvalidOperationException) { continue; }
                if (pid == 0) continue; // System Idle Process
                alive.Add(pid);

                string name;
                try { name = p.ProcessName; } catch (InvalidOperationException) { continue; }

                double cpu = 0;
                try
                {
                    var total = p.TotalProcessorTime;
                    if (_prevCpu.TryGetValue(pid, out var prev))
                    {
                        var dt = (now - prev.At).TotalMilliseconds;
                        if (dt > 0) cpu = Math.Clamp((total - prev.Cpu).TotalMilliseconds / dt / cores * 100, 0, 100);
                    }
                    _prevCpu[pid] = (total, now);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Geschützter Prozess – CPU-Zeit nicht lesbar.
                }

                long ws = 0;
                try { ws = p.WorkingSet64; } catch (InvalidOperationException) { }

                var path = GetPath(pid);
                SignatureInfo? sig = null;
                if (verifySignatures && path is not null && !_sigCache.TryGetValue(path, out sig))
                {
                    sig = SignatureVerifier.Verify(path);
                    _sigCache[path] = sig;
                }
                else if (path is not null)
                {
                    _sigCache.TryGetValue(path, out sig);
                }

                list.Add(new ProcessInfo(pid, name, path, cpu, ws, sig));
            }
        }

        foreach (var gone in _prevCpu.Keys.Where(k => !alive.Contains(k)).ToList()) _prevCpu.Remove(gone);
        return list.OrderByDescending(x => x.CpuPercent).ThenByDescending(x => x.WorkingSetBytes).ToList();
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint flags, StringBuilder exeName, ref uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr h);

    /// <summary>Pfad über QueryFullProcessImageName – funktioniert auch für viele Prozesse ohne Adminrechte.</summary>
    private static string? GetPath(int pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }
}
