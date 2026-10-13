using System.Diagnostics.Eventing.Reader;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace UltimateX.Services.Diagnostics;

/// <summary>Ein gefundenes Ereignis (nur Metadaten + gekürzte Meldung).</summary>
public sealed record EventHit(DateTime? Time, long Id, string Provider, int Level, string? Message);

/// <summary>Lesende Ereignisprotokoll-Abfragen für die Komponententests. XPath ist immer eine Code-Konstante.</summary>
public static class EventProbe
{
    /// <summary>
    /// Liest passende Ereignisse der letzten <paramref name="days"/> Tage. <paramref name="condition"/> ist ein
    /// fester XPath-Ausdruck aus dem Code (z. B. "Provider[@Name='disk'] and (EventID=7 or EventID=51)").
    /// Liefert null, wenn das Protokoll nicht lesbar ist.
    /// </summary>
    public static List<EventHit>? Query(string log, string condition, int days, int max = 200, int withMessages = 0)
    {
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        var xpath = $"*[System[{condition} and TimeCreated[timediff(@SystemTime) <= {ms}]]]";
        var list = new List<EventHit>();
        try
        {
            var q = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(q);
            for (var i = 0; i < max; i++)
            {
                using var rec = reader.ReadEvent();
                if (rec is null) break;
                string? msg = null;
                if (i < withMessages)
                {
                    try { msg = rec.FormatDescription(); }
                    catch (EventLogException) { }
                    if (msg is { Length: > 240 }) msg = msg[..240] + " …";
                }
                list.Add(new EventHit(rec.TimeCreated, rec.Id, rec.ProviderName ?? "?", rec.Level ?? 0, msg));
            }
            return list;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Minimaler SNTP-Client (RFC 4330) zur Prüfung der Systemzeit. Nur lesend: Es wird keine Uhrzeit gesetzt.
/// Server sind feste Konstanten (time.windows.com, pool.ntp.org).
/// </summary>
public static class SntpClient
{
    public static readonly string[] Servers = ["time.windows.com", "pool.ntp.org"];

    /// <summary>Abweichung lokale Uhr − Serverzeit (positiv = lokale Uhr geht vor). null, wenn kein Server antwortet.</summary>
    public static async Task<(TimeSpan Offset, string Server)?> QueryOffsetAsync(CancellationToken ct)
    {
        foreach (var server in Servers)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                var addresses = await Dns.GetHostAddressesAsync(server, AddressFamily.InterNetwork, timeout.Token).ConfigureAwait(false);
                if (addresses.Length == 0) continue;
                using var udp = new UdpClient(AddressFamily.InterNetwork);
                var request = new byte[48];
                request[0] = 0x1B; // LI=0, VN=3, Mode=3 (Client)
                var t1 = DateTime.UtcNow;
                await udp.SendAsync(request, new IPEndPoint(addresses[0], 123), timeout.Token).ConfigureAwait(false);
                var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                var t4 = DateTime.UtcNow;
                if (response.Buffer.Length < 48 || (response.Buffer[0] & 0x07) != 4) continue; // Mode 4 = Server
                var t2 = ReadTimestamp(response.Buffer, 32);
                var t3 = ReadTimestamp(response.Buffer, 40);
                if (t2 is null || t3 is null) continue;
                // Offset θ = ((T2 − T1) + (T3 − T4)) / 2 → Serverzeit − lokale Zeit
                var theta = ((t2.Value - t1) + (t3.Value - t4)) / 2;
                return (-theta, server);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
            {
                if (ct.IsCancellationRequested) throw;
            }
        }
        return null;
    }

    private static DateTime? ReadTimestamp(byte[] b, int offset)
    {
        ulong seconds = (ulong)b[offset] << 24 | (ulong)b[offset + 1] << 16 | (ulong)b[offset + 2] << 8 | b[offset + 3];
        ulong fraction = (ulong)b[offset + 4] << 24 | (ulong)b[offset + 5] << 16 | (ulong)b[offset + 6] << 8 | b[offset + 7];
        if (seconds == 0) return null;
        var ms = seconds * 1000 + fraction * 1000 / 0x1_0000_0000UL;
        return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
    }
}

/// <summary>WDDM-Version der Grafiktreiber über den Windows-Grafikkernel (D3DKMT, ohne Adminrechte).</summary>
public static class WddmInfo
{
    private const int KMTQAITYPE_DRIVERVERSION = 13;
    private const int KMTQAITYPE_ADAPTERREGISTRYINFO = 8;

    [StructLayout(LayoutKind.Sequential)] private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ADAPTERINFO { public uint hAdapter; public LUID AdapterLuid; public uint NumOfSources; public int bPrecisePresentRegionsPreferred; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_ENUMADAPTERS2 { public uint NumAdapters; public IntPtr pAdapters; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_QUERYADAPTERINFO { public uint hAdapter; public int Type; public IntPtr pPrivateDriverData; public uint PrivateDriverDataSize; }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DKMT_CLOSEADAPTER { public uint hAdapter; }

    [DllImport("gdi32.dll")] private static extern int D3DKMTEnumAdapters2(ref D3DKMT_ENUMADAPTERS2 p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTQueryAdapterInfo(ref D3DKMT_QUERYADAPTERINFO p);
    [DllImport("gdi32.dll")] private static extern int D3DKMTCloseAdapter(ref D3DKMT_CLOSEADAPTER p);

    /// <summary>Liefert je Adapter (Name aus der Registry des Treibers) die WDDM-Version, z. B. „WDDM 3.1“.</summary>
    public static IReadOnlyList<(string Adapter, string Wddm)> Read()
    {
        var result = new List<(string, string)>();
        var e = new D3DKMT_ENUMADAPTERS2();
        try
        {
            if (D3DKMTEnumAdapters2(ref e) != 0 || e.NumAdapters == 0 || e.NumAdapters > 64) return result;
        }
        catch (EntryPointNotFoundException) { return result; }
        catch (DllNotFoundException) { return result; }

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
                    var name = QueryName(info.hAdapter);
                    var ver = QueryInt(info.hAdapter, KMTQAITYPE_DRIVERVERSION);
                    if (ver is > 0 && name is not null && !name.Contains("Basic Render", StringComparison.OrdinalIgnoreCase))
                        result.Add((name, $"WDDM {ver.Value / 1000}.{ver.Value % 1000 / 100}"));
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
        return result.Distinct().ToList();
    }

    private static int? QueryInt(uint handle, int type)
    {
        var ptr = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(ptr, 0);
            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = handle, Type = type, pPrivateDriverData = ptr, PrivateDriverDataSize = 4 };
            return D3DKMTQueryAdapterInfo(ref q) == 0 ? Marshal.ReadInt32(ptr) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static string? QueryName(uint handle)
    {
        const int size = 260 * 2 * 4;
        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            for (var i = 0; i < size; i++) Marshal.WriteByte(ptr, i, 0);
            var q = new D3DKMT_QUERYADAPTERINFO { hAdapter = handle, Type = KMTQAITYPE_ADAPTERREGISTRYINFO, pPrivateDriverData = ptr, PrivateDriverDataSize = size };
            if (D3DKMTQueryAdapterInfo(ref q) != 0) return null;
            return Marshal.PtrToStringUni(ptr)?.Trim() is { Length: > 0 } s ? s : null;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
