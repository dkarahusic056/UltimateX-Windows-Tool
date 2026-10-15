using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Network;

namespace UltimateX.Services.Security;

public sealed record ClamAvProjectCheck(
    string? LatestVersion,
    DateTime? LatestRelease,
    bool? ActivelyMaintained,
    string? License,
    bool? WindowsSupport,
    bool? Archived,
    string OfficialSource,
    string? InstalledVersion,
    bool? InstalledIsCurrent,
    IReadOnlyList<Finding> Findings,
    string? Error);

public enum ClamScanKind { Quick, Full, Custom, Usb, Downloads }

public sealed record ClamScanResult(DateTime Time, ClamScanKind Kind, string Target, long? ScannedFiles, IReadOnlyList<string> Infected, bool Quarantined,
    int ExitCode, bool Completed, string Summary);

/// <summary>
/// OPEN SOURCE SECURITY (v1.2 §27): ClamAV als ZUSÄTZLICHER Scanner. Microsoft Defender bleibt der Echtzeitschutz;
/// ULTIMATE X deaktiviert oder verändert Defender nie. Vor der Installation wird online geprüft, ob das Projekt aktiv gepflegt wird
/// (GitHub: letzter Release, Lizenz, Windows-Builds, Archiv-Status). Signaturen liegen im Benutzerprofil.
/// </summary>
public sealed partial class ClamAvService
{
    public const string ProjectApi = "https://api.github.com/repos/Cisco-Talos/clamav";
    public const string OfficialDownloads = "https://www.clamav.net/downloads";
    public const string SecurityAdvisories = "https://github.com/Cisco-Talos/clamav/security";

    [GeneratedRegex(@"^(?<path>.+?):\s+(?<name>\S.*?)\s+FOUND\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FoundRegex();

    [GeneratedRegex(@"Scanned files:\s*(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ScannedRegex();

    [GeneratedRegex(@"ClamAV\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    private static string HistoryPath => Path.Combine(CommandCatalog.ClamAvDataRoot, "scan-history.json");

    public static bool IsInstalled => File.Exists(CommandCatalog.ClamScanPath);

    public static bool HasSignatures
        => Directory.Exists(CommandCatalog.ClamAvDatabaseDirectory)
           && Directory.EnumerateFiles(CommandCatalog.ClamAvDatabaseDirectory).Any(f => f.EndsWith(".cvd", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cld", StringComparison.OrdinalIgnoreCase));

    public static DateTime? SignaturesUpdated
        => HasSignatures ? Directory.EnumerateFiles(CommandCatalog.ClamAvDatabaseDirectory).Max(File.GetLastWriteTime) : null;

    /// <summary>Online-Prüfung des Projekts (nur auf Benutzeraktion; sendet keine Daten außer der Anfrage an die GitHub-API).</summary>
    public async Task<ClamAvProjectCheck> CheckProjectAsync(CancellationToken ct)
    {
        string? installed = await InstalledVersionAsync(ct).ConfigureAwait(false);
        try
        {
            using var repoDoc = JsonDocument.Parse(await SecureHttp.GetSmallStringAsync(ProjectApi, 400_000, ct).ConfigureAwait(false));
            using var relDoc = JsonDocument.Parse(await SecureHttp.GetSmallStringAsync(ProjectApi + "/releases/latest", 800_000, ct).ConfigureAwait(false));
            var repo = repoDoc.RootElement;
            var rel = relDoc.RootElement;
            var archived = repo.TryGetProperty("archived", out var a) && a.ValueKind == JsonValueKind.True;
            var license = repo.TryGetProperty("license", out var l) && l.ValueKind == JsonValueKind.Object && l.TryGetProperty("spdx_id", out var spdx) ? spdx.GetString() : null;
            var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            DateTime? published = rel.TryGetProperty("published_at", out var p) && p.TryGetDateTime(out var pd) ? pd.ToLocalTime() : null;
            var windows = rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array
                          && assets.EnumerateArray().Any(x => x.TryGetProperty("name", out var n) && (n.GetString() ?? "").Contains("win", StringComparison.OrdinalIgnoreCase));
            var latest = tag?.TrimStart('c', 'l', 'a', 'm', 'v', '-').Trim();
            bool? active = published is { } pub ? !archived && (DateTime.Now - pub).TotalDays <= 365 : null;
            bool? current = installed is not null && latest is not null && Version.TryParse(installed, out var iv) && Version.TryParse(latest, out var lv) ? iv >= lv : null;

            var findings = new List<Finding>
            {
                new("Aktiv gepflegt", active switch { true => CheckStatus.Pass, false => CheckStatus.Fail, _ => CheckStatus.NotAvailable },
                    published is { } d ? $"Letzter Release: {d:dd.MM.yyyy}" : "N/A"),
                new("Letzte Version", latest is null ? CheckStatus.NotAvailable : CheckStatus.Pass, latest ?? "N/A"),
                new("Windows-Unterstützung", windows ? CheckStatus.Pass : CheckStatus.Warning, windows ? "Windows-Builds im Release" : "Keine Windows-Datei im letzten Release"),
                new("Lizenz", license is null ? CheckStatus.NotAvailable : CheckStatus.Pass, license ?? "N/A"),
                new("Projektstatus", archived ? CheckStatus.Fail : CheckStatus.Pass, archived ? "Archiviert" : "Aktiv"),
                new("Offizielle Quelle", CheckStatus.Pass, OfficialDownloads, "Installation über den Windows-Paket-Manager (Cisco.ClamAV) mit Quellenprüfung."),
                new("Signatur-Updates", CheckStatus.Pass, "freshclam (database.clamav.net)"),
                new("Bekannte Sicherheitsprobleme", current == false ? CheckStatus.Warning : CheckStatus.Pass,
                    current == false ? $"Installiert {installed} – aktuelle Version {latest} installieren" : "Sicherheitshinweise des Projekts beachten",
                    SecurityAdvisories),
            };
            return new ClamAvProjectCheck(latest, published, active, license, windows, archived, OfficialDownloads, installed, current, findings, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or ArgumentException)
        {
            if (ct.IsCancellationRequested) throw;
            return new ClamAvProjectCheck(null, null, null, null, null, null, OfficialDownloads, installed, null,
                [new Finding("Online-Prüfung", CheckStatus.NotAvailable, "Nicht möglich", ex.Message)], ex.Message);
        }
    }

    private static async Task<string?> InstalledVersionAsync(CancellationToken ct)
    {
        if (!IsInstalled) return null;
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.ClamAvVersion), null, ct).ConfigureAwait(false);
        return VersionRegex().Match(r.Output) is { Success: true } m ? m.Groups[1].Value : null;
    }

    /// <summary>Legt Datenbank-/Quarantäneordner und eine minimale freshclam-Konfiguration im Benutzerprofil an.</summary>
    public static void EnsureConfiguration()
    {
        Directory.CreateDirectory(CommandCatalog.ClamAvDatabaseDirectory);
        Directory.CreateDirectory(CommandCatalog.ClamAvQuarantineDirectory);
        if (!File.Exists(CommandCatalog.ClamAvConfigPath))
            File.WriteAllText(CommandCatalog.ClamAvConfigPath,
                "# ULTIMATE X – freshclam-Konfiguration (nur Signatur-Updates)\nDatabaseMirror database.clamav.net\nConnectTimeout 30\nReceiveTimeout 60\n");
    }

    public async Task<(bool Ok, string Output)> UpdateSignaturesAsync(IProgress<string>? progress, CancellationToken ct)
    {
        if (!File.Exists(CommandCatalog.FreshClamPath)) return (false, "ClamAV (freshclam.exe) ist nicht installiert.");
        EnsureConfiguration();
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.ClamAvUpdate), progress, ct).ConfigureAwait(false);
        AppLog.Info("ClamAV", "Signatur-Update", $"ExitCode={r.ExitCode}");
        return (r.ExitCode == 0 && HasSignatures, r.Output);
    }

    /// <summary>Ziele der vordefinierten Scans.</summary>
    public static IReadOnlyList<string> TargetsFor(ClamScanKind kind, string? custom)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string Downloads() => Path.Combine(profile, "Downloads");
        return kind switch
        {
            ClamScanKind.Quick => new[] { Downloads(), Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetTempPath() }
                .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            ClamScanKind.Downloads => Directory.Exists(Downloads()) ? [Downloads()] : [],
            ClamScanKind.Full => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList(),
            ClamScanKind.Usb => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.RootDirectory.FullName).ToList(),
            _ => custom is null ? [] : [custom],
        };
    }

    public async Task<ClamScanResult> ScanAsync(ClamScanKind kind, string? customPath, bool quarantine, IProgress<string>? progress, CancellationToken ct)
    {
        if (!IsInstalled) throw new InvalidOperationException("ClamAV ist nicht installiert.");
        if (!HasSignatures) throw new InvalidOperationException("Es sind noch keine ClamAV-Signaturen vorhanden – zuerst „Definitions Update“ ausführen.");
        EnsureConfiguration();
        var targets = TargetsFor(kind, customPath);
        if (targets.Count == 0) throw new InvalidOperationException(kind == ClamScanKind.Usb ? "Kein USB-Laufwerk gefunden." : "Kein Scanziel vorhanden.");

        var infected = new List<string>();
        long scanned = 0;
        var exit = 0;
        var completed = true;
        foreach (var target in targets)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Prüfe {target} …\n");
            var spec = CommandCatalog.Build(quarantine ? AllowedCommand.ClamAvScanQuarantine : AllowedCommand.ClamAvScan, target);
            var r = await ProcessRunner.RunAsync(spec, progress, ct).ConfigureAwait(false);
            if (r.Cancelled) { completed = false; break; }
            foreach (Match m in FoundRegex().Matches(r.Output)) infected.Add($"{m.Groups["path"].Value.Trim()} – {m.Groups["name"].Value.Trim()}");
            if (ScannedRegex().Match(r.Output) is { Success: true } s) scanned += long.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture);
            // clamscan: 0 = nichts gefunden, 1 = Fund, 2 = Fehler
            exit = Math.Max(exit, r.ExitCode);
        }
        var summary = !completed ? "Abgebrochen."
            : infected.Count > 0 ? $"{infected.Count} Fund(e){(quarantine ? " – in Quarantäne verschoben" : "")}. Microsoft Defender zusätzlich prüfen lassen."
            : exit >= 2 ? "Prüfung mit Fehlern beendet (einzelne Dateien nicht lesbar)." : "Keine Bedrohungen gefunden.";
        var result = new ClamScanResult(DateTime.Now, kind, string.Join("; ", targets), scanned == 0 ? null : scanned, infected, quarantine && infected.Count > 0,
            exit, completed, summary);
        SaveHistory(result);
        AppLog.Info("ClamAV", $"Scan {kind} beendet", $"Funde={infected.Count} Exit={exit}");
        return result;
    }

    public static IReadOnlyList<ClamScanResult> LoadHistory()
    {
        try
        {
            return File.Exists(HistoryPath) ? JsonSerializer.Deserialize<List<ClamScanResult>>(File.ReadAllText(HistoryPath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void SaveHistory(ClamScanResult r)
    {
        try
        {
            var list = LoadHistory().Prepend(r).Take(50).ToList();
            Directory.CreateDirectory(CommandCatalog.ClamAvDataRoot);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("ClamAV", "Scanverlauf nicht gespeichert", ex.Message);
        }
    }

    public static IReadOnlyList<string> QuarantineFiles()
        => Directory.Exists(CommandCatalog.ClamAvQuarantineDirectory) ? Directory.EnumerateFiles(CommandCatalog.ClamAvQuarantineDirectory).ToList() : [];
}
