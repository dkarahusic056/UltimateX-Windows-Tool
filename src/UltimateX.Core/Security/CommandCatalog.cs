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

            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Befehl ist nicht in der Allowlist."),
        };
    }

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
