using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Network;

public enum SpeedtestPhase { Latency, Download, Upload, Done }

public sealed record SpeedtestProgress(SpeedtestPhase Phase, double CurrentBitsPerSecond, double Fraction);

public sealed record SpeedtestResult(DateTime Time, double DownloadBps, double UploadBps, double PingMs, double JitterMs, double? PacketLossPercent, string Server);

/// <summary>
/// Eigener Speedtest (§23) über HTTPS gegen das öffentliche Cloudflare-Speedtest-Backend.
/// - Latenz/Jitter: 10 leere HTTPS-Anfragen über eine bestehende Verbindung.
/// - Download/Upload: 4 parallele Streams, feste Messdauer.
/// - Paketverlust: ICMP-Ping (20 Pakete) an 1.1.1.1.
/// Datenverbrauch typ. 100–500 MB, je nach Anschluss – die UI weist vor dem ersten Test darauf hin.
///
/// Hinweis Release: Für einen kommerziellen Vertrieb Nutzungsbedingungen des Backends prüfen
/// oder einen eigenen Messserver betreiben (Endpunkt ist hier zentral konfigurierbar).
/// </summary>
public sealed class SpeedtestService
{
    public const string BaseUrl = "https://speed.cloudflare.com";
    private const int Streams = 4;
    private static readonly TimeSpan DownloadDuration = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan UploadDuration = TimeSpan.FromSeconds(8);

    private static string HistoryFile => Path.Combine(AppPaths.UserDataRoot, "speedtest-history.json");

    public async Task<SpeedtestResult> RunAsync(IProgress<SpeedtestProgress>? progress, CancellationToken ct)
    {
        var client = SecureHttp.Client;

        // ---- Latenz ----
        progress?.Report(new(SpeedtestPhase.Latency, 0, 0));
        var latencies = new List<double>();
        for (int i = 0; i < 11; i++)
        {
            var sw = Stopwatch.StartNew();
            using var resp = await client.GetAsync(SecureHttp.RequireHttps($"{BaseUrl}/__down?bytes=0"), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();
            resp.EnsureSuccessStatusCode();
            if (i > 0) latencies.Add(sw.Elapsed.TotalMilliseconds); // erste Anfrage = Verbindungsaufbau, nicht werten
            progress?.Report(new(SpeedtestPhase.Latency, 0, i / 10.0));
        }
        var ping = latencies.Min();
        var jitter = latencies.Zip(latencies.Skip(1), (a, b) => Math.Abs(b - a)).Average();

        // ---- Download ----
        long downloaded = 0;
        var dlWatch = Stopwatch.StartNew();
        using (var dlCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            dlCts.CancelAfter(DownloadDuration);
            var reporter = ReportLoopAsync(() => Interlocked.Read(ref downloaded), dlWatch, DownloadDuration, SpeedtestPhase.Download, progress, dlCts.Token);
            var tasks = Enumerable.Range(0, Streams).Select(_ => DownloadLoopAsync(client, b => Interlocked.Add(ref downloaded, b), dlCts.Token)).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            await reporter.ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        var downBps = downloaded * 8 / dlWatch.Elapsed.TotalSeconds;

        // ---- Upload ----
        long uploaded = 0;
        var ulWatch = Stopwatch.StartNew();
        var payload = new byte[2 * 1024 * 1024];
        Random.Shared.NextBytes(payload);
        using (var ulCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            ulCts.CancelAfter(UploadDuration);
            var reporter = ReportLoopAsync(() => Interlocked.Read(ref uploaded), ulWatch, UploadDuration, SpeedtestPhase.Upload, progress, ulCts.Token);
            var tasks = Enumerable.Range(0, Streams).Select(_ => UploadLoopAsync(client, payload, b => Interlocked.Add(ref uploaded, b), ulCts.Token)).ToList();
            await Task.WhenAll(tasks).ConfigureAwait(false);
            await reporter.ConfigureAwait(false);
        }
        ct.ThrowIfCancellationRequested();
        var upBps = uploaded * 8 / ulWatch.Elapsed.TotalSeconds;

        // ---- Paketverlust ----
        double? loss = null;
        try
        {
            var p = await new NetworkService().PingAsync("1.1.1.1", 20, 1500, ct).ConfigureAwait(false);
            loss = p.LossPercent;
        }
        catch (System.Net.NetworkInformation.PingException) { }

        var result = new SpeedtestResult(DateTime.Now, downBps, upBps, ping, jitter, loss, "speed.cloudflare.com");
        progress?.Report(new(SpeedtestPhase.Done, 0, 1));
        SaveToHistory(result);
        AppLog.Info("Speedtest", "Speedtest durchgeführt",
            $"Down={downBps / 1e6:0.0} Mbit/s, Up={upBps / 1e6:0.0} Mbit/s, Ping={ping:0} ms");
        return result;
    }

    private static async Task ReportLoopAsync(Func<long> bytes, Stopwatch sw, TimeSpan duration, SpeedtestPhase phase,
        IProgress<SpeedtestProgress>? progress, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(250, ct).ConfigureAwait(false);
                var s = sw.Elapsed.TotalSeconds;
                progress?.Report(new(phase, s > 0 ? bytes() * 8 / s : 0, Math.Min(1, sw.Elapsed / duration)));
            }
        }
        catch (OperationCanceledException) { }
    }

    private static async Task DownloadLoopAsync(HttpClient client, Action<long> add, CancellationToken ct)
    {
        var buffer = new byte[81920];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var resp = await client.GetAsync(SecureHttp.RequireHttps($"{BaseUrl}/__down?bytes=25000000"), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                int read;
                while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0) add(read);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) when (ct.IsCancellationRequested) { }
        catch (IOException) when (ct.IsCancellationRequested) { }
    }

    private static async Task UploadLoopAsync(HttpClient client, byte[] payload, Action<long> add, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var content = new ByteArrayContent(payload);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                using var resp = await client.PostAsync(SecureHttp.RequireHttps($"{BaseUrl}/__up"), content, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();
                add(payload.Length);
            }
        }
        catch (OperationCanceledException) { }
        catch (HttpRequestException) when (ct.IsCancellationRequested) { }
        catch (IOException) when (ct.IsCancellationRequested) { }
    }

    // ---- Historie (SQLite, §71) ----

    private readonly Data.HistoryDatabase _db = new();
    private bool _imported;

    public IReadOnlyList<SpeedtestResult> LoadHistory()
    {
        ImportLegacyJsonOnce();
        return _db.GetSpeedtests()
            .Select(r => new SpeedtestResult(r.Time, r.DownloadBps, r.UploadBps, r.PingMs, r.JitterMs, r.PacketLossPercent, r.Server))
            .ToList();
    }

    private void SaveToHistory(SpeedtestResult r)
        => _db.AddSpeedtest(new Data.SpeedtestRecord(r.Time, r.DownloadBps, r.UploadBps, r.PingMs, r.JitterMs, r.PacketLossPercent, r.Server));

    /// <summary>Übernimmt einen Verlauf aus älteren Versionen (JSON) einmalig in die Datenbank.</summary>
    private void ImportLegacyJsonOnce()
    {
        if (_imported) return;
        _imported = true;
        if (!File.Exists(HistoryFile)) return;
        try
        {
            var legacy = JsonSerializer.Deserialize<List<SpeedtestResult>>(File.ReadAllText(HistoryFile)) ?? [];
            foreach (var r in legacy) SaveToHistory(r);
            File.Move(HistoryFile, HistoryFile + ".imported", overwrite: true);
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
    }
}
