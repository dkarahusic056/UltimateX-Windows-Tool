using System.Net;
using System.Net.Sockets;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Services.Benchmark;

namespace UltimateX.Services.Network;

public sealed record NetworkBenchmarkResult(NetworkMeasurements Measurements, NetworkHealthResult Health, string? SpeedtestServer, BenchmarkRun Run);

/// <summary>
/// NETWORK BENCHMARK (v1.2 §8): Download/Upload/Ping/Jitter/Paketverlust (Speedtest), DNS-Antwortzeit, Gateway-Ping,
/// Verbindungsgeschwindigkeit des Adapters, WLAN-Signal sowie IPv4-/IPv6-Konnektivität → NETWORK HEALTH SCORE.
/// Der Speedtest ist optional (überträgt Testdaten zu Cloudflare); ohne ihn fehlen Download/Upload in der Wertung.
/// </summary>
public sealed class NetworkBenchmarkService(NetworkService network, SpeedtestService speedtest, WlanService wlan, BenchmarkService history)
{
    public const string MetricHealthScore = "Network Health Score";

    public async Task<NetworkBenchmarkResult> RunAsync(bool includeSpeedtest, IProgress<BenchmarkProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new BenchmarkProgress("Netzwerkadapter", 2));
        var adapters = await Task.Run(network.GetAdapters, ct).ConfigureAwait(false);
        var active = adapters.Where(a => a.IsUp && a.Kind is "LAN" or "WLAN").ToList();
        var primary = active.FirstOrDefault(a => a.Gateways.Count > 0) ?? active.FirstOrDefault();
        double? link = primary?.LinkSpeedBps is > 0 ? primary.LinkSpeedBps.Value / 1e6 : null;

        progress?.Report(new BenchmarkProgress("Gateway-Ping", 8));
        double? gatewayMs = null;
        var gateway = primary?.Gateways.FirstOrDefault(g => IPAddress.TryParse(g, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork);
        if (gateway is not null)
        {
            var gp = await network.PingAsync(gateway, 10, 1000, ct).ConfigureAwait(false);
            if (gp.Received > 0) gatewayMs = gp.AvgMs;
        }

        progress?.Report(new BenchmarkProgress("DNS-Antwortzeit", 15));
        double? dnsMs = null;
        try
        {
            var dns = await network.TestDnsServersAsync("www.microsoft.com", ct).ConfigureAwait(false);
            dnsMs = dns.Where(d => d.Resolved && d.ResponseMs is not null).Select(d => d.ResponseMs).Min();
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            AppLog.Warn("NetworkBenchmark", "DNS-Test fehlgeschlagen", ex.Message);
        }

        progress?.Report(new BenchmarkProgress("IPv4 / IPv6", 22));
        var ipv4 = await CanConnectAsync(AddressFamily.InterNetwork, ct).ConfigureAwait(false);
        var ipv6 = await CanConnectAsync(AddressFamily.InterNetworkV6, ct).ConfigureAwait(false);

        int? wifiSignal = null;
        if (primary?.Kind == "WLAN")
        {
            var (ifs, _, _) = wlan.GetInfo();
            wifiSignal = ifs.FirstOrDefault(i => i.Connected)?.SignalQuality;
        }

        double? down = null, up = null, ping = null, jitter = null, loss = null;
        string? server = null;
        if (includeSpeedtest)
        {
            var sp = new Progress<SpeedtestProgress>(p => progress?.Report(new BenchmarkProgress(
                p.Phase switch { SpeedtestPhase.Latency => "Speedtest: Ping", SpeedtestPhase.Download => "Speedtest: Download", SpeedtestPhase.Upload => "Speedtest: Upload", _ => "Speedtest" },
                30 + 65 * (p.Phase switch { SpeedtestPhase.Latency => 0, SpeedtestPhase.Download => 0.1 + 0.45 * p.Fraction, SpeedtestPhase.Upload => 0.55 + 0.45 * p.Fraction, _ => 1 }))));
            var r = await speedtest.RunAsync(sp, ct).ConfigureAwait(false);
            down = r.DownloadBps / 1e6;
            up = r.UploadBps / 1e6;
            ping = r.PingMs;
            jitter = r.JitterMs;
            loss = r.PacketLossPercent;
            server = r.Server;
        }
        else
        {
            progress?.Report(new BenchmarkProgress("Ping / Jitter / Paketverlust", 40));
            var p = await network.PingAsync("1.1.1.1", 20, 1500, ct).ConfigureAwait(false);
            if (p.Received > 0) { ping = p.AvgMs; jitter = p.JitterMs; }
            loss = p.LossPercent;
        }

        var m = new NetworkMeasurements(down, up, ping, jitter, loss, dnsMs, gatewayMs, link, wifiSignal, ipv4, ipv6);
        var health = NetworkHealth.Evaluate(m);

        var metrics = new List<BenchmarkMetric>();
        void Add(string name, double? v, string unit) { if (v is { } x) metrics.Add(new BenchmarkMetric(name, Math.Round(x, 1), unit)); }
        Add("Network Health Score", health.Score, "Punkte");
        Add("Download", down, "Mbit/s");
        Add("Upload", up, "Mbit/s");
        Add("Ping", ping, "ms");
        Add("Jitter", jitter, "ms");
        Add("Paketverlust", loss, "%");
        Add("DNS-Antwortzeit", dnsMs, "ms");
        Add("Gateway-Ping", gatewayMs, "ms");
        Add("Verbindungsgeschwindigkeit", link, "Mbit/s");
        Add("WLAN-Signal", wifiSignal, "%");
        // Nur mit echtem Wert – ohne Messdaten bleibt der Score N/A (der Lauf-Score 0 ist dann kein Messwert).
        if (health.Score is { } hs) metrics.Add(new BenchmarkMetric(MetricHealthScore, hs, "/100"));
        var note = $"IPv4: {(ipv4 ? "OK" : "keine Verbindung")} · IPv6: {(ipv6 ? "OK" : "nicht verfügbar")}"
                   + (includeSpeedtest ? $" · Speedtest: {server}" : " · ohne Speedtest (Download/Upload N/A)");
        var run = history.Record(new BenchmarkRun(BenchmarkService.KindNetwork, DateTime.Now, health.Score ?? 0, metrics, note));
        progress?.Report(new BenchmarkProgress("Fertig", 100));
        return new NetworkBenchmarkResult(m, health, server, run);
    }

    /// <summary>Prüft eine HTTPS-Verbindung (TCP 443) zu Cloudflare über IPv4 bzw. IPv6 – ohne Daten zu senden.</summary>
    private static async Task<bool> CanConnectAsync(AddressFamily family, CancellationToken ct)
    {
        var target = family == AddressFamily.InterNetwork ? IPAddress.Parse("1.1.1.1") : IPAddress.Parse("2606:4700:4700::1111");
        try
        {
            using var socket = new Socket(family, SocketType.Stream, ProtocolType.Tcp);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await socket.ConnectAsync(new IPEndPoint(target, 443), cts.Token).ConfigureAwait(false);
            return socket.Connected;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return false;
        }
    }
}
