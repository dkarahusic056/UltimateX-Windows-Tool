using System.Security.Cryptography;
using System.Text.Json;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Network;

namespace UltimateX.Services.Updates;

public sealed record AppUpdateManifest(string Version, string MsiUrl, string Sha256, string? Notes);

public sealed record AppUpdateCheck(bool UpdateAvailable, Version Current, Version? Latest, AppUpdateManifest? Manifest, string Message);

/// <summary>
/// Sichere Selbstaktualisierung (§64) – Ablauf:
///  1. Manifest per HTTPS laden (Zertifikatsprüfung durch Windows, keine Redirects, max. 64 KB).
///  2. MSI per HTTPS laden (max. 1 GB) und dabei SHA-256 berechnen.
///  3. SHA-256 mit dem Manifest vergleichen.
///  4. Authenticode-Signatur der MSI prüfen: gültig UND vom selben Zertifikat wie die laufende App.
///  5. Erst dann msiexec starten (Benutzer bestätigt vorher). Bei jedem Fehler: Abbruch, Datei wird gelöscht.
/// Unsignierte Entwicklungs-Builds installieren grundsätzlich keine Updates (Fail Securely).
/// </summary>
public sealed class AppUpdateService
{
    private const long MaxMsiBytes = 1L << 30;

    public static Version CurrentVersion => typeof(AppUpdateService).Assembly.GetName().Version ?? new Version(0, 1, 0);

    public async Task<AppUpdateCheck> CheckAsync(string manifestUrl, CancellationToken ct = default)
    {
        var current = CurrentVersion;
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return new AppUpdateCheck(false, current, null, null, "Kein Update-Server konfiguriert (Einstellungen → Updates).");

        string json;
        try
        {
            json = await SecureHttp.GetSmallStringAsync(manifestUrl, 64 * 1024, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or IOException)
        {
            return new AppUpdateCheck(false, current, null, null, "Update-Server nicht erreichbar: " + ex.Message);
        }

        AppUpdateManifest? m;
        try
        {
            m = JsonSerializer.Deserialize<AppUpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return new AppUpdateCheck(false, current, null, null, "Update-Manifest ist ungültig.");
        }

        if (m is null || !Version.TryParse(m.Version, out var latest)
            || !Uri.TryCreate(m.MsiUrl, UriKind.Absolute, out var msiUri) || msiUri.Scheme != Uri.UriSchemeHttps
            || m.Sha256 is not { Length: 64 } || !m.Sha256.All(Uri.IsHexDigit))
            return new AppUpdateCheck(false, current, null, null, "Update-Manifest ist unvollständig oder unsicher (nur HTTPS + SHA-256 erlaubt).");

        return latest > current
            ? new AppUpdateCheck(true, current, latest, m, $"Version {latest} ist verfügbar.")
            : new AppUpdateCheck(false, current, latest, m, "ULTIMATE X ist aktuell.");
    }

    /// <summary>Lädt und verifiziert das Update. Erst bei Erfolg liegt die MSI im festen Update-Ordner.</summary>
    public async Task<OperationResult> DownloadAndVerifyAsync(AppUpdateManifest manifest, IProgress<double>? progress, CancellationToken ct = default)
    {
        var self = Environment.ProcessPath;
        var selfSig = self is null ? null : SignatureVerifier.Verify(self);
        if (selfSig is not { Status: SignatureStatus.Valid })
            return OperationResult.Fail("Dies ist ein unsignierter Entwicklungs-Build. Updates werden nur für signierte Release-Versionen installiert.");

        var target = CommandCatalog.AppUpdateMsiPath;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".download";
        TryDelete(temp);
        TryDelete(target);

        try
        {
            using var response = await SecureHttp.Client.GetAsync(SecureHttp.RequireHttps(manifest.MsiUrl), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            if (total > MaxMsiBytes) return OperationResult.Fail("Update ist unplausibel groß – abgebrochen.");

            using var sha = SHA256.Create();
            await using (var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    read += n;
                    if (read > MaxMsiBytes) throw new IOException("Download überschreitet die maximale Größe.");
                    sha.TransformBlock(buffer, 0, n, null, 0);
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    if (total > 0) progress?.Report((double)read / total.Value);
                }
                sha.TransformFinalBlock([], 0, 0);
            }

            var hash = Convert.ToHexString(sha.Hash!);
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                AppLog.Security("SignatureFailure", "Update verworfen: SHA-256 stimmt nicht", LogLevel.Error);
                return OperationResult.Fail("Prüfsumme stimmt nicht – Update verworfen.");
            }

            var sig = SignatureVerifier.Verify(temp);
            if (sig.Status != SignatureStatus.Valid || !string.Equals(sig.Thumbprint, selfSig.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(temp);
                AppLog.Security("SignatureFailure", "Update verworfen: Signatur ungültig oder fremdes Zertifikat", LogLevel.Error, sig.StatusText);
                return OperationResult.Fail("Signatur des Updates ist ungültig oder stammt nicht vom Herausgeber – Update verworfen.");
            }

            File.Move(temp, target, overwrite: true);
            AppLog.Security("AdminOperation", $"Update {manifest.Version} heruntergeladen und verifiziert");
            return OperationResult.Ok("Update heruntergeladen und verifiziert (SHA-256 und Signatur).");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return OperationResult.Fail("Download fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Startet die verifizierte MSI (Windows Installer zeigt eigene Oberfläche/UAC).</summary>
    public OperationResult StartInstaller()
    {
        var path = CommandCatalog.AppUpdateMsiPath;
        if (!File.Exists(path)) return OperationResult.Fail("Kein verifiziertes Update vorhanden.");
        // Erneute Prüfung direkt vor dem Start (TOCTOU-Schutz).
        var self = Environment.ProcessPath;
        var selfSig = self is null ? null : SignatureVerifier.Verify(self);
        var sig = SignatureVerifier.Verify(path);
        if (selfSig is not { Status: SignatureStatus.Valid } || sig.Status != SignatureStatus.Valid
            || !string.Equals(sig.Thumbprint, selfSig.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            TryDelete(path);
            return OperationResult.Fail("Signaturprüfung vor dem Start fehlgeschlagen – Update verworfen.");
        }
        return ProcessRunner.StartDetached(CommandCatalog.Build(AllowedCommand.InstallAppUpdate))
            ? OperationResult.Ok("Installer gestartet.")
            : OperationResult.Fail("Installer konnte nicht gestartet werden.");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
