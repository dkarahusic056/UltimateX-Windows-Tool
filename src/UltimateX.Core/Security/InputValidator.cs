using System.Net;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Security;

/// <summary>
/// Validierung aller Benutzereingaben, bevor sie irgendwo verwendet werden (§54 Input Validation, §67).
/// Grundsatz: Allowlist statt Blocklist – nur explizit erlaubte Formen werden akzeptiert.
/// </summary>
public static partial class InputValidator
{
    // RFC-1123-Hostname: Labels a-z0-9 und '-', max. 63 Zeichen, gesamt max. 253.
    [GeneratedRegex(@"^(?=.{1,253}$)(?!-)[A-Za-z0-9-]{1,63}(?<!-)(\.(?!-)[A-Za-z0-9-]{1,63}(?<!-))*\.?$", RegexOptions.CultureInvariant)]
    private static partial Regex HostnameRegex();

    /// <summary>Akzeptiert nur IPv4/IPv6-Adressen oder syntaktisch gültige Hostnamen.</summary>
    public static bool TryNormalizeHost(string? input, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var candidate = input.Trim();
        if (candidate.Length > 253) return false;

        if (IPAddress.TryParse(candidate, out var ip))
        {
            host = ip.ToString();
            return true;
        }

        if (!HostnameRegex().IsMatch(candidate)) return false;
        host = candidate.TrimEnd('.').ToLowerInvariant();
        return true;
    }

    /// <summary>Laufwerksbuchstabe A–Z, akzeptiert "C", "C:" oder "C:\".</summary>
    public static bool TryNormalizeDriveLetter(string? input, out char letter)
    {
        letter = '\0';
        if (string.IsNullOrWhiteSpace(input)) return false;
        var s = input.Trim().TrimEnd('\\').TrimEnd(':');
        if (s.Length != 1) return false;
        var c = char.ToUpperInvariant(s[0]);
        if (c is < 'A' or > 'Z') return false;
        letter = c;
        return true;
    }

    /// <summary>Countdown für Shutdown/Neustart: 0–600 Sekunden.</summary>
    public static bool IsValidCountdown(int seconds) => seconds is >= 0 and <= 600;

    /// <summary>GUID im Format "N" (32 Hex-Zeichen) – wird für Helper-Run-IDs genutzt.</summary>
    public static bool IsValidRunId(string? id) => id is { Length: 32 } && Guid.TryParseExact(id, "N", out _);
}
