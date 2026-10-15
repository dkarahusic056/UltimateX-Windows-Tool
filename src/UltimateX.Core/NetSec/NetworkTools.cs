using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace UltimateX.Core.NetSec;

// =====================================================================================================
// v1.3 Netzwerk-Werkzeuge: reine, testbare Logik (keine Netzwerkzugriffe in dieser Datei).
// Die eigentlichen Zugriffe (Ping, TCP, UDP) liegen in UltimateX.Services.NetSec.NetworkToolsService.
// =====================================================================================================

/// <summary>Art eines Hops in der Routenverfolgung.</summary>
public enum HopKind
{
    /// <summary>Zwischenstation (Router) hat „TTL abgelaufen“ gemeldet.</summary>
    Router,
    /// <summary>Ziel hat geantwortet.</summary>
    Destination,
    /// <summary>Keine Antwort (Router verwirft ICMP oder Zeitüberschreitung) – „*“.</summary>
    NoReply,
    /// <summary>Ziel bzw. Netz nicht erreichbar gemeldet.</summary>
    Unreachable,
    /// <summary>Sonstiger Fehler.</summary>
    Error,
}

/// <summary>Einzelne Ping-Probe eines Hops.</summary>
public sealed record TraceProbe(IPStatus Status, IPAddress? Address, long? RoundTripMs);

/// <summary>Ein Hop der Routenverfolgung (zusammengefasst aus mehreren Proben).</summary>
public sealed record TraceHop(int Ttl, IPAddress? Address, string? HostName, IReadOnlyList<long?> Times, HopKind Kind)
{
    public string AddressText => Address?.ToString() ?? "*";

    public string HostText => HostName ?? "–";

    public string TimesText => string.Join("   ", Times.Select(t => t is { } v ? (v < 1 ? "<1 ms" : v.ToString(CultureInfo.InvariantCulture) + " ms") : "*"));

    public string KindText => Kind switch
    {
        HopKind.Router => "Router",
        HopKind.Destination => "Ziel erreicht",
        HopKind.NoReply => "keine Antwort",
        HopKind.Unreachable => "nicht erreichbar",
        _ => "Fehler",
    };
}

/// <summary>Regeln der eigenen Traceroute-Umsetzung (Ping mit steigender TTL).</summary>
public static class TracerouteLogic
{
    public const int MaxHops = 30;
    public const int ProbesPerHop = 3;
    public const int ProbeTimeoutMs = 1500;

    /// <summary>Nach so vielen Hops ohne jede Antwort in Folge wird abgebrochen (Ziel filtert ICMP).</summary>
    public const int MaxSilentHops = 5;

    public static HopKind Classify(IPStatus status) => status switch
    {
        IPStatus.Success => HopKind.Destination,
        IPStatus.TtlExpired or IPStatus.TimeExceeded => HopKind.Router,
        IPStatus.TimedOut => HopKind.NoReply,
        IPStatus.DestinationHostUnreachable or IPStatus.DestinationNetworkUnreachable or IPStatus.DestinationUnreachable
            or IPStatus.DestinationPortUnreachable or IPStatus.DestinationProtocolUnreachable => HopKind.Unreachable,
        _ => HopKind.Error,
    };

    /// <summary>Fasst die Proben eines Hops zusammen. Ziel-Antwort hat Vorrang vor Router, Router vor „nicht erreichbar“.</summary>
    public static TraceHop Combine(int ttl, IReadOnlyList<TraceProbe> probes, string? hostName = null)
    {
        if (ttl is < 1 or > MaxHops) throw new ArgumentOutOfRangeException(nameof(ttl));
        var kinds = probes.Select(p => Classify(p.Status)).ToList();
        HopKind kind;
        if (kinds.Contains(HopKind.Destination)) kind = HopKind.Destination;
        else if (kinds.Contains(HopKind.Router)) kind = HopKind.Router;
        else if (kinds.Contains(HopKind.Unreachable)) kind = HopKind.Unreachable;
        else if (kinds.Count > 0 && kinds.All(k => k == HopKind.NoReply)) kind = HopKind.NoReply;
        else kind = kinds.Count == 0 ? HopKind.NoReply : HopKind.Error;

        // Adresse der „wichtigsten“ Probe anzeigen.
        var address = probes.FirstOrDefault(p => Classify(p.Status) == kind && p.Address is not null && !IsUnspecified(p.Address))?.Address
                      ?? probes.FirstOrDefault(p => p.Address is not null && !IsUnspecified(p.Address))?.Address;
        var times = probes.Select(p => Classify(p.Status) is HopKind.Router or HopKind.Destination ? p.RoundTripMs : null).ToList();
        return new TraceHop(ttl, address, hostName, times, kind);
    }

    /// <summary>Ende erreicht? (Ziel, „nicht erreichbar“, 30 Hops oder zu viele stumme Hops in Folge).</summary>
    public static bool IsFinished(TraceHop hop, int silentHopsInRow)
        => hop.Kind is HopKind.Destination or HopKind.Unreachable || hop.Ttl >= MaxHops || silentHopsInRow >= MaxSilentHops;

    /// <summary>Kurzfazit für den Ergebnisbereich.</summary>
    public static string Summary(IReadOnlyList<TraceHop> hops, bool cancelled)
    {
        if (cancelled) return $"Abgebrochen nach {hops.Count} Hop(s).";
        if (hops.Count == 0) return "Keine Hops ermittelt.";
        var last = hops[^1];
        return last.Kind switch
        {
            HopKind.Destination => $"Ziel nach {last.Ttl} Hop(s) erreicht.",
            HopKind.Unreachable => $"Ziel nicht erreichbar (gemeldet bei Hop {last.Ttl}).",
            _ when last.Ttl >= MaxHops => $"Ziel innerhalb von {MaxHops} Hops nicht erreicht.",
            _ => $"Abbruch nach {MaxSilentHops} Hops ohne Antwort – das Ziel oder ein Router blockiert vermutlich ICMP.",
        };
    }

    private static bool IsUnspecified(IPAddress a) => a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any);
}

// ----------------------------------------------------------------------------------------------- Ports

public sealed record ServicePort(int Port, string Service);

public enum PortState
{
    Open,
    Closed,
    Filtered,
    Error,
}

/// <summary>
/// Port-Prüfung für EIN vom Benutzer genanntes Ziel – bewusst kein Portscanner:
/// höchstens <see cref="MaxPorts"/> einzeln genannte Ports pro Lauf, keine Bereiche, kein Subnetz.
/// </summary>
public static class PortCheckLogic
{
    public const int MaxPorts = 20;
    public const int TimeoutMs = 2000;

    /// <summary>Gängige Dienste (feste Liste, genau <see cref="MaxPorts"/> Einträge).</summary>
    public static IReadOnlyList<ServicePort> CommonServices { get; } =
    [
        new(21, "FTP"), new(22, "SSH"), new(23, "Telnet"), new(25, "SMTP"), new(53, "DNS"), new(80, "HTTP"),
        new(110, "POP3"), new(143, "IMAP"), new(443, "HTTPS"), new(445, "SMB (Dateifreigabe)"), new(465, "SMTPS"),
        new(587, "SMTP Submission"), new(993, "IMAPS"), new(995, "POP3S"), new(1433, "SQL Server"), new(3306, "MySQL/MariaDB"),
        new(3389, "Remotedesktop"), new(5900, "VNC"), new(8080, "HTTP alternativ"), new(8443, "HTTPS alternativ"),
    ];

    public static string ServiceName(int port) => CommonServices.FirstOrDefault(s => s.Port == port)?.Service ?? "–";

    /// <summary>
    /// Liest eine Portliste („80, 443 8080;3389“). Nur Zahlen 1–65535, keine Bereiche, höchstens 20 verschiedene Ports.
    /// Doppelte Angaben werden zusammengefasst (Reihenfolge bleibt erhalten).
    /// </summary>
    public static bool TryParsePorts(string? input, out IReadOnlyList<int> ports, out string? error)
    {
        ports = [];
        error = null;
        if (string.IsNullOrWhiteSpace(input)) { error = "Bitte mindestens einen Port angeben."; return false; }
        if (input.Length > 400) { error = "Eingabe zu lang."; return false; }
        var list = new List<int>();
        foreach (var raw in input.Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Contains('-')) { error = "Portbereiche sind nicht erlaubt – bitte einzelne Ports angeben."; return false; }
            if (raw.Length > 5 || !raw.All(char.IsAsciiDigit)
                || !int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var p) || p is < 1 or > 65535)
            {
                error = $"Ungültiger Port: {raw} (erlaubt: 1–65535).";
                return false;
            }
            if (!list.Contains(p)) list.Add(p);
        }
        if (list.Count == 0) { error = "Bitte mindestens einen Port angeben."; return false; }
        if (list.Count > MaxPorts) { error = $"Höchstens {MaxPorts} Ports pro Prüfung."; return false; }
        ports = list;
        return true;
    }

    public static string StateText(PortState s) => s switch
    {
        PortState.Open => "offen",
        PortState.Closed => "geschlossen",
        PortState.Filtered => "keine Antwort (gefiltert)",
        _ => "Fehler",
    };
}

// ----------------------------------------------------------------------------------------------- DNS

public sealed record DnsProvider(string Id, string Name, string PrimaryV4, string SecondaryV4, string PrimaryV6, string SecondaryV6)
{
    public string Address(IpFamily family, int slot) => (family, slot) switch
    {
        (IpFamily.V4, 1) => PrimaryV4,
        (IpFamily.V4, 2) => SecondaryV4,
        (IpFamily.V6, 1) => PrimaryV6,
        (IpFamily.V6, 2) => SecondaryV6,
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };
}

public enum IpFamily
{
    V4 = 4,
    V6 = 6,
}

/// <summary>Validierter Parameter für <c>DnsSetStatic</c>.</summary>
public sealed record DnsStaticParameter(int InterfaceIndex, DnsProvider Provider, IpFamily Family, int Slot)
{
    public string Address => Provider.Address(Family, Slot);
}

/// <summary>
/// Feste DNS-Server-Liste (keine freie Eingabe) und das Parameterformat für den Admin-Helper.
/// Statisch: <c>&lt;ifIndex&gt;:&lt;anbieter&gt;</c> (= IPv4, primär) oder <c>&lt;ifIndex&gt;:&lt;anbieter&gt;:&lt;4|6&gt;:&lt;1|2&gt;</c>.
/// DHCP: <c>&lt;ifIndex&gt;</c> (IPv4) oder <c>&lt;ifIndex&gt;:6</c> (IPv6). Der Adapter wird über seinen Schnittstellen-Index (Zahl) adressiert,
/// damit der Parameter <see cref="Security.CommandCatalog.IsSafeToken"/> erfüllt.
/// </summary>
public static partial class DnsPresets
{
    public static IReadOnlyList<DnsProvider> Providers { get; } =
    [
        new("cloudflare", "Cloudflare (1.1.1.1)", "1.1.1.1", "1.0.0.1", "2606:4700:4700::1111", "2606:4700:4700::1001"),
        new("quad9", "Quad9 (9.9.9.9)", "9.9.9.9", "149.112.112.112", "2620:fe::fe", "2620:fe::9"),
        new("google", "Google (8.8.8.8)", "8.8.8.8", "8.8.4.4", "2001:4860:4860::8888", "2001:4860:4860::8844"),
    ];

    [GeneratedRegex(@"^([1-9][0-9]{0,8}):(cloudflare|quad9|google)(?::([46]):([12]))?$", RegexOptions.CultureInvariant)]
    private static partial Regex StaticRegex();

    [GeneratedRegex(@"^([1-9][0-9]{0,8})(?::(6))?$", RegexOptions.CultureInvariant)]
    private static partial Regex DhcpRegex();

    public static DnsProvider? Find(string? id) => Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public static string StaticParameter(int interfaceIndex, string providerId, IpFamily family = IpFamily.V4, int slot = 1)
    {
        if (interfaceIndex is < 1 or > 999_999_999) throw new ArgumentOutOfRangeException(nameof(interfaceIndex));
        if (Find(providerId) is null) throw new ArgumentException("Unbekannter DNS-Anbieter.", nameof(providerId));
        if (slot is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(slot));
        var idx = interfaceIndex.ToString(CultureInfo.InvariantCulture);
        return family == IpFamily.V4 && slot == 1 ? $"{idx}:{providerId}" : $"{idx}:{providerId}:{(int)family}:{slot}";
    }

    public static string DhcpParameter(int interfaceIndex, IpFamily family = IpFamily.V4)
    {
        if (interfaceIndex is < 1 or > 999_999_999) throw new ArgumentOutOfRangeException(nameof(interfaceIndex));
        var idx = interfaceIndex.ToString(CultureInfo.InvariantCulture);
        return family == IpFamily.V4 ? idx : $"{idx}:6";
    }

    public static bool TryParseStatic(string? parameter, out DnsStaticParameter? result)
    {
        result = null;
        if (parameter is null) return false;
        var m = StaticRegex().Match(parameter);
        if (!m.Success) return false;
        var index = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        var provider = Find(m.Groups[2].Value);
        if (provider is null) return false;
        var family = m.Groups[3].Success && m.Groups[3].Value == "6" ? IpFamily.V6 : IpFamily.V4;
        var slot = m.Groups[4].Success ? m.Groups[4].Value[0] - '0' : 1;
        result = new DnsStaticParameter(index, provider, family, slot);
        return true;
    }

    public static bool TryParseDhcp(string? parameter, out int interfaceIndex, out IpFamily family)
    {
        interfaceIndex = 0;
        family = IpFamily.V4;
        if (parameter is null) return false;
        var m = DhcpRegex().Match(parameter);
        if (!m.Success) return false;
        interfaceIndex = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        family = m.Groups[2].Success && m.Groups[2].Value == "6" ? IpFamily.V6 : IpFamily.V4;
        return true;
    }

    /// <summary>Welcher Anbieter ist an den angezeigten DNS-Servern erkennbar? null = keiner der festen Anbieter.</summary>
    public static DnsProvider? Recognize(IEnumerable<string> servers)
    {
        var set = servers.Select(s => IPAddress.TryParse(s, out var ip) ? ip.ToString() : s).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Providers.FirstOrDefault(p => set.Contains(p.PrimaryV4) || set.Contains(p.SecondaryV4)
                                             || set.Contains(IPAddress.Parse(p.PrimaryV6).ToString()) || set.Contains(IPAddress.Parse(p.SecondaryV6).ToString()));
    }
}

// ----------------------------------------------------------------------------------------------- Wake-on-LAN

/// <summary>Wake-on-LAN: MAC-Eingabe prüfen, Magic Packet bauen, Broadcast-Adresse des eigenen Subnetzes berechnen.</summary>
public static class WakeOnLan
{
    public const int Port = 9;

    /// <summary>Akzeptiert AA:BB:CC:DD:EE:FF, AA-BB-CC-DD-EE-FF, AABBCCDDEEFF und AABB.CCDD.EEFF.</summary>
    public static bool TryParseMac(string? input, out byte[] mac)
    {
        mac = [];
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim();
        if (s.Length > 17) return false;
        string hex;
        if (s.Length == 17 && (IsSeparated(s, ':') || IsSeparated(s, '-'))) hex = s.Replace(":", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        else if (s.Length == 14 && s[4] == '.' && s[9] == '.') hex = s.Replace(".", "", StringComparison.Ordinal);
        else if (s.Length == 12) hex = s;
        else return false;
        if (hex.Length != 12 || !hex.All(char.IsAsciiHexDigit)) return false;
        var bytes = Convert.FromHexString(hex);
        if (bytes.All(b => b == 0x00) || bytes.All(b => b == 0xFF)) return false;
        if ((bytes[0] & 0x01) != 0) return false; // Multicast-/Gruppenadresse ist kein Gerät
        mac = bytes;
        return true;
    }

    private static bool IsSeparated(string s, char sep)
    {
        for (int i = 2; i < s.Length; i += 3) if (s[i] != sep) return false;
        return true;
    }

    public static string FormatMac(byte[] mac) => string.Join(":", mac.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));

    /// <summary>Magic Packet: 6 × 0xFF, danach 16 × die MAC-Adresse (102 Byte).</summary>
    public static byte[] BuildMagicPacket(byte[] mac)
    {
        if (mac is not { Length: 6 }) throw new ArgumentException("MAC-Adresse muss 6 Byte lang sein.", nameof(mac));
        var packet = new byte[6 + 16 * 6];
        for (int i = 0; i < 6; i++) packet[i] = 0xFF;
        for (int r = 0; r < 16; r++) Buffer.BlockCopy(mac, 0, packet, 6 + r * 6, 6);
        return packet;
    }

    /// <summary>Gerichtete Broadcast-Adresse des eigenen IPv4-Subnetzes (z. B. 192.168.1.20/24 → 192.168.1.255).</summary>
    public static IPAddress DirectedBroadcast(IPAddress localIp, int prefixLength)
    {
        if (localIp.AddressFamily != AddressFamily.InterNetwork) throw new ArgumentException("Nur IPv4.", nameof(localIp));
        if (prefixLength is < 1 or > 30) throw new ArgumentOutOfRangeException(nameof(prefixLength));
        var b = localIp.GetAddressBytes();
        var value = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        var hostMask = uint.MaxValue >> prefixLength;
        var bc = value | hostMask;
        return new IPAddress(new[] { (byte)(bc >> 24), (byte)(bc >> 16), (byte)(bc >> 8), (byte)bc });
    }
}
