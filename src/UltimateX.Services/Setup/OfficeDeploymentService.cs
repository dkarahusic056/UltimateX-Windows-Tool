using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Network;

namespace UltimateX.Services.Setup;

public enum OfficeApp { Word, Excel, PowerPoint, Outlook, OneNote, Access }

public enum OfficeLanguage { German, English, SystemLanguage }

public sealed record OfficeDownloadResult(bool Ok, string Message, long? SizeBytes, string? Sha256, string? Publisher, string Url);

/// <summary>
/// Microsoft 365 Apps über das offizielle Office Deployment Tool (v1.2 §20).
///  • setup.exe wird ausschließlich von officecdn.microsoft.com (HTTPS) geladen.
///  • Vor dem Start: Authenticode-Signatur muss gültig und von „Microsoft Corporation“ sein; SHA-256 und Größe werden protokolliert.
///  • 64-Bit, gewählte Apps und Sprache über configuration.xml; die Lizenzbedingungen bestätigt der Benutzer im Office-Setup.
///  • Keine Lizenzumgehung: Die Aktivierung erfolgt mit dem eigenen Microsoft-Konto bzw. der eigenen Lizenz.
/// </summary>
public sealed class OfficeDeploymentService
{
    public const string OdtUrl = "https://officecdn.microsoft.com/pr/wsus/setup.exe";
    private const long MaxBytes = 20L * 1024 * 1024;
    private static readonly string[] AllowedHosts = ["officecdn.microsoft.com", "officecdn.microsoft.com.edgesuite.net", "microsoft.com"];

    /// <summary>Erzeugt die ODT-Konfiguration (64-Bit, Current Channel, nicht gewählte Apps ausgeschlossen).</summary>
    public static string BuildConfiguration(IReadOnlyCollection<OfficeApp> apps, OfficeLanguage language)
    {
        var lang = language switch { OfficeLanguage.German => "de-de", OfficeLanguage.English => "en-us", _ => "MatchOS" };
        var product = new XElement("Product", new XAttribute("ID", "O365ProPlusRetail"), new XElement("Language", new XAttribute("ID", lang)));
        var map = new Dictionary<OfficeApp, string>
        {
            [OfficeApp.Word] = "Word", [OfficeApp.Excel] = "Excel", [OfficeApp.PowerPoint] = "PowerPoint",
            [OfficeApp.Outlook] = "Outlook", [OfficeApp.OneNote] = "OneNote", [OfficeApp.Access] = "Access",
        };
        foreach (var (app, id) in map)
            if (!apps.Contains(app)) product.Add(new XElement("ExcludeApp", new XAttribute("ID", id)));
        // Nicht angefragte Zusatzprogramme ebenfalls ausschließen (Teams wird bei Bedarf separat installiert).
        foreach (var id in new[] { "Publisher", "Groove", "Lync", "Teams", "Bing" })
            product.Add(new XElement("ExcludeApp", new XAttribute("ID", id)));

        var doc = new XDocument(
            new XElement("Configuration",
                new XElement("Add", new XAttribute("OfficeClientEdition", "64"), new XAttribute("Channel", "Current"), product),
                new XElement("Display", new XAttribute("Level", "Full"), new XAttribute("AcceptEULA", "FALSE")),
                new XElement("Property", new XAttribute("Name", "FORCEAPPSHUTDOWN"), new XAttribute("Value", "FALSE"))));
        return doc.ToString();
    }

    /// <summary>Lädt setup.exe (nur HTTPS, nur Microsoft-Hosts, max. 20 MB) und prüft Signatur und Herausgeber.</summary>
    public async Task<OfficeDownloadResult> DownloadAndVerifyAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(CommandCatalog.OfficeSetupDirectory);
        var temp = CommandCatalog.OfficeSetupPath + ".download";
        var url = OdtUrl;
        try
        {
            for (var hop = 0; hop < 4; hop++)
            {
                var (ok, reason) = SoftwareCatalog.ValidateDownloadUrl(url, AllowedHosts);
                if (!ok) return new OfficeDownloadResult(false, "Download verweigert: " + reason, null, null, null, url);
                using var resp = await SecureHttp.Client.GetAsync(SecureHttp.RequireHttps(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if ((int)resp.StatusCode is >= 300 and < 400 && resp.Headers.Location is { } loc)
                {
                    url = (loc.IsAbsoluteUri ? loc : new Uri(new Uri(url), loc)).ToString();
                    continue; // Weiterleitung nur zu einem ebenfalls erlaubten Microsoft-Host (wird oben erneut geprüft)
                }
                resp.EnsureSuccessStatusCode();
                if (resp.Content.Headers.ContentLength is > MaxBytes)
                    return new OfficeDownloadResult(false, "Die Datei ist unerwartet groß – Download abgebrochen.", resp.Content.Headers.ContentLength, null, null, url);
                await using (var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var output = File.Create(temp))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > MaxBytes) return new OfficeDownloadResult(false, "Die Datei ist unerwartet groß – Download abgebrochen.", total, null, null, url);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    }
                }
                File.Move(temp, CommandCatalog.OfficeSetupPath, overwrite: true);
                return Verify(url);
            }
            return new OfficeDownloadResult(false, "Zu viele Weiterleitungen.", null, null, null, url);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return new OfficeDownloadResult(false, "Download fehlgeschlagen: " + ex.Message, null, null, null, url);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    /// <summary>Signatur, Herausgeber, Größe und SHA-256 der vorhandenen setup.exe.</summary>
    public static OfficeDownloadResult Verify(string url = OdtUrl)
    {
        var path = CommandCatalog.OfficeSetupPath;
        if (!File.Exists(path)) return new OfficeDownloadResult(false, "setup.exe nicht vorhanden.", null, null, null, url);
        var size = new FileInfo(path).Length;
        string sha;
        using (var fs = File.OpenRead(path)) sha = Convert.ToHexString(SHA256.HashData(fs));
        var sig = SignatureVerifier.Verify(path);
        var ok = sig.Status == SignatureStatus.Valid && SoftwareCatalog.PublisherMatches(sig.Publisher, "Microsoft Corporation");
        return new OfficeDownloadResult(ok,
            ok ? "Signatur gültig – Herausgeber: " + sig.Publisher : $"Signaturprüfung fehlgeschlagen ({sig.StatusText}, {sig.Publisher ?? "kein Herausgeber"}) – Datei wird nicht ausgeführt.",
            size, sha, sig.Publisher, url);
    }

    /// <summary>
    /// Schreibt die Konfiguration und startet das Office Deployment Tool mit UAC. Unmittelbar vor dem Start wird die Signatur erneut geprüft.
    /// Liefert den Exit-Code (0 = erfolgreich) oder null, wenn nicht gestartet.
    /// </summary>
    public async Task<(int? ExitCode, string Message)> InstallAsync(IReadOnlyCollection<OfficeApp> apps, OfficeLanguage language, CancellationToken ct)
    {
        // Erst die Konfiguration schreiben, dann setup.exe und configuration.xml mit FileShare.Read sperren (kein Ersetzen,
        // Umbenennen oder Löschen durch andere Prozesse mehr möglich), DANN prüfen und starten. Die Sperren bleiben bis nach
        // dem Start bestehen – so gibt es zwischen Prüfung und erhöhtem Start keine Lücke (TOCTOU, THREAT_MODEL T-35).
        if (!File.Exists(CommandCatalog.OfficeSetupPath))
        {
            var missing = Verify();
            Log(missing, null, false, missing.Message);
            return (null, missing.Message);
        }
        var config = BuildConfiguration(apps, language);
        await File.WriteAllTextAsync(CommandCatalog.OfficeConfigPath, config, ct).ConfigureAwait(false);
        FileStream? exeLock = null, configLock = null;
        try
        {
            exeLock = new FileStream(CommandCatalog.OfficeSetupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            configLock = new FileStream(CommandCatalog.OfficeConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            exeLock?.Dispose();
            const string busy = "setup.exe bzw. die Konfiguration ist durch einen anderen Prozess geöffnet – Abbruch.";
            Log(new OfficeDownloadResult(false, busy, null, null, null, OdtUrl), null, false, busy);
            return (null, busy);
        }
        using var exeLockScope = exeLock;
        using var configLockScope = configLock;
        using (var reader = new StreamReader(configLock, leaveOpen: true))
        {
            if (!string.Equals(await reader.ReadToEndAsync(ct).ConfigureAwait(false), config, StringComparison.Ordinal))
                return (null, "Die Office-Konfiguration wurde verändert – Abbruch.");
        }
        var check = Verify();
        if (!check.Ok)
        {
            Log(check, null, false, check.Message);
            return (null, check.Message);
        }
        var spec = CommandCatalog.Build(AllowedCommand.OfficeDeploy);
        var psi = new ProcessStartInfo(spec.FilePath) { UseShellExecute = true, Verb = "runas", WorkingDirectory = CommandCatalog.OfficeSetupDirectory };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);
        try
        {
            AppLog.Security("AdminOperation", "UAC-Anforderung für: " + spec.DisplayName);
            using var p = Process.Start(psi);
            if (p is null) return (null, "Das Office Deployment Tool konnte nicht gestartet werden.");
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
            var msg = p.ExitCode == 0 ? "Microsoft 365 Apps wurden installiert. Bitte mit dem eigenen Microsoft-Konto anmelden/aktivieren."
                : $"Das Office-Setup meldet Code {p.ExitCode}.";
            Log(check, p.ExitCode, p.ExitCode == 0, msg);
            return (p.ExitCode, msg);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log(check, null, false, "UAC abgelehnt");
            return (null, "Die Administratorfreigabe wurde abgelehnt.");
        }
    }

    private static void Log(OfficeDownloadResult check, int? exit, bool ok, string message)
        => InstallLogService.Add(new InstallLogEntry(DateTime.Now, "Microsoft 365 Apps (Office Deployment Tool)", null, check.Publisher,
            "Microsoft (officecdn.microsoft.com)", check.Url, check.Ok ? "Authenticode gültig (Microsoft Corporation)" : "ungültig", check.Sha256, exit, ok, message));
}
