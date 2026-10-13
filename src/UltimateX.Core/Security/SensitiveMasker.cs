using System.Net;
using System.Net.Sockets;

namespace UltimateX.Core.Security;

/// <summary>
/// Maskierung sensibler Werte (§60). Masking ist eine reine Anzeigefunktion –
/// Reports exportieren sensible Felder standardmäßig gar nicht (§82).
/// </summary>
public static class SensitiveMasker
{
    /// <summary>"AA-BB-CC-DD-EE-FF" → "AA-BB-CC-XX-XX-XX" (Herstellerpräfix bleibt sichtbar).</summary>
    public static string MaskMac(string mac)
    {
        if (string.IsNullOrWhiteSpace(mac)) return mac;
        var parts = mac.Split('-', ':');
        if (parts.Length != 6) return new string('X', Math.Min(mac.Length, 17));
        return string.Join('-', parts.Take(3).Concat(Enumerable.Repeat("XX", 3)));
    }

    /// <summary>IPv4 "203.0.113.45" → "203.0.x.x"; IPv6 → erste 2 Gruppen.</summary>
    public static string MaskIp(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return "•••";
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = addr.GetAddressBytes();
            return $"{b[0]}.{b[1]}.x.x";
        }
        var groups = addr.ToString().Split(':');
        return string.Join(':', groups.Take(2)) + ":x:x:…";
    }

    /// <summary>Seriennummern, Hardware-IDs: nur letzte 4 Zeichen sichtbar.</summary>
    public static string MaskGeneric(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= 4) return new string('•', value.Length);
        return new string('•', Math.Min(value.Length - 4, 12)) + value[^4..];
    }
}
