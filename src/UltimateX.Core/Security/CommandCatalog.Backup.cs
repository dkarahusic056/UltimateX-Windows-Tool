using System.Globalization;
using System.Text.RegularExpressions;
using UltimateX.Core.Common;

namespace UltimateX.Core.Security;

/// <summary>
/// v1.3 Backup-Center. Alle Befehle laufen erhöht über den Admin-Helper und nehmen nur einfache Tokens an:
///  - BackupSystemImage: Ziel-Laufwerksbuchstabe (nicht das Systemlaufwerk)
///  - BackupImageVersions: optional Ziel-Laufwerksbuchstabe
///  - DriverExport: "&lt;Laufwerk&gt;-&lt;yyyyMMdd-HHmmss&gt;" → fester Ordner &lt;Laufwerk&gt;:\UltimateX-Backup\Treiber\&lt;Zeitstempel&gt;
///    (muss bereits existieren und darf keine Junction/Symlink-Kette enthalten – pnputil legt den Ordner nicht selbst an)
///  - RecoveryDriveCreate: kein Parameter (interaktiver Windows-Assistent)
/// </summary>
public static partial class CommandCatalog
{
    public const string BackupRootFolderName = "UltimateX-Backup";
    public const string DriverBackupFolderName = "Treiber";

    [GeneratedRegex(@"^([A-Za-z])-(\d{8}-\d{6})$", RegexOptions.CultureInvariant)]
    private static partial Regex DriverExportTokenRegex();

    /// <summary>
    /// Nur für Tests: ersetzt die Laufwerkswurzel ("X:\") für DriverExport durch einen Testordner. Wird im Admin-Helper nie gesetzt.
    /// </summary>
    internal static string? DriverExportRootOverrideForTests { get; set; }

    /// <summary>Buchstabe des Systemlaufwerks (z. B. 'C').</summary>
    public static char SystemDriveLetter
    {
        get
        {
            var root = Path.GetPathRoot(AppPaths.System32);
            return root is { Length: >= 2 } && root[1] == ':' ? char.ToUpperInvariant(root[0]) : 'C';
        }
    }

    private static CommandSpec? BuildBackupCommand(AllowedCommand id, string? parameter)
    {
        string Sys(string exe) => Path.Combine(AppPaths.System32, exe);
        return id switch
        {
            AllowedCommand.BackupSystemImage => new(id, Sys("wbadmin.exe"),
                ["start", "backup", $"-backupTarget:{ImageTarget(parameter)}:", $"-include:{SystemDriveLetter}:", "-allCritical", "-quiet"],
                true, false, "System-Abbild erstellen (wbadmin)"),
            AllowedCommand.BackupImageVersions => parameter is null
                ? new(id, Sys("wbadmin.exe"), ["get", "versions"], true, false, "System-Abbilder auflisten (wbadmin)")
                : new(id, Sys("wbadmin.exe"), ["get", "versions", $"-backupTarget:{Drive(parameter)}"], true, false, "System-Abbilder auflisten (wbadmin)"),
            AllowedCommand.DriverExport => new(id, Sys("pnputil.exe"), ["/export-driver", "*", DriverExportFolder(parameter)],
                true, false, "Treiber sichern (pnputil)"),
            AllowedCommand.RecoveryDriveCreate => parameter is null
                ? new(id, Sys("RecoveryDrive.exe"), [], true, true, "Wiederherstellungslaufwerk erstellen")
                : throw new ArgumentException("Dieser Befehl hat keine Parameter.", nameof(parameter)),
            _ => null,
        };
    }

    /// <summary>Ziel des System-Abbilds: Laufwerksbuchstabe A–Z, nie das Systemlaufwerk.</summary>
    private static char ImageTarget(string? parameter)
    {
        if (!InputValidator.TryNormalizeDriveLetter(parameter, out var letter))
            throw new ArgumentException("Ungültiger Laufwerksbuchstabe.", nameof(parameter));
        if (letter == SystemDriveLetter)
            throw new ArgumentException("Das Systemlaufwerk kann nicht Ziel des System-Abbilds sein.", nameof(parameter));
        return letter;
    }

    /// <summary>Erzeugt das Token für <see cref="AllowedCommand.DriverExport"/> (z. B. "E-20261014-153000").</summary>
    public static string MakeDriverExportToken(char driveLetter, DateTime local)
    {
        var letter = char.ToUpperInvariant(driveLetter);
        if (letter is < 'A' or > 'Z') throw new ArgumentException("Ungültiger Laufwerksbuchstabe.", nameof(driveLetter));
        return $"{letter}-{local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Löst ein DriverExport-Token in den festen Zielordner auf, ohne das Dateisystem zu prüfen
    /// (die App nutzt das, um den Ordner VOR dem Start des Helpers anzulegen).
    /// </summary>
    public static string ResolveDriverExportFolder(string? token)
    {
        if (token is null) throw new ArgumentException("Parameter fehlt.", nameof(token));
        var m = DriverExportTokenRegex().Match(token);
        if (!m.Success || !DateTime.TryParseExact(m.Groups[2].Value, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new ArgumentException("Ungültiger Parameter für die Treibersicherung.", nameof(token));
        var root = DriverExportRootOverrideForTests ?? $"{char.ToUpperInvariant(m.Groups[1].Value[0])}:\\";
        return Path.Combine(root, BackupRootFolderName, DriverBackupFolderName, m.Groups[2].Value);
    }

    /// <summary>Zielordner für pnputil: muss existieren und darf auf dem Weg ab der Laufwerkswurzel keinen Reparse-Point enthalten.</summary>
    private static string DriverExportFolder(string? parameter)
    {
        var folder = ResolveDriverExportFolder(parameter);
        var root = DriverExportRootOverrideForTests ?? Path.GetPathRoot(folder)!;
        if (!Directory.Exists(folder))
            throw new ArgumentException("Der Zielordner für die Treibersicherung existiert nicht.", nameof(parameter));
        if (!IsReparseFreeChain(root, folder))
            throw new ArgumentException("Der Zielordner enthält eine Verknüpfung (Junction/Symlink) und wird nicht verwendet.", nameof(parameter));
        return folder;
    }

    /// <summary>
    /// true, wenn <paramref name="path"/> unterhalb von <paramref name="root"/> liegt und weder er selbst noch ein Ordner dazwischen
    /// ein Reparse-Point (Junction, Symlink, Mount-Point) ist. Nicht existierende Teile gelten als unkritisch (werden noch angelegt).
    /// </summary>
    public static bool IsReparseFreeChain(string root, string path)
    {
        try
        {
            var r = Path.GetFullPath(root).TrimEnd('\\', '/');
            var p = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!p.Equals(r, StringComparison.OrdinalIgnoreCase) && !p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
            var current = p;
            while (current.Length > r.Length)
            {
                if (Directory.Exists(current) || File.Exists(current))
                {
                    if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint)) return false;
                }
                var parent = Path.GetDirectoryName(current);
                if (parent is null || parent.Length >= current.Length) break;
                current = parent;
            }
            if (r.Length > 3 && Directory.Exists(r) && File.GetAttributes(r).HasFlag(FileAttributes.ReparsePoint)) return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
