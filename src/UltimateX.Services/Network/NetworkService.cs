using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Services.Network;

/// <summary>
/// Network Center (§24–§29). Nutzt ausschließlich System.Net.NetworkInformation, Ping und eigene DNS-Abfragen.
/// Öffentliche IP wird nur auf ausdrückliche Benutzeraktion abgefragt (§24, §106).
/// </summary>
public sealed class NetworkService
{
    /// <summary>
    /// Vertrauenswürdiger Dienst für die öffentliche IP (HTTPS, nur IP als Antwort, keine Cookies).
    /// Austauschbar – siehe docs/DEPENDENCIES.md "Externe Dienste".
    /// </summary>
    public const string PublicIpV4Url = "https://api.ipify.org";
    public const string PublicIpV6Url = "https://api6.ipify.org";

    public IReadOnlyList<AdapterInfo> GetAdapters()
    {
        var list = new List<AdapterInfo>();
        NetworkInterface[] all;
        try { all = NetworkInterface.GetAllNetworkInterfaces(); }
        catch (NetworkInformationException ex)
        {
            AppLog.Warn("Network", "Netzwerkadapter konnten nicht gelesen werden", ex.Message);
            return list;
        }

        foreach (var ni in all)
        {
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            var v4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork).ToList();
            var mac = ni.GetPhysicalAddress().GetAddressBytes();
            bool dhcp = false;
            try { dhcp = props.GetIPv4Properties()?.IsDhcpEnabled ?? false; }
            catch (NetworkInformationException) { }

            list.Add(new AdapterInfo(
                ni.Name,
                ni.Description,
                ni.NetworkInterfaceType switch
                {
                    NetworkInterfaceType.Wireless80211 => "WLAN",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "LAN",
                    NetworkInterfaceType.Ppp => "PPP",
                    _ => ni.NetworkInterfaceType.ToString(),
                },
                ni.OperationalStatus == OperationalStatus.Up,
                ni.Speed > 0 ? ni.Speed : null,
                mac.Length == 6 ? string.Join('-', mac.Select(b => b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture))) : null,
                v4.Select(a => a.Address.ToString()).ToList(),
                props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6).Select(a => a.Address.ToString()).ToList(),
                v4.Select(a => a.IPv4Mask?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
                props.GatewayAddresses.Select(g => g.Address).Where(a => !a.Equals(IPAddress.Any)).Select(a => a.ToString()).ToList(),
                props.DnsAddresses.Select(a => a.ToString()).ToList(),
                dhcp,
                props.DhcpServerAddresses.Select(a => a.ToString()).ToList()));
        }

        // Aktive Adapter zuerst
        return list.OrderByDescending(a => a.IsUp).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Ping-Tool (§28). Ziel wird streng validiert; es wird kein externes ping.exe verwendet.</summary>
    public async Task<PingStatistics> PingAsync(string target, int count = 10, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (!InputValidator.TryNormalizeHost(target, out var host))
            throw new ArgumentException("Ungültige Adresse oder ungültiger Hostname.", nameof(target));
        count = Math.Clamp(count, 1, 50);

        var times = new List<long>();
        using var ping = new Ping();
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var reply = await ping.SendPingAsync(host, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: ct).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
            }
            catch (PingException)
            {
                // Name nicht auflösbar o. ä. – zählt als Verlust.
            }
            if (i < count - 1) await Task.Delay(250, ct).ConfigureAwait(false);
        }

        double? jitter = null;
        if (times.Count >= 2)
            jitter = times.Zip(times.Skip(1), (a, b) => Math.Abs(b - a)).Average();

        return new PingStatistics(host, count, times.Count,
            times.Count > 0 ? times.Min() : null,
            times.Count > 0 ? times.Max() : null,
            times.Count > 0 ? times.Average() : null,
            jitter);
    }

    /// <summary>
    /// DNS-Test (§29): sendet eine echte DNS-A-Abfrage direkt an jeden konfigurierten Server (UDP/53)
    /// und misst Antwortzeit und Ergebnis. So wird jeder Server einzeln geprüft – nicht nur der Systemresolver.
    /// </summary>
    public async Task<IReadOnlyList<DnsTestResult>> TestDnsServersAsync(string testHost = "www.microsoft.com", CancellationToken ct = default)
    {
        var servers = GetAdapters().Where(a => a.IsUp).SelectMany(a => a.DnsServers)
            .Select(s => IPAddress.TryParse(s, out var ip) ? ip : null)
            .OfType<IPAddress>()
            .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork || !ip.IsIPv6SiteLocal)
            .Distinct()
            .ToList();

        var results = new List<DnsTestResult>();
        foreach (var server in servers)
            results.Add(await QueryDnsServerAsync(server, testHost, ct).ConfigureAwait(false));
        return results;
    }

    internal static async Task<DnsTestResult> QueryDnsServerAsync(IPAddress server, string host, CancellationToken ct)
    {
        var id = (ushort)Random.Shared.Next(0, ushort.MaxValue);
        var query = BuildDnsQuery(id, host);
        using var udp = new UdpClient(server.AddressFamily);
        var sw = Stopwatch.StartNew();
        try
        {
            await udp.SendAsync(query, new IPEndPoint(server, 53), ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            var response = await udp.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            sw.Stop();

            var buf = response.Buffer;
            if (buf.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(buf) != id)
                return new DnsTestResult(server.ToString(), true, sw.Elapsed.TotalMilliseconds, false, "Ungültige Antwort");
            var rcode = buf[3] & 0x0F;
            var answers = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(6));
            var ok = rcode == 0 && answers > 0;
            return new DnsTestResult(server.ToString(), true, sw.Elapsed.TotalMilliseconds, ok,
                ok ? $"{answers} Antwort(en)" : $"Fehlercode {rcode}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new DnsTestResult(server.ToString(), false, null, false, "Zeitüberschreitung (2 s)");
        }
        catch (SocketException ex)
        {
            return new DnsTestResult(server.ToString(), false, null, false, ex.SocketErrorCode.ToString());
        }
    }

    internal static byte[] BuildDnsQuery(ushort id, string host)
    {
        var ms = new MemoryStream();
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header, id);
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], 0x0100); // Recursion desired
        BinaryPrimitives.WriteUInt16BigEndian(header[4..], 1);      // QDCOUNT
        ms.Write(header);
        foreach (var label in host.TrimEnd('.').Split('.'))
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(label);
            if (bytes.Length is 0 or > 63) throw new ArgumentException("Ungültiger Hostname.", nameof(host));
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes);
        }
        ms.WriteByte(0);
        ms.Write([0x00, 0x01, 0x00, 0x01]); // QTYPE=A, QCLASS=IN
        return ms.ToArray();
    }

    /// <summary>Öffentliche IP – nur auf Benutzeraktion, Antwort wird als IP validiert.</summary>
    public async Task<PublicIpResult> GetPublicIpAsync(CancellationToken ct = default)
    {
        static string? Validate(string? s, AddressFamily family)
            => s is not null && IPAddress.TryParse(s, out var ip) && ip.AddressFamily == family ? ip.ToString() : null;

        string? v4 = null, v6 = null;
        try { v4 = Validate(await SecureHttp.GetSmallStringAsync(PublicIpV4Url, 64, ct).ConfigureAwait(false), AddressFamily.InterNetwork); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }
        try { v6 = Validate(await SecureHttp.GetSmallStringAsync(PublicIpV6Url, 64, ct).ConfigureAwait(false), AddressFamily.InterNetworkV6); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException) { }

        AppLog.Info("PublicIp", "Öffentliche IP abgefragt (Dienst: ipify.org)");
        return new PublicIpResult(v4, v6, "ipify.org");
    }

    /// <summary>Netzwerkdiagnose (§27): prüft Schritt für Schritt von Adapter bis Internet.</summary>
    public async Task<IReadOnlyList<DiagnosisStep>> DiagnoseAsync(CancellationToken ct = default)
    {
        var steps = new List<DiagnosisStep>();
        var adapters = GetAdapters();
        var active = adapters.Where(a => a.IsUp && a.Kind is "LAN" or "WLAN").ToList();

        steps.Add(active.Count > 0
            ? new("Adapter aktiv", DataState.Ok, string.Join(", ", active.Select(a => $"{a.Name} ({a.Kind})")))
            : new("Adapter aktiv", DataState.Problem, "Kein aktiver LAN- oder WLAN-Adapter."));

        var withIp = active.Where(a => a.IPv4.Any(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal))).ToList();
        steps.Add(withIp.Count > 0
            ? new("IP-Adresse", DataState.Ok, "Gültige IPv4-Adresse vorhanden.")
            : new("IP-Adresse", DataState.Problem, active.Count > 0 ? "Keine gültige IPv4 (evtl. APIPA 169.254.x.x – DHCP prüfen)." : "–"));

        var gateway = withIp.SelectMany(a => a.Gateways).FirstOrDefault(g => IPAddress.TryParse(g, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork);
        if (gateway is null)
        {
            steps.Add(new("Gateway erreichbar", DataState.Problem, "Kein Standardgateway konfiguriert."));
        }
        else
        {
            var gp = await PingAsync(gateway, 5, 1000, ct).ConfigureAwait(false);
            steps.Add(new("Gateway erreichbar", gp.Received > 0 ? (gp.LossPercent > 0 ? DataState.Warning : DataState.Ok) : DataState.Problem,
                gp.Received > 0 ? $"{gateway}: Ø {gp.AvgMs:0} ms, Verlust {gp.LossPercent:0} %" : $"{gateway} antwortet nicht (Ping kann auch blockiert sein)."));
        }

        var sw = Stopwatch.StartNew();
        try
        {
            var addrs = await Dns.GetHostAddressesAsync("www.microsoft.com", ct).ConfigureAwait(false);
            steps.Add(new("DNS-Auflösung", addrs.Length > 0 ? DataState.Ok : DataState.Problem, $"www.microsoft.com aufgelöst in {sw.ElapsedMilliseconds} ms"));
        }
        catch (SocketException)
        {
            steps.Add(new("DNS-Auflösung", DataState.Problem, "Namensauflösung fehlgeschlagen."));
        }

        var inet = await PingAsync("1.1.1.1", 10, 1500, ct).ConfigureAwait(false);
        steps.Add(new("Internet (Ping 1.1.1.1)", inet.Received > 0 ? DataState.Ok : DataState.Problem,
            inet.Received > 0 ? $"Ø {inet.AvgMs:0} ms, Jitter {(inet.JitterMs ?? 0):0} ms" : "Keine Antwort."));
        steps.Add(new("Paketverlust", inet.LossPercent switch { 0 => DataState.Ok, < 10 => DataState.Warning, _ => DataState.Problem },
            $"{inet.LossPercent:0} % bei {inet.Sent} Paketen"));

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Head, SecureHttp.RequireHttps("https://www.microsoft.com/"));
            using var resp = await SecureHttp.Client.SendAsync(req, ct).ConfigureAwait(false);
            steps.Add(new("HTTPS-Verbindung", DataState.Ok, $"https://www.microsoft.com erreichbar (HTTP {(int)resp.StatusCode})"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            steps.Add(new("HTTPS-Verbindung", DataState.Problem, "HTTPS-Verbindung fehlgeschlagen: " + ex.Message));
        }

        AppLog.Info("NetworkDiagnosis", "Netzwerkdiagnose ausgeführt",
            string.Join("; ", steps.Select(s => $"{s.Name}={s.State}")));
        return steps;
    }
}
