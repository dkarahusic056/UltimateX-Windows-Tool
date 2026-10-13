using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UltimateX.Services.Network;

namespace UltimateX.Services.Diagnostics;

public sealed partial class ComponentTestService
{
    private static readonly string[] VirtualMarkers = ["Virtual", "Hyper-V", "VMware", "VirtualBox", "TAP-", "Bluetooth", "WAN Miniport", "Loopback", "vEthernet", "Npcap", "WireGuard", "Tailscale", "ZeroTier"];

    private static bool IsPhysical(AdapterInfo a) => !VirtualMarkers.Any(m => a.Description.Contains(m, StringComparison.OrdinalIgnoreCase) || a.Name.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static string LinkText(long? bps) => bps is > 0 ? $"{bps.Value / 1_000_000:0} Mbit/s" : "–";

    private ComponentTestResult TestEthernet(ComponentTestDefinition d)
    {
        var lan = Context.Adapters.Value.Where(a => a.Kind == "LAN" && IsPhysical(a)).ToList();
        if (lan.Count == 0) return R(d, TestStatus.NotAvailable, "Kein physischer Ethernet-Adapter gefunden.");
        var details = new List<string>();
        foreach (var a in lan)
        {
            details.Add($"{a.Description}: {(a.IsUp ? "verbunden" : "kein Link")} · {LinkText(a.LinkSpeedBps)}");
            if (a.IsUp)
            {
                details.Add($"  IPv4: {(a.IPv4.Count == 0 ? "–" : string.Join(", ", a.IPv4))} · DHCP: {(a.DhcpEnabled ? "ja" : "nein (statisch)")}");
                details.Add($"  Gateway: {(a.Gateways.Count == 0 ? "–" : string.Join(", ", a.Gateways))} · DNS: {(a.DnsServers.Count == 0 ? "–" : string.Join(", ", a.DnsServers.Take(3)))}");
            }
        }
        var errors = PnpErrors("Net").Where(p => !VirtualMarkers.Any(m => p.Name.Contains(m, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var e in errors) details.Add($"Treiberfehler: {e.Name} – {e.ErrorText}");
        if (errors.Count > 0) return R(d, TestStatus.Fail, "Netzwerkadapter meldet einen Treiberfehler.", details);

        var up = lan.Where(a => a.IsUp).ToList();
        if (up.Count == 0) return R(d, TestStatus.Pass, "Adapter betriebsbereit, aktuell kein Kabel verbunden.", details);
        var noIp = up.Where(a => a.IPv4.Count == 0 || a.IPv4.All(ip => ip.StartsWith("169.254.", StringComparison.Ordinal))).ToList();
        if (noIp.Count > 0) return R(d, TestStatus.Warning, "Link vorhanden, aber keine gültige IPv4-Adresse (DHCP antwortet nicht?).", details);
        var slow = up.Where(a => a.LinkSpeedBps is > 0 and < 1_000_000_000).ToList();
        if (slow.Count > 0)
            return R(d, TestStatus.Warning, $"Verbunden, aber nur {LinkText(slow[0].LinkSpeedBps)} – Kabel (mind. Cat 5e) oder Port prüfen.", details);
        return R(d, TestStatus.Pass, $"Verbunden mit {LinkText(up[0].LinkSpeedBps)}.", details);
    }

    private ComponentTestResult TestWifi(ComponentTestDefinition d)
    {
        var (ifaces, state, message) = wlan.GetInfo();
        if (ifaces.Count == 0) return R(d, TestStatus.NotAvailable, message ?? "Kein WLAN-Adapter vorhanden.");
        var details = new List<string>();
        foreach (var w in ifaces)
        {
            details.Add($"{w.InterfaceName}: {(w.Connected ? "verbunden" : "nicht verbunden")}");
            if (w.Connected)
                details.Add($"  Signal {(w.SignalQuality is { } q ? q + " %" : "–")}{(w.RssiDbm is { } r ? $" ({r} dBm)" : "")}, {w.Band ?? "–"}, Kanal {w.Channel?.ToString() ?? "–"}, {w.Standard ?? "–"}, {w.RxMbps?.ToString("0") ?? "–"} Mbit/s");
            if (w.Note is not null) details.Add("  " + w.Note);
        }
        var errors = PnpErrors("Net").Where(p => p.Name.Contains("Wi", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("WLAN", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var e in errors) details.Add($"Treiberfehler: {e.Name} – {e.ErrorText}");
        if (errors.Count > 0) return R(d, TestStatus.Fail, "WLAN-Adapter meldet einen Treiberfehler.", details);
        var conn = ifaces.FirstOrDefault(w => w.Connected);
        if (conn is null) return R(d, TestStatus.Pass, "WLAN-Adapter betriebsbereit, nicht verbunden.", details);
        return conn.SignalQuality is < 40
            ? R(d, TestStatus.Warning, $"Verbunden, aber schwaches Signal ({conn.SignalQuality} %).", details)
            : R(d, TestStatus.Pass, $"Verbunden, Signal {(conn.SignalQuality is { } s ? s + " %" : "OK")}.", details);
    }

    private static async Task<ComponentTestResult> TestDnsAsync(ComponentTestDefinition d, CancellationToken ct)
    {
        string[] hosts = ["www.microsoft.com", "www.cloudflare.com"];
        var details = new List<string>();
        var ok = 0;
        var slow = false;
        foreach (var h in hosts)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
                t.CancelAfter(TimeSpan.FromSeconds(5));
                var a = await Dns.GetHostAddressesAsync(h, t.Token).ConfigureAwait(false);
                var ms = sw.Elapsed.TotalMilliseconds;
                details.Add($"{h} → {a.FirstOrDefault()} in {ms:0} ms");
                if (a.Length > 0) ok++;
                if (ms > 500) slow = true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                if (ct.IsCancellationRequested) throw;
                details.Add($"{h} → nicht auflösbar ({(ex is OperationCanceledException ? "Zeitüberschreitung" : ex.Message)})");
            }
        }
        if (ok == 0) return R(d, TestStatus.Fail, "Namensauflösung funktioniert nicht – DNS-Server oder Verbindung prüfen.", details);
        if (ok < hosts.Length || slow) return R(d, TestStatus.Warning, "DNS funktioniert eingeschränkt oder langsam.", details);
        return R(d, TestStatus.Pass, "Namensauflösung funktioniert.", details);
    }

    private async Task<ComponentTestResult> TestGatewayAsync(ComponentTestDefinition d, CancellationToken ct)
    {
        var gw = Context.Adapters.Value.Where(a => a.IsUp).SelectMany(a => a.Gateways)
            .FirstOrDefault(g => IPAddress.TryParse(g, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork)
            ?? Context.Adapters.Value.Where(a => a.IsUp).SelectMany(a => a.Gateways).FirstOrDefault();
        if (gw is null) return R(d, TestStatus.Fail, "Kein Standardgateway – keine Netzwerkverbindung.");
        var replies = new List<PingReplyInfo>();
        var eval = await LivePingService.RunAsync(gw, 4, replies.Add, 300, 1000, ct).ConfigureAwait(false);
        var details = new List<string> { $"Gateway: {gw}", $"Gesendet {eval.Sent}, empfangen {eval.Received}, Verlust {eval.LossPercent:0} %, Ø {eval.AvgMs?.ToString("0.#") ?? "–"} ms" };
        if (eval.Received == 0) return R(d, TestStatus.Warning, "Router antwortet nicht auf Ping (kann per Firewall blockiert sein).", details);
        return R(d, eval.State == Core.Common.DataState.Ok ? TestStatus.Pass : TestStatus.Warning, eval.StatusText, details);
    }

    private static async Task<ComponentTestResult> TestInternetAsync(ComponentTestDefinition d, CancellationToken ct)
    {
        var details = new List<string>();
        PingEvaluation? best = null;
        foreach (var target in new[] { "1.1.1.1", "8.8.8.8" })
        {
            var e = await LivePingService.RunAsync(target, 4, _ => { }, 300, 1500, ct).ConfigureAwait(false);
            details.Add($"{target}: {e.Received}/{e.Sent} Antworten, Ø {e.AvgMs?.ToString("0.#") ?? "–"} ms, Jitter {e.JitterMs?.ToString("0.#") ?? "–"} ms");
            if (best is null || e.Received > best.Received) best = e;
        }
        if (best is { Received: > 0 })
        {
            var status = best.LossPercent >= 20 ? TestStatus.Warning : best.AvgMs > 100 ? TestStatus.Warning : TestStatus.Pass;
            return R(d, status, status == TestStatus.Pass ? $"Internet erreichbar, Ø {best.AvgMs:0} ms." : "Internet erreichbar, aber mit Verlust oder hoher Latenz.", details);
        }
        // ICMP kann blockiert sein → TCP-Verbindung zu Port 443 als Gegenprobe.
        try
        {
            using var t = CancellationTokenSource.CreateLinkedTokenSource(ct);
            t.CancelAfter(TimeSpan.FromSeconds(4));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Parse("1.1.1.1"), 443, t.Token).ConfigureAwait(false);
            details.Add("TCP 443 zu 1.1.1.1: erreichbar (Ping wird im Netzwerk blockiert)");
            return R(d, TestStatus.Pass, "Internet erreichbar (Ping blockiert).", details);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            details.Add("TCP 443: keine Verbindung");
            return R(d, TestStatus.Fail, "Keine Internetverbindung.", details);
        }
    }
}
