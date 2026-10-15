using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Security;
using UltimateX.Services.Software;

namespace UltimateX.Services.Setup;

public sealed record PackageValidation(bool Ok, string? Version, string? Publisher, string? InstallerUrl, string? Sha256, string Reason);

public sealed record InstallOutcome(SoftwarePackage Package, bool Success, int? ExitCode, string Message, PackageValidation? Validation);

/// <summary>
/// NEW PC SETUP (v1.2 §19–§32): Umgebungsprüfung, Erkennung vorhandener Programme, Quellenprüfung und Installation
/// ausschließlich über den Windows-Paket-Manager. Vor jeder Installation werden Version, Herausgeber, Installer-URL
/// (HTTPS + offizielle Herstellerdomain) und SHA-256 aus dem Paketmanifest geprüft; winget verifiziert den Hash beim Download.
/// Es gibt keine Downloads über Suchmaschinen, Portale, Mirrors oder Filehoster.
/// </summary>
public sealed class SoftwareSetupService(ProgramService programs, HardwareService hardware, WindowsHealthService health, SecurityService security)
{
    public static bool WingetAvailable => File.Exists(CommandCatalog.WingetPath);

    /// <summary>Prüfungen vor der Installation (§19).</summary>
    public async Task<IReadOnlyList<Finding>> CheckEnvironmentAsync(CancellationToken ct)
    {
        var list = new List<Finding>();
        var os = await Task.Run(hardware.GetOs, ct).ConfigureAwait(false);
        var build = int.TryParse(os.Build, out var b) ? b : 0;
        list.Add(new Finding("Windows-Version", build >= 19041 ? CheckStatus.Pass : CheckStatus.Warning,
            $"{os.Caption} {os.DisplayVersion} (Build {os.Build})", build >= 19041 ? null : "Windows 10 ab 2004 oder Windows 11 empfohlen."));
        var arch = RuntimeInformation.OSArchitecture;
        list.Add(new Finding("Architektur", arch is Architecture.X64 or Architecture.Arm64 ? CheckStatus.Pass : CheckStatus.Fail, arch.ToString()));

        var internet = await InternetAvailableAsync(ct).ConfigureAwait(false);
        list.Add(new Finding("Internetverbindung", internet ? CheckStatus.Pass : CheckStatus.Fail, internet ? "Verbunden" : "Keine Verbindung",
            internet ? null : "Für Installationen ist eine Internetverbindung nötig."));

        list.Add(new Finding("Adminrechte", CheckStatus.Pass, Elevation.IsElevated ? "App läuft als Administrator" : "Über UAC (je Installation)",
            "Installer fordern Administratorrechte bei Bedarf selbst über die Windows-Benutzerkontensteuerung an."));

        var sys = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
        var freeGb = sys.IsReady ? sys.AvailableFreeSpace / 1024.0 / 1024 / 1024 : (double?)null;
        list.Add(freeGb is { } f
            ? new Finding("Freier Speicher", f >= 30 ? CheckStatus.Pass : f >= 10 ? CheckStatus.Warning : CheckStatus.Fail, $"{f:0} GB frei auf {sys.Name}")
            : new Finding("Freier Speicher", CheckStatus.NotAvailable, "N/A"));

        var reboot = await Task.Run(health.GetRebootStatus, ct).ConfigureAwait(false);
        list.Add(new Finding("Neustart erforderlich", reboot.RebootRequired ? CheckStatus.Warning : CheckStatus.Pass,
            reboot.RebootRequired ? string.Join("; ", reboot.Reasons) : "Nein", reboot.RebootRequired ? "Vor größeren Installationen neu starten." : null));

        var lastUpdate = await Task.Run(LastWindowsUpdate, ct).ConfigureAwait(false);
        list.Add(lastUpdate is { } lu
            ? new Finding("Windows Update", (DateTime.Now - lu).TotalDays <= 45 ? CheckStatus.Pass : CheckStatus.Warning,
                $"Letztes Update: {Format.DateText(lu)}", (DateTime.Now - lu).TotalDays > 45 ? "Windows Update ausführen." : null)
            : new Finding("Windows Update", CheckStatus.NotAvailable, "N/A"));

        var sec = await Task.Run(security.GetStatus, ct).ConfigureAwait(false);
        var av = sec.FirstOrDefault(s => s.Area.Contains("Antivirus", StringComparison.OrdinalIgnoreCase) || s.Area.Contains("Defender", StringComparison.OrdinalIgnoreCase));
        list.Add(av is null ? new Finding("Microsoft Defender", CheckStatus.NotAvailable, "N/A")
            : new Finding("Microsoft Defender", av.State == DataState.Ok ? CheckStatus.Pass : av.State == DataState.Problem ? CheckStatus.Fail : CheckStatus.Warning, av.Status, av.Detail));

        list.Add(new Finding("Windows-Paket-Manager (winget)", WingetAvailable ? CheckStatus.Pass : CheckStatus.Fail,
            WingetAvailable ? "Verfügbar" : "Nicht installiert", WingetAvailable ? null : "„App-Installer“ aus dem Microsoft Store installieren."));
        return list;
    }

    private static async Task<bool> InternetAvailableAsync(CancellationToken ct)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("1.1.1.1"), 443), cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            if (ct.IsCancellationRequested) throw;
            return false;
        }
    }

    /// <summary>Installationsdatum des neuesten Windows-Updates (Win32_QuickFixEngineering).</summary>
    public static DateTime? LastWindowsUpdate()
    {
        var r = Wmi.Query("SELECT InstalledOn FROM Win32_QuickFixEngineering");
        DateTime? best = null;
        foreach (var row in r.Rows)
        {
            var s = row.Str("InstalledOn");
            if (s is null) continue;
            if (DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
                || DateTime.TryParse(s, System.Globalization.CultureInfo.CurrentCulture, System.Globalization.DateTimeStyles.None, out d))
                if (best is null || d > best) best = d;
        }
        return best;
    }

    /// <summary>Bereits installierte Katalogpakete (Schlüssel → Version).</summary>
    public IReadOnlyDictionary<string, string?> DetectInstalled()
        => SoftwareCatalog.DetectInstalled(programs.GetPrograms().Select(p => (p.Name, p.Version)));

    /// <summary>Prüft die Quelle eines Pakets anhand des winget-Manifests (§31).</summary>
    public async Task<PackageValidation> ValidateAsync(SoftwarePackage pkg, CancellationToken ct)
    {
        if (pkg.Source == PackageSource.MicrosoftStore)
            return new PackageValidation(true, null, pkg.Publisher, null, null, "Microsoft Store (offizielle Quelle, von Microsoft geprüft und signiert)");
        if (!WingetAvailable) return new PackageValidation(false, null, null, null, null, "winget ist nicht installiert.");

        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.WingetShow, pkg.PackageId), null, ct).ConfigureAwait(false);
        if (!r.Started || r.ExitCode != 0)
            return new PackageValidation(false, null, null, null, null, "Paketinformationen nicht abrufbar: " + r.Output.Trim().Split('\n').LastOrDefault());
        var m = SoftwareCatalog.ParseWingetShow(r.Output);
        var (urlOk, urlReason) = SoftwareCatalog.ValidateDownloadUrl(m.InstallerUrl, pkg.OfficialHosts);
        if (!urlOk) return new PackageValidation(false, m.Version, m.Publisher, m.InstallerUrl, m.Sha256, urlReason);
        if (!SoftwareCatalog.IsValidSha256(m.Sha256))
            return new PackageValidation(false, m.Version, m.Publisher, m.InstallerUrl, m.Sha256, "Kein SHA-256-Hash im Paketmanifest – Installation verweigert.");
        return new PackageValidation(true, m.Version, m.Publisher, m.InstallerUrl, m.Sha256, urlReason + " · SHA-256 im Manifest");
    }

    /// <summary>Installiert ein Paket nach erfolgreicher Quellenprüfung und protokolliert das Ergebnis (§32).</summary>
    public async Task<InstallOutcome> InstallAsync(SoftwarePackage pkg, IProgress<string>? progress, CancellationToken ct)
    {
        var v = await ValidateAsync(pkg, ct).ConfigureAwait(false);
        if (!v.Ok)
        {
            InstallLogService.Add(new InstallLogEntry(DateTime.Now, pkg.Name, v.Version, v.Publisher, pkg.Source.ToString(), v.InstallerUrl,
                "nicht geprüft", v.Sha256, null, false, "Quellenprüfung fehlgeschlagen: " + v.Reason));
            return new InstallOutcome(pkg, false, null, "Nicht installiert – " + v.Reason, v);
        }

        var cmd = pkg.Source == PackageSource.MicrosoftStore ? AllowedCommand.WingetInstallStore : AllowedCommand.WingetInstall;
        progress?.Report($"{pkg.Name} wird installiert …\n");
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(cmd, pkg.PackageId), progress, ct).ConfigureAwait(false);
        var already = r.Output.Contains("already installed", StringComparison.OrdinalIgnoreCase)
                      || r.Output.Contains("bereits installiert", StringComparison.OrdinalIgnoreCase);
        var ok = r.ExitCode == 0 || already;
        var message = r.Cancelled ? "Abgebrochen."
            : already ? "Bereits installiert (keine neuere Version)."
            : ok ? "Installiert." : $"Installation fehlgeschlagen (Code {r.ExitCode}).";
        InstallLogService.Add(new InstallLogEntry(DateTime.Now, pkg.Name, v.Version, v.Publisher,
            pkg.Source == PackageSource.MicrosoftStore ? "Microsoft Store" : "winget (Paketmanifest)", v.InstallerUrl,
            pkg.Source == PackageSource.MicrosoftStore ? "Microsoft Store" : "SHA-256 durch winget geprüft; Authenticode durch Windows",
            v.Sha256, r.ExitCode, ok, message));
        return new InstallOutcome(pkg, ok, r.ExitCode, message, v);
    }
}
