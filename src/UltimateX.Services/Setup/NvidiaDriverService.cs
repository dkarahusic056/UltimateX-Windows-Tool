using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Network;

namespace UltimateX.Services.Setup;

/// <summary>Erkannte NVIDIA-Grafikkarte mit installierter Treiberversion (NVIDIA-Schreibweise, z. B. 616.92).</summary>
public sealed record NvidiaGpu(string Name, string? WindowsDriverVersion, string? DriverVersion, DateTime? DriverDate);

/// <summary>Ergebnis der Treibersuche: Karte, Produkt bei NVIDIA, neuester Treiber und ob er neuer ist.</summary>
public sealed record NvidiaDriverCheck(NvidiaGpu? Gpu, NvidiaProduct? Product, NvidiaDriverInfo? Latest, bool UpdateAvailable, string Message);

public sealed record NvidiaDownloadResult(bool Ok, string Message, long? SizeBytes, string? Sha256, string? Publisher, string Url);

/// <summary>
/// v1.3 NVIDIA-Grafiktreiber automatisch finden und installieren.
///  • Erkennung: Win32_VideoController (PCI-Hersteller 10DE), installierte Version aus der Windows-Treiberversion.
///  • Suche: offizielle NVIDIA-Treibersuche (Produktliste von www.nvidia.com, Abfrage gfwsl.geforce.com) – Game Ready, WHQL, DCH, Windows 10/11 64-Bit.
///  • Download nur über HTTPS von *.download.nvidia.com (auch nach Weiterleitungen), höchstens 2 GB, in einen festen Ordner.
///  • Vor dem Start: Authenticode gültig und Herausgeber „NVIDIA Corporation“; Datei bis nach dem Start gegen Austausch gesperrt;
///    Start per UAC durch die App (nie durch den Admin-Helper). Der NVIDIA-Installer zeigt seine eigene Oberfläche.
/// </summary>
public sealed class NvidiaDriverService
{
    private const long MaxBytes = 2L * 1024 * 1024 * 1024;
    private const string Publisher = "NVIDIA Corporation";
    private static readonly string[] LookupHosts = ["www.nvidia.com", "gfwsl.geforce.com"];
    private static readonly HttpClient DownloadClient = CreateDownloadClient();

    private static HttpClient CreateDownloadClient()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 },
            AutomaticDecompression = DecompressionMethods.None,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        // Große Datei (~1 GB): kein Gesamt-Timeout; abgebrochen wird über das CancellationToken.
        var c = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("UltimateX-WindowsControl/1.3");
        return c;
    }

    /// <summary>Erste NVIDIA-Grafikkarte im System oder null.</summary>
    public NvidiaGpu? DetectGpu()
    {
        var r = Wmi.Query("SELECT Name, DriverVersion, DriverDate, PNPDeviceID FROM Win32_VideoController");
        foreach (var row in r.Rows)
        {
            var name = row.Str("Name");
            if (!NvidiaDriver.IsNvidia(row.Str("PNPDeviceID"), name) || name is null) continue;
            var win = row.Str("DriverVersion");
            return new NvidiaGpu(name.Trim(), win, NvidiaDriver.FromWindowsDriverVersion(win), row.Date("DriverDate"));
        }
        return null;
    }

    /// <summary>Sucht den neuesten Treiber für die erkannte Karte (zwei kleine HTTPS-Abfragen bei NVIDIA).</summary>
    public async Task<NvidiaDriverCheck> CheckAsync(CancellationToken ct)
    {
        var gpu = await Task.Run(DetectGpu, ct).ConfigureAwait(false);
        if (gpu is null) return new NvidiaDriverCheck(null, null, null, false, "Keine NVIDIA-Grafikkarte erkannt.");
        try
        {
            var xml = await SecureHttp.GetSmallStringAsync(Checked(NvidiaDriver.ProductListUrl), 2_000_000, ct).ConfigureAwait(false);
            var product = NvidiaDriver.FindProduct(NvidiaDriver.ParseProductList(xml), gpu.Name);
            if (product is null)
                return new NvidiaDriverCheck(gpu, null, null, false, $"{gpu.Name} wurde in NVIDIAs Treibersuche nicht eindeutig gefunden.");
            var json = await SecureHttp.GetSmallStringAsync(Checked(NvidiaDriver.LookupUrl(product)), 500_000, ct).ConfigureAwait(false);
            var latest = NvidiaDriver.ParseLookup(json);
            if (latest is null) return new NvidiaDriverCheck(gpu, product, null, false, "NVIDIA lieferte keinen gültigen Treiber.");
            var newer = NvidiaDriver.IsNewer(latest.Version, gpu.DriverVersion);
            AppLog.Info("NVIDIA", "Treibersuche", $"{gpu.Name}: installiert {gpu.DriverVersion ?? "–"}, neuester {latest.Version}");
            return new NvidiaDriverCheck(gpu, product, latest, newer,
                newer ? $"Neuer Treiber verfügbar: {latest.Version} (installiert: {gpu.DriverVersion ?? "unbekannt"})."
                      : $"Treiber ist aktuell ({gpu.DriverVersion}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or ArgumentException)
        {
            if (ct.IsCancellationRequested) throw;
            AppLog.Warn("NVIDIA", "Treibersuche fehlgeschlagen", ex.Message);
            return new NvidiaDriverCheck(gpu, null, null, false, "Treibersuche nicht möglich: " + ex.Message);
        }
    }

    private static string Checked(string url)
    {
        var (ok, reason) = SoftwareCatalog.ValidateDownloadUrl(url, LookupHosts);
        return ok ? url : throw new ArgumentException(reason);
    }

    /// <summary>Lädt das Treiberpaket (nur *.download.nvidia.com) und prüft Signatur und Herausgeber.</summary>
    public async Task<NvidiaDownloadResult> DownloadAndVerifyAsync(NvidiaDriverInfo driver, IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(CommandCatalog.NvidiaDriverDirectory);
        var temp = CommandCatalog.NvidiaDriverPath + ".download";
        var url = driver.DownloadUrl;
        try
        {
            for (var hop = 0; hop < 4; hop++)
            {
                if (!NvidiaDriver.IsOfficialDownload(url))
                    return new NvidiaDownloadResult(false, "Download verweigert: keine offizielle NVIDIA-Adresse (" + url + ").", null, null, null, url);
                using var resp = await DownloadClient.GetAsync(SecureHttp.RequireHttps(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
                {
                    url = (loc.IsAbsoluteUri ? loc : new Uri(new Uri(url), loc)).ToString();
                    continue; // Weiterleitung wird oben erneut geprüft
                }
                resp.EnsureSuccessStatusCode();
                var length = resp.Content.Headers.ContentLength;
                if (length is > MaxBytes) return new NvidiaDownloadResult(false, "Die Datei ist unerwartet groß – Download abgebrochen.", length, null, null, url);
                await using (var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var output = File.Create(temp))
                {
                    var buffer = new byte[1 << 20];
                    long total = 0;
                    int read;
                    var lastReport = 0L;
                    while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxBytes) return new NvidiaDownloadResult(false, "Die Datei ist unerwartet groß – Download abgebrochen.", total, null, null, url);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        if (length is > 0 && total - lastReport > 8 << 20) { lastReport = total; progress?.Report(total * 100.0 / length.Value); }
                    }
                    if (length is > 0 && total != length) return new NvidiaDownloadResult(false, "Download unvollständig.", total, null, null, url);
                }
                File.Move(temp, CommandCatalog.NvidiaDriverPath, overwrite: true);
                progress?.Report(100);
                return Verify(url);
            }
            return new NvidiaDownloadResult(false, "Zu viele Weiterleitungen.", null, null, null, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return new NvidiaDownloadResult(false, "Download fehlgeschlagen: " + ex.Message, null, null, null, url);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>Signatur, Herausgeber, Größe und SHA-256 des geladenen Treiberpakets.</summary>
    public static NvidiaDownloadResult Verify(string url)
    {
        var path = CommandCatalog.NvidiaDriverPath;
        if (!File.Exists(path)) return new NvidiaDownloadResult(false, "Treiberpaket nicht vorhanden.", null, null, null, url);
        var size = new FileInfo(path).Length;
        string sha;
        using (var fs = File.OpenRead(path)) sha = Convert.ToHexString(SHA256.HashData(fs));
        var sig = SignatureVerifier.Verify(path);
        var ok = sig.Status == SignatureStatus.Valid && SoftwareCatalog.PublisherMatches(sig.Publisher, Publisher);
        return new NvidiaDownloadResult(ok,
            ok ? "Signatur gültig – Herausgeber: " + sig.Publisher
               : $"Signaturprüfung fehlgeschlagen ({sig.StatusText}, {sig.Publisher ?? "kein Herausgeber"}) – Datei wird nicht ausgeführt.",
            size, sha, sig.Publisher, url);
    }

    /// <summary>
    /// Startet den NVIDIA-Installer per UAC. Die Datei wird vorher gesperrt (kein Austausch zwischen Prüfung und Start)
    /// und unmittelbar vor dem Start erneut geprüft. Liefert den Exit-Code oder null, wenn nicht gestartet.
    /// </summary>
    public async Task<(int? ExitCode, string Message)> InstallAsync(NvidiaDriverInfo driver, CancellationToken ct)
    {
        if (!File.Exists(CommandCatalog.NvidiaDriverPath)) return (null, "Treiberpaket nicht vorhanden – bitte zuerst herunterladen.");
        FileStream? exeLock;
        try { exeLock = new FileStream(CommandCatalog.NvidiaDriverPath, FileMode.Open, FileAccess.Read, FileShare.Read); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, "Das Treiberpaket ist durch einen anderen Prozess geöffnet – Abbruch.");
        }
        using var lockScope = exeLock;
        var check = Verify(driver.DownloadUrl);
        if (!check.Ok)
        {
            Log(driver, check, null, false, check.Message);
            return (null, check.Message);
        }
        var spec = CommandCatalog.Build(AllowedCommand.NvidiaDriverInstall);
        var psi = new ProcessStartInfo(spec.FilePath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = CommandCatalog.NvidiaDriverDirectory };
        try
        {
            AppLog.Security("AdminOperation", "UAC-Anforderung für: " + spec.DisplayName, details: driver.Version);
            using var p = Process.Start(psi);
            if (p is null) return (null, "Der NVIDIA-Installer konnte nicht gestartet werden.");
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            // Das Paket entpackt sich und startet das eigentliche Setup; Exit-Code 0 = erfolgreich bzw. vom Benutzer beendet.
            var msg = p.ExitCode == 0
                ? $"NVIDIA-Installer {driver.Version} beendet. Ggf. Neustart durchführen."
                : $"Der NVIDIA-Installer meldet Code {p.ExitCode}.";
            Log(driver, check, p.ExitCode, p.ExitCode == 0, msg);
            return (p.ExitCode, msg);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log(driver, check, null, false, "UAC abgelehnt");
            return (null, "Die Administratorfreigabe wurde abgelehnt.");
        }
    }

    private static void Log(NvidiaDriverInfo d, NvidiaDownloadResult check, int? exit, bool ok, string message)
        => InstallLogService.Add(new InstallLogEntry(DateTime.Now, "NVIDIA-Grafiktreiber " + (d.Name ?? "Game Ready"), d.Version, check.Publisher,
            "NVIDIA (download.nvidia.com)", check.Url, check.Ok ? "Authenticode gültig (NVIDIA Corporation)" : "ungültig", check.Sha256, exit, ok, message));
}
