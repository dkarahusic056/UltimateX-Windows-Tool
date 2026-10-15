using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.NetSec;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Network;

namespace UltimateX.Services.NetSec;

/// <summary>Netzwerkadapter für die DNS-Umstellung (Adressierung über den Schnittstellen-Index).</summary>
public sealed record DnsAdapter(int Index, int? IndexV6, string Name, string Description, IReadOnlyList<string> DnsServers, bool? StaticDns)
{
    public string DnsText => DnsServers.Count == 0 ? "keine" : string.Join(", ", DnsServers);

    public string ModeText => StaticDns switch { true => "manuell", false => "automatisch (DHCP)", _ => "N/A" };

    public override string ToString() => $"{Name} (#{Index})";
}

public sealed record PortResult(int Port, string Service, PortState State, long? Ms)
{
    public string StateText => PortCheckLogic.StateText(State);

    public DataState DataState => State switch
    {
        PortState.Open => DataState.Ok,
        PortState.Closed => DataState.NotAvailable,
        PortState.Filtered => DataState.Warning,
        _ => DataState.Problem,
    };

    public string TimeText => Ms is { } m ? $"{m} ms" : "–";
}

/// <summary>
/// v1.3 Netzwerk-Werkzeuge. Internetzugriffe ausschließlich zu Zielen, die der Benutzer selbst eingibt
/// (Traceroute, Port-Prüfung). Wake-on-LAN sendet nur Broadcasts ins eigene lokale Subnetz.
/// Systembefehle nur über die Allowlist (ipconfig/netsh).
/// </summary>
public sealed class NetworkToolsService
{
    // ---------------------------------------------------------------- Namensauflösung

    /// <summary>Löst ein geprüftes Ziel (IP oder Hostname) auf; IPv4 bevorzugt. Fehlertext statt Exception.</summary>
    public static async Task<(IPAddress? Address, string? Error)> ResolveAsync(string input, CancellationToken ct)
    {
        if (!InputValidator.TryNormalizeHost(input, out var host)) return (null, "Ungültiges Ziel. Erlaubt: IPv4-/IPv6-Adresse oder Hostname.");
        if (IPAddress.TryParse(host, out var ip)) return (ip, null);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var addresses = await Dns.GetHostAddressesAsync(host, cts.Token).ConfigureAwait(false);
            var pick = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                       ?? addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6);
            return pick is null ? (null, "Name konnte nicht aufgelöst werden.") : (pick, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (null, "Zeitüberschreitung bei der Namensauflösung.");
        }
        catch (SocketException ex)
        {
            return (null, "Name konnte nicht aufgelöst werden: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------- Traceroute

    /// <summary>
    /// Eigene Routenverfolgung: ICMP-Echo mit steigender TTL (1–30), je Hop 3 Proben. Bricht ab, wenn das Ziel antwortet,
    /// „nicht erreichbar“ gemeldet wird oder 5 Hops in Folge stumm bleiben.
    /// </summary>
    public async Task<IReadOnlyList<TraceHop>> TraceAsync(IPAddress target, bool resolveNames, IProgress<TraceHop>? progress, CancellationToken ct)
    {
        AppLog.Info("NetTools", "Traceroute", target.ToString());
        var hops = new List<TraceHop>();
        var buffer = new byte[32];
        var silent = 0;
        using var ping = new Ping();
        for (int ttl = 1; ttl <= TracerouteLogic.MaxHops; ttl++)
        {
            ct.ThrowIfCancellationRequested();
            var probes = new List<TraceProbe>();
            for (int i = 0; i < TracerouteLogic.ProbesPerHop; i++)
            {
                ct.ThrowIfCancellationRequested();
                var options = new PingOptions(ttl, dontFragment: true);
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                try
                {
                    var reply = await ping.SendPingAsync(target, TracerouteLogic.ProbeTimeoutMs, buffer, options).ConfigureAwait(false);
                    // Bei „TTL abgelaufen“ liefert RoundtripTime oft 0 – dann die gemessene Zeit verwenden.
                    var elapsed = (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var rtt = reply.Status == IPStatus.Success && reply.RoundtripTime > 0 ? reply.RoundtripTime : elapsed;
                    probes.Add(new TraceProbe(reply.Status, reply.Address, rtt));
                }
                catch (PingException ex)
                {
                    AppLog.Warn("NetTools", "Traceroute-Probe fehlgeschlagen", ex.Message);
                    probes.Add(new TraceProbe(IPStatus.Unknown, null, null));
                }
            }

            var hop = TracerouteLogic.Combine(ttl, probes);
            if (resolveNames && hop.Address is { } addr && hop.Kind is HopKind.Router or HopKind.Destination or HopKind.Unreachable)
                hop = hop with { HostName = await ReverseLookupAsync(addr, ct).ConfigureAwait(false) };
            hops.Add(hop);
            progress?.Report(hop);
            silent = hop.Kind == HopKind.NoReply ? silent + 1 : 0;
            if (TracerouteLogic.IsFinished(hop, silent)) break;
        }
        return hops;
    }

    private static async Task<string?> ReverseLookupAsync(IPAddress ip, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            var entry = await Dns.GetHostEntryAsync(ip.ToString(), cts.Token).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == ip.ToString() ? null : entry.HostName;
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- Port-Prüfung

    /// <summary>
    /// TCP-Verbindungsaufbau zu höchstens 20 Ports EINES Ziels (kein Scan von Adressbereichen). Es werden keine Daten gesendet.
    /// </summary>
    public async Task<IReadOnlyList<PortResult>> CheckPortsAsync(IPAddress target, IReadOnlyList<int> ports, IProgress<PortResult>? progress, CancellationToken ct)
    {
        if (ports.Count is 0 or > PortCheckLogic.MaxPorts) throw new ArgumentException($"1 bis {PortCheckLogic.MaxPorts} Ports erlaubt.", nameof(ports));
        if (ports.Any(p => p is < 1 or > 65535)) throw new ArgumentOutOfRangeException(nameof(ports));
        AppLog.Info("NetTools", "Port-Prüfung", $"{target}: {string.Join(",", ports)}");

        var results = new System.Collections.Concurrent.ConcurrentBag<PortResult>();
        await Parallel.ForEachAsync(ports, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (port, token) =>
        {
            var r = await ProbePortAsync(target, port, token).ConfigureAwait(false);
            results.Add(r);
            progress?.Report(r);
        }).ConfigureAwait(false);
        return results.OrderBy(r => ports.ToList().IndexOf(r.Port)).ToList();
    }

    private static async Task<PortResult> ProbePortAsync(IPAddress target, int port, CancellationToken ct)
    {
        var service = PortCheckLogic.ServiceName(port);
        using var client = new TcpClient(target.AddressFamily);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(PortCheckLogic.TimeoutMs);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await client.ConnectAsync(target, port, cts.Token).ConfigureAwait(false);
            return new PortResult(port, service, PortState.Open, (long)System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new PortResult(port, service, PortState.Filtered, null);
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new PortResult(port, service, PortState.Closed, null);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable)
        {
            return new PortResult(port, service, PortState.Filtered, null);
        }
        catch (SocketException)
        {
            return new PortResult(port, service, PortState.Error, null);
        }
    }

    // ---------------------------------------------------------------- Adapter / DNS

    /// <summary>Aktive Adapter mit IPv4 (Ethernet/WLAN), inkl. aktueller DNS-Server und ob diese manuell gesetzt sind.</summary>
    public static IReadOnlyList<DnsAdapter> GetAdapters()
    {
        var list = new List<DnsAdapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            int index;
            try
            {
                props = ni.GetIPProperties();
                var v4 = props.GetIPv4Properties();
                if (v4 is null) continue;
                index = v4.Index;
            }
            catch (Exception ex) when (ex is NetworkInformationException or NotSupportedException)
            {
                continue;
            }
            int? indexV6 = null;
            try
            {
                if (ni.Supports(NetworkInterfaceComponent.IPv6)) indexV6 = props.GetIPv6Properties()?.Index;
            }
            catch (Exception ex) when (ex is NetworkInformationException or NotSupportedException)
            {
            }
            var servers = props.DnsAddresses.Select(a => a.ToString()).ToList();
            list.Add(new DnsAdapter(index, indexV6, ni.Name, ni.Description, servers, IsStaticDns(ni.Id)));
        }
        return list.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Manuelle DNS-Server stehen in Tcpip\Parameters\Interfaces\{GUID}\NameServer (leer = DHCP).</summary>
    private static bool? IsStaticDns(string interfaceId)
    {
        if (!Guid.TryParse(interfaceId, out var g)) return null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{g:B}");
            if (k is null) return null;
            return k.GetValue("NameServer") is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Stellt die DNS-Server eines Adapters um. Jeder Schritt ist ein eigener Allowlist-Befehl (netsh kennt nur eine Adresse
    /// pro Aufruf) – daher erscheint je Schritt eine Windows-Rückfrage (UAC).
    /// </summary>
    public static async Task<OperationResult> SetDnsAsync(DnsAdapter adapter, string? providerId, bool includeIpv6, IProgress<string>? progress, CancellationToken ct)
    {
        var steps = new List<(AllowedCommand Cmd, string Param, string Label)>();
        if (providerId is null)
        {
            steps.Add((AllowedCommand.DnsSetDhcp, DnsPresets.DhcpParameter(adapter.Index), "IPv4 automatisch"));
            if (includeIpv6 && adapter.IndexV6 is { } i6) steps.Add((AllowedCommand.DnsSetDhcp, DnsPresets.DhcpParameter(i6, IpFamily.V6), "IPv6 automatisch"));
        }
        else
        {
            var provider = DnsPresets.Find(providerId) ?? throw new ArgumentException("Unbekannter DNS-Anbieter.", nameof(providerId));
            steps.Add((AllowedCommand.DnsSetStatic, DnsPresets.StaticParameter(adapter.Index, provider.Id), $"IPv4 primär {provider.PrimaryV4}"));
            steps.Add((AllowedCommand.DnsSetStatic, DnsPresets.StaticParameter(adapter.Index, provider.Id, IpFamily.V4, 2), $"IPv4 sekundär {provider.SecondaryV4}"));
            if (includeIpv6 && adapter.IndexV6 is { } i6)
            {
                steps.Add((AllowedCommand.DnsSetStatic, DnsPresets.StaticParameter(i6, provider.Id, IpFamily.V6, 1), $"IPv6 primär {provider.PrimaryV6}"));
                steps.Add((AllowedCommand.DnsSetStatic, DnsPresets.StaticParameter(i6, provider.Id, IpFamily.V6, 2), $"IPv6 sekundär {provider.SecondaryV6}"));
            }
        }

        var log = new System.Text.StringBuilder();
        foreach (var (cmd, param, label) in steps)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(label + " …" + Environment.NewLine);
            var r = await ElevatedCommandRunner.RunAsync(cmd, param, null, ct).ConfigureAwait(false);
            log.AppendLine($"{label}: Exit {r.ExitCode}").AppendLine(r.Output.Trim());
            if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt – DNS-Einstellung evtl. nur teilweise geändert.", log.ToString());
            if (r.ExitCode != 0) return OperationResult.Fail($"Schritt „{label}“ fehlgeschlagen (Exit {r.ExitCode}).", log.ToString());
        }
        AppLog.Info("NetTools", "DNS-Server geändert", $"{adapter.Name} (#{adapter.Index}) → {providerId ?? "DHCP"}{(includeIpv6 ? " inkl. IPv6" : "")}");
        return OperationResult.Ok(providerId is null ? "DNS-Server wieder automatisch (DHCP)." : "DNS-Server umgestellt.", log.ToString());
    }

    // ---------------------------------------------------------------- Einfache Befehle

    public static Task<CommandResult> FlushDnsAsync(IProgress<string>? progress, CancellationToken ct)
        => ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.DnsFlush), progress, ct);

    public static Task<CommandResult> RenewIpAsync(IProgress<string>? progress, CancellationToken ct)
        => ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.IpRenew), progress, ct);

    /// <summary>Winsock- und TCP/IP-Reset (je eine UAC-Rückfrage). Danach ist ein Neustart nötig.</summary>
    public static async Task<OperationResult> ResetNetworkAsync(IProgress<string>? progress, CancellationToken ct)
    {
        var log = new System.Text.StringBuilder();
        foreach (var cmd in new[] { AllowedCommand.NetworkResetWinsock, AllowedCommand.NetworkResetIp })
        {
            var r = await ElevatedCommandRunner.RunAsync(cmd, null, progress, ct).ConfigureAwait(false);
            log.AppendLine($"{cmd}: Exit {r.ExitCode}").AppendLine(r.Output.Trim());
            if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.", log.ToString());
            // netsh int ip reset meldet bei einzelnen gesperrten Schlüsseln Exit 1, obwohl der Rest zurückgesetzt wurde.
            if (r.ExitCode != 0 && cmd == AllowedCommand.NetworkResetWinsock)
                return OperationResult.Fail($"Winsock-Reset fehlgeschlagen (Exit {r.ExitCode}).", log.ToString());
        }
        AppLog.Info("NetTools", "Netzwerk zurückgesetzt", "Winsock + TCP/IP");
        return OperationResult.Ok("Netzwerk zurückgesetzt. Bitte den PC jetzt neu starten.", log.ToString());
    }

    // ---------------------------------------------------------------- Wake-on-LAN

    /// <summary>
    /// Sendet ein Magic Packet als UDP-Broadcast (Port 9) – nur ins eigene lokale Subnetz: an 255.255.255.255 und an die
    /// gerichtete Broadcast-Adresse jedes aktiven privaten IPv4-Netzes. Keine frei wählbare Zieladresse.
    /// </summary>
    public static async Task<OperationResult> WakeAsync(string macInput, CancellationToken ct)
    {
        if (!WakeOnLan.TryParseMac(macInput, out var mac)) return OperationResult.Fail("Ungültige MAC-Adresse (z. B. AA:BB:CC:DD:EE:FF).");
        var packet = WakeOnLan.BuildMagicPacket(mac);
        var sent = 0;
        var targets = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork || !NetworkScannerService.IsPrivate(ua.Address)) continue;
                if (ua.PrefixLength is < 8 or > 30) continue;
                var directed = WakeOnLan.DirectedBroadcast(ua.Address, ua.PrefixLength);
                try
                {
                    using var udp = new UdpClient(new IPEndPoint(ua.Address, 0)) { EnableBroadcast = true };
                    foreach (var dest in new[] { IPAddress.Broadcast, directed })
                    {
                        await udp.SendAsync(packet, new IPEndPoint(dest, WakeOnLan.Port), ct).ConfigureAwait(false);
                        sent++;
                    }
                    targets.Add($"{ni.Name}: {directed}");
                }
                catch (SocketException ex)
                {
                    AppLog.Warn("NetTools", "Wake-on-LAN über Adapter fehlgeschlagen", $"{ni.Name}: {ex.Message}");
                }
            }
        }
        var macText = WakeOnLan.FormatMac(mac);
        if (sent == 0) return OperationResult.Fail("Kein aktiver Adapter in einem privaten Heimnetz gefunden – nichts gesendet.");
        AppLog.Info("NetTools", "Wake-on-LAN gesendet", macText);
        return OperationResult.Ok($"Magic Packet an {macText} gesendet ({sent} Broadcast(s)).", string.Join(Environment.NewLine, targets));
    }
}
