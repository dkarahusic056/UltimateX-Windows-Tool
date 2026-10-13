using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Data;

namespace UltimateX.Services.Network;

public sealed record ScanSubnet(string AdapterName, IPAddress LocalIp, IPAddress Network, int PrefixLength, IPAddress? Gateway, string? LocalMac, int HostCount, bool Truncated)
{
    public string Cidr => $"{Network}/{PrefixLength}";
}

public sealed record OpenPort(int Port, string Service, DataState State, string? Hint);

public sealed record LanDevice(
    IPAddress Ip,
    string? Mac,
    string? Vendor,
    string? HostName,
    long? PingMs,
    bool IsGateway,
    bool IsSelf,
    bool IsNew,
    string? Label)
{
    public string TypeGuess { get; init; } = "";
}

public sealed record ScanProgress(int Done, int Total, int Found);

/// <summary>
/// Netzwerk-Scanner (§103) für das EIGENE Heimnetz.
///
/// Sicherheitsregeln (THREAT_MODEL T-25):
///  - Gescannt wird ausschließlich ein privates Subnetz (RFC 1918 / Link-Local) eines eigenen, aktiven Adapters.
///    Öffentliche Adressbereiche werden abgelehnt.
///  - Höchstens 1024 Adressen; größere Netze werden auf das /22 um die eigene Adresse begrenzt.
///  - Erkennung über ARP (SendARP) und Ping – keine Angriffs- oder Fingerprinting-Techniken.
///  - Portprüfung nur auf Klick für EIN Gerät, nur eine feste Liste gängiger Dienste, einfacher TCP-Verbindungsaufbau.
/// </summary>
public sealed class NetworkScannerService(HistoryDatabase db)
{
    public const string OuiUrl = "https://standards-oui.ieee.org/oui/oui.csv";
    private static readonly string OuiPath = Path.Combine(AppPaths.UserDataRoot, "oui.csv");
    private Dictionary<string, string>? _oui;

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(uint destIp, uint srcIp, byte[] macAddr, ref uint macLength);

    // ---------------------------------------------------------------- Subnetze

    public static IReadOnlyList<ScanSubnet> GetLocalSubnets()
    {
        var list = new List<ScanSubnet>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            IPInterfaceProperties props;
            try { props = ni.GetIPProperties(); } catch (NetworkInformationException) { continue; }
            var gw = props.GatewayAddresses.Select(g => g.Address).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var prefix = ua.PrefixLength;
                if (prefix is < 8 or > 30 || !IsPrivate(ua.Address)) continue;
                var effective = Math.Max(prefix, 22); // höchstens 1024 Adressen
                var network = NetworkAddress(ua.Address, effective);
                var hosts = (1 << (32 - effective)) - 2;
                var mac = ni.GetPhysicalAddress().GetAddressBytes() is { Length: 6 } b ? FormatMac(b) : null;
                list.Add(new ScanSubnet(ni.Name, ua.Address, network, effective, gw, mac, hosts, effective != prefix));
            }
        }
        // Adapter mit Gateway (das „echte“ Heimnetz) zuerst.
        return list.OrderByDescending(s => s.Gateway is not null).ToList();
    }

    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10
               || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
               || (b[0] == 192 && b[1] == 168)
               || (b[0] == 169 && b[1] == 254);
    }

    internal static IPAddress NetworkAddress(IPAddress ip, int prefix)
    {
        var value = ToUInt(ip);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        return FromUInt(value & mask);
    }

    internal static uint ToUInt(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
    }

    internal static IPAddress FromUInt(uint v) => new(new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });

    /// <summary>Alle Hostadressen eines Subnetzes (ohne Netz- und Broadcastadresse).</summary>
    internal static IEnumerable<IPAddress> Hosts(IPAddress network, int prefix)
    {
        var start = ToUInt(network);
        var count = (1u << (32 - prefix)) - 2;
        for (uint i = 1; i <= count; i++) yield return FromUInt(start + i);
    }

    internal static bool Contains(ScanSubnet s, IPAddress ip)
        => ip.AddressFamily == AddressFamily.InterNetwork && NetworkAddress(ip, s.PrefixLength).Equals(s.Network);

    // ---------------------------------------------------------------- Scan

    public async Task<IReadOnlyList<LanDevice>> ScanAsync(ScanSubnet subnet, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        if (!IsPrivate(subnet.LocalIp) || subnet.PrefixLength < 22)
            throw new InvalidOperationException("Es werden nur private Heimnetze mit höchstens 1024 Adressen gescannt.");

        AppLog.Info("NetworkScanner", "Scan gestartet", subnet.Cidr);
        var oui = LoadOui();
        var targets = Hosts(subnet.Network, subnet.PrefixLength).ToList();
        var found = new ConcurrentBag<(IPAddress Ip, string? Mac, long? Ping)>();
        var done = 0;
        var srcIp = BitConverter.ToUInt32(subnet.LocalIp.GetAddressBytes(), 0);
        using var gate = new SemaphoreSlim(64);

        var tasks = targets.Select(async ip =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long? ping = null;
                try
                {
                    using var p = new Ping();
                    var reply = await p.SendPingAsync(ip, 700).ConfigureAwait(false);
                    if (reply.Status == IPStatus.Success) ping = reply.RoundtripTime;
                }
                catch (PingException) { }

                // ARP findet auch Geräte, die Ping blockieren (z. B. Windows-PCs mit Firewall).
                string? mac = null;
                if (!ip.Equals(subnet.LocalIp))
                {
                    var buf = new byte[6];
                    uint len = 6;
                    var dest = BitConverter.ToUInt32(ip.GetAddressBytes(), 0);
                    if (await Task.Run(() => SendARP(dest, srcIp, buf, ref len), ct).ConfigureAwait(false) == 0 && len == 6)
                        mac = FormatMac(buf);
                }
                else
                {
                    mac = subnet.LocalMac;
                }
                if (ping is not null || mac is not null) found.Add((ip, mac, ping));
            }
            finally
            {
                gate.Release();
                var d = Interlocked.Increment(ref done);
                if (d % 8 == 0 || d == targets.Count) progress?.Report(new ScanProgress(d, targets.Count, found.Count));
            }
        }).ToList();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var known = db.KnownDeviceCount() > 0;
        var labels = db.GetKnownDevices().ToDictionary(d => d.Mac, d => d.Label, StringComparer.OrdinalIgnoreCase);

        var devices = new List<LanDevice>();
        foreach (var f in found.OrderBy(f => ToUInt(f.Ip)))
        {
            ct.ThrowIfCancellationRequested();
            var name = await ReverseDnsAsync(f.Ip).ConfigureAwait(false);
            var vendor = Vendor(f.Mac, oui);
            var isNew = false;
            if (f.Mac is not null)
                isNew = db.UpsertDevice(f.Mac, f.Ip.ToString(), name, vendor) && known;
            var isGateway = f.Ip.Equals(subnet.Gateway);
            var isSelf = f.Ip.Equals(subnet.LocalIp);
            devices.Add(new LanDevice(f.Ip, f.Mac, vendor, name, f.Ping, isGateway, isSelf, isNew,
                f.Mac is not null ? labels.GetValueOrDefault(f.Mac) : null)
            {
                TypeGuess = isSelf ? "Dieser PC" : isGateway ? "Router / Gateway" : GuessType(vendor, name, []),
            });
        }
        AppLog.Info("NetworkScanner", "Scan abgeschlossen", $"{subnet.Cidr}: {devices.Count} Geräte");
        return devices;
    }

    private static async Task<string?> ReverseDnsAsync(IPAddress ip)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var entry = await Dns.GetHostEntryAsync(ip.ToString(), cts.Token).ConfigureAwait(false);
            var host = entry.HostName;
            return string.IsNullOrWhiteSpace(host) || host == ip.ToString() ? null : host;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- Ports

    private static readonly (int Port, string Service)[] CommonPorts =
    [
        (21, "FTP"), (22, "SSH"), (23, "Telnet"), (53, "DNS"), (80, "HTTP (Weboberfläche)"), (139, "NetBIOS"),
        (443, "HTTPS (Weboberfläche)"), (445, "SMB (Dateifreigabe)"), (515, "Drucker (LPD)"), (554, "RTSP (Kamera/Stream)"),
        (631, "Drucker (IPP)"), (1883, "MQTT (Smart Home)"), (3389, "Remotedesktop"), (5000, "NAS / UPnP"), (5001, "NAS (HTTPS)"),
        (8080, "HTTP alternativ"), (8443, "HTTPS alternativ"), (9100, "Drucker (RAW)"), (32400, "Plex Media Server"), (62078, "Apple-Gerät (Sync)"),
    ];

    /// <summary>Prüft gängige Dienste eines Geräts im gescannten Subnetz (TCP-Verbindungsaufbau, 600 ms Timeout).</summary>
    public async Task<IReadOnlyList<OpenPort>> CheckPortsAsync(ScanSubnet subnet, IPAddress ip, CancellationToken ct)
    {
        if (!Contains(subnet, ip) || !IsPrivate(ip))
            throw new InvalidOperationException("Portprüfung nur für Geräte im eigenen Heimnetz.");
        AppLog.Info("NetworkScanner", "Portprüfung", ip.ToString());
        var results = new ConcurrentBag<OpenPort>();
        await Parallel.ForEachAsync(CommonPorts, new ParallelOptions { MaxDegreeOfParallelism = 10, CancellationToken = ct }, async (p, token) =>
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(600);
            try
            {
                await client.ConnectAsync(ip, p.Port, cts.Token).ConfigureAwait(false);
                var (state, hint) = p.Port switch
                {
                    23 => (DataState.Problem, "Telnet überträgt unverschlüsselt – in den Geräteeinstellungen deaktivieren."),
                    21 => (DataState.Warning, "FTP ist unverschlüsselt – wenn möglich SFTP/FTPS nutzen."),
                    3389 => (DataState.Warning, "Remotedesktop ist erreichbar – nur aktiv lassen, wenn benötigt."),
                    1883 => (DataState.Warning, "MQTT ohne TLS – nur im Heimnetz betreiben."),
                    80 or 8080 => (DataState.Ok, "Weboberfläche (unverschlüsselt) – Passwort nur im Heimnetz eingeben."),
                    _ => (DataState.Ok, (string?)null),
                };
                results.Add(new OpenPort(p.Port, p.Service, state, hint));
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // geschlossen / gefiltert
            }
        }).ConfigureAwait(false);
        return results.OrderBy(r => r.Port).ToList();
    }

    /// <summary>Gerätetyp-Vermutung aus Hersteller, Name und offenen Diensten – als Vermutung gekennzeichnet.</summary>
    public static string GuessType(string? vendor, string? host, IReadOnlyCollection<int> ports)
    {
        var v = (vendor ?? "") + " " + (host ?? "");
        bool Has(params string[] words) => words.Any(w => v.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (ports.Contains(9100) || ports.Contains(631) || ports.Contains(515) || Has("printer", "drucker", "epson", "brother", "canon", "hewlett", "kyocera", "lexmark")) return "Drucker";
        if ((ports.Contains(5000) && ports.Contains(5001)) || Has("synology", "qnap", "nas")) return "NAS";
        if (ports.Contains(554) || Has("camera", "kamera", "hikvision", "dahua", "reolink", "axis comm")) return "Kamera";
        if (ports.Contains(62078) || Has("iphone", "ipad", "apple")) return "Apple-Gerät";
        if (Has("sonos", "denon", "yamaha", "bose")) return "Lautsprecher / Audio";
        if (Has("tv", "lg electronics", "samsung electronics", "sony", "roku", "chromecast")) return "TV / Streaming";
        if (Has("raspberry")) return "Raspberry Pi";
        if (Has("espressif", "tuya", "shelly", "philips lighting", "signify", "tp-link", "amazon")) return "Smart-Home-Gerät";
        if (ports.Contains(3389) || ports.Contains(445) || Has("desktop", "laptop", "pc-")) return "Computer";
        if (Has("avm", "fritz", "router", "speedport", "ubiquiti", "netgear", "asus")) return "Netzwerkgerät";
        return "";
    }

    // ---------------------------------------------------------------- Hersteller (IEEE OUI)

    public static bool HasVendorList => File.Exists(OuiPath);

    /// <summary>Lädt die offizielle IEEE-Herstellerliste (HTTPS) und speichert sie lokal. Nur auf Klick.</summary>
    public async Task<OperationResult> DownloadVendorListAsync(CancellationToken ct)
    {
        try
        {
            var text = await SecureHttp.GetSmallStringAsync(OuiUrl, 12 * 1024 * 1024, ct).ConfigureAwait(false);
            var parsed = ParseOui(text);
            if (parsed.Count < 1000) return OperationResult.Fail("Die heruntergeladene Herstellerliste ist unvollständig und wurde verworfen.");
            Directory.CreateDirectory(AppPaths.UserDataRoot);
            var tmp = OuiPath + ".tmp";
            await File.WriteAllTextAsync(tmp, text, ct).ConfigureAwait(false);
            File.Move(tmp, OuiPath, true);
            _oui = parsed;
            return OperationResult.Ok($"Herstellerliste geladen ({parsed.Count:N0} Einträge, IEEE).");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            return OperationResult.Fail("Herstellerliste konnte nicht geladen werden: " + ex.Message);
        }
    }

    private Dictionary<string, string> LoadOui()
    {
        if (_oui is not null) return _oui;
        try
        {
            _oui = File.Exists(OuiPath) ? ParseOui(File.ReadAllText(OuiPath)) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _oui = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        return _oui;
    }

    /// <summary>Parst die IEEE-CSV („Registry,Assignment,Organization Name,Organization Address“).</summary>
    internal static Dictionary<string, string> ParseOui(string csv)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in csv.Split('\n'))
        {
            var fields = SplitCsv(line);
            if (fields.Count < 3 || fields[0] != "MA-L") continue;
            var key = fields[1].Trim();
            if (key.Length != 6 || !key.All(Uri.IsHexDigit)) continue;
            var name = fields[2].Trim();
            if (name.Length > 0) map[key] = name.Length > 80 ? name[..80] : name;
        }
        return map;
    }

    private static List<string> SplitCsv(string line)
    {
        var result = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { result.Add(cur.ToString()); cur.Clear(); }
            else if (c != '\r') cur.Append(c);
        }
        result.Add(cur.ToString());
        return result;
    }

    internal static string? Vendor(string? mac, IReadOnlyDictionary<string, string> oui)
    {
        if (mac is null || mac.Length < 8) return null;
        var first = Convert.ToByte(mac[..2], 16);
        if ((first & 0x02) != 0) return "Private/zufällige MAC-Adresse";
        var key = mac.Replace(":", "", StringComparison.Ordinal)[..6];
        return oui.TryGetValue(key, out var v) ? v : null;
    }

    internal static string FormatMac(byte[] b) => string.Join(":", b.Take(6).Select(x => x.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)));

    public void SetLabel(string mac, string? label) => db.SetDeviceLabel(mac, label);

    public void ForgetKnownDevices() => db.ForgetDevices();
}
