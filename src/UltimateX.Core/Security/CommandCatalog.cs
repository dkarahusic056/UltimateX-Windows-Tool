using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UltimateX.Core.Common;

namespace UltimateX.Core.Security;

/// <summary>
/// Abschließende Liste aller Systembefehle, die die Anwendung jemals ausführt (§67, §88).
/// Es gibt bewusst KEINE Möglichkeit, einen freien Befehlsstring auszuführen.
/// Neue Einträge erfordern Code-Review und eine Ergänzung im Threat Model.
/// </summary>
public enum AllowedCommand
{
    // --- Windows-Reparatur (Admin, über Helper) ---
    SfcScan,
    DismCheckHealth,
    DismScanHealth,
    DismRestoreHealth,
    ChkdskScan,
    ChkdskSpotFix,
    ScheduleBootDiskCheck,
    MemoryDiagnostic,

    // --- Power (Benutzer) ---
    Shutdown,
    Restart,
    AbortShutdown,

    // --- Software-Updates über winget (Benutzer; Installer fordern bei Bedarf selbst UAC an) ---
    WingetListUpgrades,
    WingetUpgrade,

    // --- Eigenes App-Update: startet msiexec mit der verifizierten MSI aus dem festen Update-Ordner ---
    InstallAppUpdate,

    // --- Interne Admin-Aktionen: werden im Helper per API ausgeführt, nicht über eine EXE ---
    ReadSmart,
    CreateRestorePoint,
    SetMachineStartup,
    InstallWindowsUpdate,

    // --- Absturzanalyse (§103): Minidumps liegen in einem nur für Administratoren lesbaren Ordner ---
    ReadMinidumps,
    AnalyzeMinidump,

    // --- v1.2 New PC Setup: Installation ausschließlich über den Windows-Paket-Manager (Benutzer; Installer fordern selbst UAC an) ---
    WingetShow,
    WingetInstall,
    WingetInstallStore,

    // --- v1.2 Microsoft 365 Apps über das offizielle Office Deployment Tool (setup.exe nach Signaturprüfung, UAC) ---
    OfficeDeploy,

    // --- v1.2 Open Source Security: ClamAV (zusätzlicher Scanner, ersetzt Defender nicht) ---
    ClamAvVersion,
    ClamAvUpdate,
    ClamAvScan,
    ClamAvScanQuarantine,

    // --- v1.3 Backup-Center (Admin, über Helper) – Umsetzung: CommandCatalog.Backup.cs ---
    BackupSystemImage,
    BackupImageVersions,
    DriverExport,
    RecoveryDriveCreate,

    // --- v1.3 Netzwerk-Werkzeugkasten, Sicherheits-Check, Aufgabenplanung, Datenschutz – Umsetzung: CommandCatalog.Network.cs ---
    DnsFlush,
    IpRenew,
    NetworkResetWinsock,
    NetworkResetIp,
    DnsSetStatic,
    DnsSetDhcp,
    Smb1Disable,
    RdpDisable,
    FirewallEnableAll,
    ScheduledTaskDisable,
    ScheduledTaskEnable,
    PrivacySetMachine,

    // --- v1.3 Zeitgesteuertes Herunterfahren/Neustarten (Benutzer) – Umsetzung: CommandCatalog.System.cs ---
    ShutdownAt,
    RestartAt,

    // --- v1.3 Geekbench (nur ein bereits installiertes Programm starten) – Umsetzung: CommandCatalog.Tools.cs ---
    GeekbenchLaunch,

    // --- v1.3 NVIDIA-Grafiktreiber (offizieller Download, Authenticode „NVIDIA Corporation“, UAC durch die App) – CommandCatalog.Tools.cs ---
    NvidiaDriverInstall,
}

/// <summary>Fest definierter, vollständig aufgelöster Befehl.</summary>
public sealed record CommandSpec(
    AllowedCommand Id,
    string FilePath,
    IReadOnlyList<string> Arguments,
    bool RequiresElevation,
    bool IsInteractive,
    string DisplayName)
{
    /// <summary>Wird im Admin-Helper per API ausgeführt (kein externer Prozess).</summary>
    public bool IsInternal { get; init; }

    /// <summary>Bereits validierter Parameter (nur für interne Aktionen relevant).</summary>
    public string? Parameter { get; init; }

    /// <summary>true = Ausgabe ist UTF-8 (z. B. winget); sonst automatische Erkennung.</summary>
    public bool Utf8Output { get; init; }
}

public static partial class CommandCatalog
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex WingetIdRegex();

    [GeneratedRegex(@"^[ED][123][A-Za-z0-9_-]{1,340}$", RegexOptions.CultureInvariant)]
    private static partial Regex StartupTokenRegex();

    // Nur ein Dateiname (kein Pfad) im Minidump-Ordner, z. B. "101026-12345-01.dmp", oder "MEMORY.DMP".
    [GeneratedRegex(@"^(?:[A-Za-z0-9_-]{1,64}\.dmp|MEMORY\.DMP)$", RegexOptions.CultureInvariant)]
    private static partial Regex DumpNameRegex();

    /// <summary>Feste Ablageorte der Windows-Absturzabbilder.</summary>
    public static string MinidumpDirectory => Path.Combine(Path.GetDirectoryName(AppPaths.System32)!, "Minidump");
    public static string MemoryDumpPath => Path.Combine(Path.GetDirectoryName(AppPaths.System32)!, "MEMORY.DMP");

    /// <summary>Löst einen validierten Dump-Namen in den festen Pfad auf (Minidump-Ordner bzw. MEMORY.DMP).</summary>
    public static string ResolveDumpPath(string name)
    {
        if (!DumpNameRegex().IsMatch(name)) throw new ArgumentException("Ungültiger Dump-Name.", nameof(name));
        return name == "MEMORY.DMP" ? MemoryDumpPath : Path.Combine(MinidumpDirectory, name);
    }

    /// <summary>Pfad zu winget (App-Ausführungsalias des Benutzers).</summary>
    public static string WingetPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");

    /// <summary>Feste Ablage des Office Deployment Tools und seiner Konfiguration (v1.2).</summary>
    public static string OfficeSetupDirectory => Path.Combine(AppPaths.UserDataRoot, "setup", "office");
    public static string OfficeSetupPath => Path.Combine(OfficeSetupDirectory, "setup.exe");
    public static string OfficeConfigPath => Path.Combine(OfficeSetupDirectory, "configuration.xml");

    /// <summary>ClamAV: Programmordner (Installation über winget) sowie eigene Datenbank/Quarantäne im Benutzerprofil.</summary>
    public static string ClamAvDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClamAV");
    public static string ClamScanPath => Path.Combine(ClamAvDirectory, "clamscan.exe");
    public static string FreshClamPath => Path.Combine(ClamAvDirectory, "freshclam.exe");
    public static string ClamAvDataRoot => Path.Combine(AppPaths.UserDataRoot, "clamav");
    public static string ClamAvDatabaseDirectory => Path.Combine(ClamAvDataRoot, "db");
    public static string ClamAvConfigPath => Path.Combine(ClamAvDataRoot, "freshclam.conf");
    public static string ClamAvQuarantineDirectory => Path.Combine(ClamAvDataRoot, "quarantine");

    /// <summary>Fester Ablageort für ein verifiziertes App-Update (kein frei wählbarer Pfad).</summary>
    public static string AppUpdateMsiPath => Path.Combine(AppPaths.UserDataRoot, "updates", "UltimateX-update.msi");

    /// <summary>
    /// Baut einen Befehl aus der Allowlist. Parameter (Laufwerk, Sekunden, Paket-ID …) werden streng validiert;
    /// ungültige Parameter führen zu <see cref="ArgumentException"/> – niemals zu einem "best effort"-Befehl.
    /// </summary>
    public static CommandSpec Build(AllowedCommand id, string? parameter = null)
    {
        string Sys(string exe) => Path.Combine(AppPaths.System32, exe);

        return id switch
        {
            AllowedCommand.SfcScan => new(id, Sys("sfc.exe"), ["/scannow"], true, false, "Systemdateien prüfen (SFC)"),
            AllowedCommand.DismCheckHealth => new(id, Sys("dism.exe"), ["/Online", "/Cleanup-Image", "/CheckHealth"], true, false, "DISM Quick Check"),
            AllowedCommand.DismScanHealth => new(id, Sys("dism.exe"), ["/Online", "/Cleanup-Image", "/ScanHealth"], true, false, "DISM Deep Scan"),
            AllowedCommand.DismRestoreHealth => new(id, Sys("dism.exe"), ["/Online", "/Cleanup-Image", "/RestoreHealth"], true, false, "DISM Windows Repair"),
            AllowedCommand.ChkdskScan => new(id, Sys("chkdsk.exe"), [Drive(parameter), "/scan"], true, false, "Datenträger prüfen (CHKDSK /scan)"),
            AllowedCommand.ChkdskSpotFix => new(id, Sys("chkdsk.exe"), [Drive(parameter), "/spotfix"], true, false, "Datenträgerfehler reparieren (CHKDSK /spotfix)"),
            // Setzt das Dirty-Bit: Windows prüft/repariert beim nächsten Start (nicht-interaktiv, sprachunabhängig).
            AllowedCommand.ScheduleBootDiskCheck => new(id, Sys("fsutil.exe"), ["dirty", "set", Drive(parameter)], true, false, "Datenträgerprüfung beim Neustart planen"),
            AllowedCommand.MemoryDiagnostic => new(id, Sys("mdsched.exe"), [], true, true, "Windows-Speicherdiagnose"),

            AllowedCommand.Shutdown => new(id, Sys("shutdown.exe"), ["/s", "/t", Seconds(parameter)], false, false, "Herunterfahren"),
            AllowedCommand.Restart => new(id, Sys("shutdown.exe"), ["/r", "/t", Seconds(parameter)], false, false, "Neustart"),
            AllowedCommand.AbortShutdown => new(id, Sys("shutdown.exe"), ["/a"], false, false, "Herunterfahren abbrechen"),

            AllowedCommand.WingetListUpgrades => new(id, WingetPath,
                ["upgrade", "--accept-source-agreements", "--disable-interactivity"], false, false, "Software-Updates suchen (winget)")
                { Utf8Output = true },
            AllowedCommand.WingetUpgrade => new(id, WingetPath,
                ["upgrade", "--id", WingetId(parameter), "--exact", "--silent", "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"],
                false, false, "Software-Update installieren (winget)")
                { Utf8Output = true },

            AllowedCommand.InstallAppUpdate => new(id, Sys("msiexec.exe"), ["/i", AppUpdateMsiPath], false, true, "ULTIMATE X aktualisieren"),

            AllowedCommand.ReadSmart => new(id, "", [], true, false, "SMART-Daten lesen") { IsInternal = true },
            AllowedCommand.CreateRestorePoint => new(id, "", [], true, false, "Wiederherstellungspunkt erstellen") { IsInternal = true },
            AllowedCommand.SetMachineStartup => new(id, "", [], true, false, "Autostart (alle Benutzer) ändern")
                { IsInternal = true, Parameter = StartupToken(parameter) },
            AllowedCommand.InstallWindowsUpdate => new(id, "", [], true, false, "Update über Windows Update installieren")
                { IsInternal = true, Parameter = UpdateId(parameter) },
            AllowedCommand.ReadMinidumps => new(id, "", [], true, false, "Absturzabbilder lesen") { IsInternal = true },
            AllowedCommand.AnalyzeMinidump => new(id, "", [], true, false, "Absturzabbild mit Debugger analysieren")
                { IsInternal = true, Parameter = DumpName(parameter) },

            AllowedCommand.WingetShow => new(id, WingetPath,
                ["show", "--id", WingetId(parameter), "--exact", "--source", "winget", "--accept-source-agreements", "--disable-interactivity"],
                false, false, "Paketinformationen abrufen (winget)")
                { Utf8Output = true },
            AllowedCommand.WingetInstall => new(id, WingetPath,
                ["install", "--id", WingetId(parameter), "--exact", "--source", "winget", "--silent",
                 "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"],
                false, false, "Software installieren (winget)")
                { Utf8Output = true },
            AllowedCommand.WingetInstallStore => new(id, WingetPath,
                ["install", "--id", StoreId(parameter), "--exact", "--source", "msstore", "--silent",
                 "--accept-source-agreements", "--accept-package-agreements", "--disable-interactivity"],
                false, false, "App aus dem Microsoft Store installieren (winget)")
                { Utf8Output = true },

            AllowedCommand.OfficeDeploy => new(id, OfficeSetupPath, ["/configure", OfficeConfigPath], true, true, "Microsoft 365 Apps installieren (Office Deployment Tool)"),

            AllowedCommand.ClamAvVersion => new(id, ClamScanPath, ["--version"], false, false, "ClamAV-Version abfragen"),
            AllowedCommand.ClamAvUpdate => new(id, FreshClamPath,
                ["--config-file=" + ClamAvConfigPath, "--datadir=" + ClamAvDatabaseDirectory, "--stdout"], false, false, "ClamAV-Signaturen aktualisieren"),
            AllowedCommand.ClamAvScan => new(id, ClamScanPath,
                ["-r", "--database=" + ClamAvDatabaseDirectory, "--infected", "--stdout", "--max-filesize=200M", "--max-scansize=400M", ClamAvExcludeOwnData, ScanTarget(parameter)],
                false, false, "Mit ClamAV prüfen"),
            AllowedCommand.ClamAvScanQuarantine => new(id, ClamScanPath,
                ["-r", "--database=" + ClamAvDatabaseDirectory, "--infected", "--stdout", "--max-filesize=200M", "--max-scansize=400M",
                 ClamAvExcludeOwnData, "--move=" + ClamAvQuarantineDirectory, ScanTarget(parameter)],
                false, false, "Mit ClamAV prüfen und Funde in Quarantäne verschieben"),

            _ => BuildExtension(id, parameter) ?? throw new ArgumentOutOfRangeException(nameof(id), id, "Befehl ist nicht in der Allowlist."),
        };
    }

    /// <summary>
    /// Eigene ClamAV-Daten (Signaturen, Quarantäne, Verlauf) nie mitscannen – sonst würden bereits isolierte Funde bei jedem
    /// Full Scan erneut gemeldet und innerhalb der Quarantäne verschoben. POSIX-Regex, passt auf „\“ und „/“.
    /// </summary>
    public const string ClamAvExcludeOwnData = @"--exclude-dir=UltimateX[\/]clamav";

    private static string Drive(string? parameter)
    {
        if (!InputValidator.TryNormalizeDriveLetter(parameter, out var letter))
            throw new ArgumentException("Ungültiger Laufwerksbuchstabe.", nameof(parameter));
        return $"{letter}:";
    }

    private static string Seconds(string? parameter)
    {
        if (!int.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var s) || !InputValidator.IsValidCountdown(s))
            throw new ArgumentException("Countdown muss zwischen 0 und 600 Sekunden liegen.", nameof(parameter));
        return s.ToString(CultureInfo.InvariantCulture);
    }

    private static string WingetId(string? parameter)
    {
        if (parameter is null || !WingetIdRegex().IsMatch(parameter))
            throw new ArgumentException("Ungültige Paket-ID.", nameof(parameter));
        return parameter;
    }

    // Microsoft-Store-Produkt-IDs: 12 Zeichen, Großbuchstaben/Ziffern (z. B. 9NKSQGP7F2NH).
    [GeneratedRegex(@"^[0-9A-Z]{12}$", RegexOptions.CultureInvariant)]
    private static partial Regex StoreIdRegex();

    private static string StoreId(string? parameter)
    {
        if (parameter is null || !StoreIdRegex().IsMatch(parameter))
            throw new ArgumentException("Ungültige Store-ID.", nameof(parameter));
        return parameter;
    }

    /// <summary>
    /// Scanziel für ClamAV: vollständiger, existierender lokaler Pfad (Ordner, Datei oder Laufwerkswurzel).
    /// Keine UNC-/Gerätepfade, keine Anführungszeichen oder Steuerzeichen, keine Optionen („-…“).
    /// Die Übergabe erfolgt als eigenes Argument ohne Shell (ArgumentList).
    /// </summary>
    public static string ScanTarget(string? parameter)
    {
        if (string.IsNullOrWhiteSpace(parameter) || parameter.Length > 260 || parameter.StartsWith('-')
            || parameter.Any(c => char.IsControl(c) || c is '"' or '*' or '?' or '<' or '>' or '|'))
            throw new ArgumentException("Ungültiger Scanpfad.", nameof(parameter));
        // UNC-/Gerätepfade in jeder Schreibweise (\\server, //server, \\?\, \\.\) ablehnen; nur vollständige lokale Pfade.
        if (parameter.StartsWith(@"\\", StringComparison.Ordinal) || parameter.StartsWith("//", StringComparison.Ordinal)
            || parameter.StartsWith(@"\/", StringComparison.Ordinal) || parameter.StartsWith(@"/\", StringComparison.Ordinal))
            throw new ArgumentException("Netzwerk- und Gerätepfade werden nicht gescannt.", nameof(parameter));
        if (!Path.IsPathFullyQualified(parameter))
            throw new ArgumentException("Der Scanpfad muss vollständig sein (z. B. C:\\Ordner).", nameof(parameter));
        string full;
        try { full = Path.GetFullPath(parameter); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            throw new ArgumentException("Ungültiger Scanpfad.", nameof(parameter), ex);
        }
        if (full.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("Netzwerk- und Gerätepfade werden nicht gescannt.", nameof(parameter));
        if (!Path.IsPathFullyQualified(full) || !(Directory.Exists(full) || File.Exists(full)))
            throw new ArgumentException("Der Scanpfad existiert nicht.", nameof(parameter));
        return full;
    }

    private static string DumpName(string? parameter)
    {
        if (parameter is null || !DumpNameRegex().IsMatch(parameter))
            throw new ArgumentException("Ungültiger Dump-Name.", nameof(parameter));
        return parameter;
    }

    private static string UpdateId(string? parameter)
    {
        if (!Guid.TryParseExact(parameter, "D", out var g))
            throw new ArgumentException("Ungültige Update-ID.", nameof(parameter));
        return g.ToString("D");
    }

    private static string StartupToken(string? parameter)
    {
        if (parameter is null || !StartupTokenRegex().IsMatch(parameter) || !TryDecodeStartupToken(parameter, out _, out _, out _))
            throw new ArgumentException("Ungültiger Autostart-Parameter.", nameof(parameter));
        return parameter;
    }

    // ---------------- Autostart-Token (HKLM, über Helper) ----------------
    // Format: <E|D><1|2|3><base64url(UTF-8-Wertname)>
    //   E/D = Enable/Disable · 1 = Run (64 Bit) · 2 = Run32 · 3 = StartupFolder (alle Benutzer)
    // Der Wertname kann Leer-/Sonderzeichen enthalten und wird deshalb kodiert übergeben.

    public static string EncodeStartupToken(bool enable, int source, string valueName)
    {
        if (source is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(source));
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(valueName)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{(enable ? 'E' : 'D')}{source}{b64}";
    }

    public static bool TryDecodeStartupToken(string token, out bool enable, out int source, out string valueName)
    {
        enable = false; source = 0; valueName = string.Empty;
        if (token.Length < 3 || token[0] is not ('E' or 'D') || token[1] is < '1' or > '3') return false;
        enable = token[0] == 'E';
        source = token[1] - '0';
        var b64 = token[2..].Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try
        {
            valueName = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(b64));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
        return valueName.Length is > 0 and <= 260 && !valueName.Any(char.IsControl);
    }

    /// <summary>
    /// Argumente für den Admin-Helper sind ausschließlich einfache Tokens (Enum-Name, Laufwerk, Run-ID, Paket-/Update-ID,
    /// Base64url). Alles mit Leer- oder Sonderzeichen wird abgelehnt, statt es zu quoten – so ist Injection ausgeschlossen.
    /// </summary>
    public static bool IsSafeToken(string arg)
    {
        if (string.IsNullOrEmpty(arg) || arg.Length > 360) return false;
        foreach (var c in arg)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is ':' or '-' or '_' or '.' or '+')) return false;
        }
        return true;
    }
}
