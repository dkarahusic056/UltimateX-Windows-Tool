using System.Net;
using System.Security.Authentication;

namespace UltimateX.Services.Network;

/// <summary>
/// Zentraler HttpClient (§65): nur HTTPS, TLS 1.2/1.3, Standard-Zertifikatsprüfung durch Windows.
/// Es gibt KEINEN ServerCertificateCustomValidationCallback – Zertifikatsfehler werden nie ignoriert.
/// </summary>
public static class SecureHttp
{
    public static HttpClient Client { get; } = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 },
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false, // keine stillen Umleitungen auf andere Hosts
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UltimateX-WindowsControl/0.1");
        return client;
    }

    /// <summary>Erzwingt HTTPS für jede URL, die die App aufruft.</summary>
    public static Uri RequireHttps(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Nur HTTPS-URLs sind erlaubt.", nameof(url));
        return uri;
    }

    /// <summary>Liest höchstens <paramref name="maxBytes"/> als Text (Schutz vor übergroßen/kaputten Antworten).</summary>
    public static async Task<string> GetSmallStringAsync(string url, int maxBytes, CancellationToken ct)
    {
        using var response = await Client.GetAsync(RequireHttps(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[maxBytes];
        int total = 0, read;
        while (total < maxBytes && (read = await stream.ReadAsync(buffer.AsMemory(total, maxBytes - total), ct).ConfigureAwait(false)) > 0)
            total += read;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, total).Trim();
    }
}
