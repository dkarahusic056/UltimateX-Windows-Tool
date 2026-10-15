namespace UltimateX.Core.Security;

/// <summary>
/// Navigationsregel für das eingebettete speedtest.ch-Fenster (v1.3, WebView2): erlaubt sind ausschließlich
/// HTTPS-Adressen auf speedtest.ch bzw. Subdomains (*.speedtest.ch), Standardport, ohne Benutzerinformationen in der URL.
/// Alles andere (Werbe-/Fremdlinks, http, file:, javascript:, data:, Ähnlich-Domains wie speedtest.ch.evil.com) wird abgebrochen.
/// </summary>
public static class EmbeddedBrowserPolicy
{
    public const string SpeedtestChHome = "https://www.speedtest.ch/";

    public static bool IsAllowedSpeedtestCh(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || uri.Length > 4096) return false;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.IsNullOrEmpty(u.UserInfo)) return false;
        if (!u.IsDefaultPort) return false;
        var host = u.IdnHost.TrimEnd('.').ToLowerInvariant();
        return host == "speedtest.ch" || host.EndsWith(".speedtest.ch", StringComparison.Ordinal);
    }
}
