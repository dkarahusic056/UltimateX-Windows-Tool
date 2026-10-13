using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using UltimateX.Core.Common;
using UltimateX.Core.Security;

namespace UltimateX.Services.Network;

/// <summary>Eine einzelne Ping-Antwort (oder ein Verlust).</summary>
public sealed record PingReplyInfo(int Sequence, DateTime Time, bool Success, long? RoundtripMs, string Status);

/// <summary>Auswertung einer Ping-Serie (v1.1 Phase 3).</summary>
public sealed record PingEvaluation(
    string Target,
    string? ResolvedAddress,
    int Sent,
    int Received,
    long? MinMs,
    double? AvgMs,
    long? MaxMs,
    double? JitterMs,
    DataState State,
    string StatusText)
{
    public int Lost => Sent - Received;
    public double LossPercent => Sent == 0 ? 0 : 100.0 * Lost / Sent;
}

/// <summary>
/// Ping über die .NET-API (System.Net.NetworkInformation.Ping) – es wird kein ping.exe, keine Shell und keine
/// Kommandozeile verwendet. Ziele werden vorher über <see cref="InputValidator.TryNormalizeHost"/> geprüft
/// (nur IPv4/IPv6 oder RFC-1123-Hostname, z. B. „fritz.box“ oder „drucker.local“).
/// </summary>
public static class LivePingService
{
    /// <summary>Sicherheitsgrenze für den Dauermodus (1 Ping/s → 24 h).</summary>
    public const int MaxContinuousPings = 86_400;

    /// <summary>Prüft und normalisiert ein Ziel. Liefert eine verständliche Fehlermeldung bei ungültiger Eingabe.</summary>
    public static bool TryValidateTarget(string? input, out string host, out string? error)
    {
        error = null;
        if (InputValidator.TryNormalizeHost(input, out host)) return true;
        error = string.IsNullOrWhiteSpace(input)
            ? "Bitte eine IP-Adresse oder einen Hostnamen eingeben."
            : "Ungültige Eingabe. Erlaubt sind IPv4/IPv6-Adressen oder Hostnamen (z. B. 192.168.178.1, fritz.box, drucker.local).";
        return false;
    }

    /// <summary>Löst einen Hostnamen auf (lokales DNS, LLMNR/mDNS über Windows). IPv4 wird bevorzugt.</summary>
    public static async Task<IPAddress?> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(4));
            var addresses = await Dns.GetHostAddressesAsync(host, timeout.Token).ConfigureAwait(false);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? addresses.FirstOrDefault();
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException or ArgumentException)
        {
            if (ct.IsCancellationRequested) throw;
            return null;
        }
    }

    /// <summary>
    /// Sendet <paramref name="count"/> Pings (null = Dauermodus bis zum Abbruch) im Abstand von <paramref name="intervalMs"/>.
    /// Jede Antwort wird sofort über <paramref name="onReply"/> gemeldet.
    /// </summary>
    public static async Task<PingEvaluation> RunAsync(string target, int? count, Action<PingReplyInfo> onReply,
        int intervalMs = 1000, int timeoutMs = 2000, CancellationToken ct = default)
    {
        if (!TryValidateTarget(target, out var host, out var error)) throw new ArgumentException(error, nameof(target));
        var total = count is { } c ? Math.Clamp(c, 1, 100) : MaxContinuousPings;
        intervalMs = Math.Clamp(intervalMs, 200, 10_000);
        timeoutMs = Math.Clamp(timeoutMs, 200, 10_000);

        var address = await ResolveAsync(host, ct).ConfigureAwait(false);
        var replies = new List<PingReplyInfo>();
        if (address is null)
        {
            var r = new PingReplyInfo(1, DateTime.Now, false, null, "Name konnte nicht aufgelöst werden");
            replies.Add(r);
            onReply(r);
            return Evaluate(host, null, replies);
        }

        using var ping = new Ping();
        for (var i = 1; i <= total; i++)
        {
            if (ct.IsCancellationRequested) break;
            PingReplyInfo info;
            try
            {
                var reply = await ping.SendPingAsync(address, TimeSpan.FromMilliseconds(timeoutMs), cancellationToken: ct).ConfigureAwait(false);
                info = reply.Status == IPStatus.Success
                    ? new PingReplyInfo(i, DateTime.Now, true, reply.RoundtripTime, "OK")
                    : new PingReplyInfo(i, DateTime.Now, false, null, StatusText(reply.Status));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (PingException ex)
            {
                info = new PingReplyInfo(i, DateTime.Now, false, null, ex.InnerException?.Message ?? ex.Message);
            }
            replies.Add(info);
            if (replies.Count > 3600) replies.RemoveAt(0); // Speicher im Dauermodus begrenzen
            onReply(info);
            if (i < total)
            {
                try { await Task.Delay(intervalMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        return Evaluate(host, address.ToString(), replies);
    }

    /// <summary>Bewertet eine Ping-Serie: erreichbar / instabil / nicht erreichbar.</summary>
    public static PingEvaluation Evaluate(string target, string? resolved, IReadOnlyList<PingReplyInfo> replies)
    {
        var ok = replies.Where(r => r.Success && r.RoundtripMs is not null).Select(r => r.RoundtripMs!.Value).ToList();
        var sent = replies.Count;
        double? jitter = ok.Count >= 2 ? ok.Zip(ok.Skip(1), (a, b) => (double)Math.Abs(b - a)).Average() : null;
        var loss = sent == 0 ? 0 : 100.0 * (sent - ok.Count) / sent;
        double? avg = ok.Count > 0 ? ok.Average() : null;

        (DataState state, string text) = sent == 0 ? (DataState.NotAvailable, "Kein Ping gesendet")
            : ok.Count == 0 ? (DataState.Problem, "NICHT ERREICHBAR – keine Antwort (Gerät aus oder Ping durch Firewall blockiert)")
            : loss >= 20 ? (DataState.Problem, $"INSTABIL – {loss:0} % Paketverlust")
            : loss > 0 ? (DataState.Warning, $"ERREICHBAR mit Verlusten ({loss:0} %)")
            : avg >= 150 ? (DataState.Warning, "ERREICHBAR – hohe Latenz")
            : jitter >= 30 ? (DataState.Warning, "ERREICHBAR – schwankende Latenz (Jitter)")
            : (DataState.Ok, "ERREICHBAR – stabile Verbindung");

        return new PingEvaluation(target, resolved, sent, ok.Count,
            ok.Count > 0 ? ok.Min() : null, avg, ok.Count > 0 ? ok.Max() : null, jitter, state, text);
    }

    private static string StatusText(IPStatus s) => s switch
    {
        IPStatus.TimedOut => "Zeitüberschreitung",
        IPStatus.DestinationHostUnreachable => "Ziel nicht erreichbar",
        IPStatus.DestinationNetworkUnreachable => "Netzwerk nicht erreichbar",
        IPStatus.TtlExpired => "TTL abgelaufen",
        IPStatus.BadDestination => "Ungültiges Ziel",
        _ => s.ToString(),
    };
}
