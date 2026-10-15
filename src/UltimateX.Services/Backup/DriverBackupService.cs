using System.Diagnostics;
using UltimateX.Core.Backup;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Backup;

public sealed record ExportedDriver(string Package, string InfName);

public sealed record DriverExportResult(bool Success, string Message, string? Folder, IReadOnlyList<ExportedDriver> Drivers, string Output, TimeSpan Duration);

/// <summary>
/// Treiber sichern: <c>pnputil /export-driver * &lt;Laufwerk&gt;:\UltimateX-Backup\Treiber\&lt;yyyyMMdd-HHmmss&gt;</c> über den Admin-Helper.
/// Der Helper nimmt keine Pfade an – er bekommt nur Laufwerk + Zeitstempel als Token, der feste Ordner wird im CommandCatalog gebildet.
/// pnputil legt den Ordner nicht selbst an: Die App legt ihn vorher an; der Helper prüft erneut, dass er existiert und keine
/// Junction/Symlink-Kette enthält. Zurückspielen nur als Anleitung (pnputil /add-driver … /subdirs /install), nie automatisch.
/// </summary>
public sealed class DriverBackupService(BackupService backup)
{
    private const long MinFreeBytes = 1L * 1024 * 1024 * 1024;

    public async Task<DriverExportResult> ExportAsync(char driveLetter, IProgress<string>? progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var letter = char.ToUpperInvariant(driveLetter);
        string token, folder;
        try
        {
            token = CommandCatalog.MakeDriverExportToken(letter, DateTime.Now);
            folder = CommandCatalog.ResolveDriverExportFolder(token);
        }
        catch (ArgumentException ex)
        {
            return new(false, ex.Message, null, [], "", sw.Elapsed);
        }

        var root = $"{letter}:\\";
        try
        {
            var d = new DriveInfo(root);
            if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable))
                return new(false, $"Laufwerk {letter}: ist nicht bereit oder kein lokaler Datenträger.", null, [], "", sw.Elapsed);
            if (d.AvailableFreeSpace < MinFreeBytes)
                return new(false, $"Zu wenig Platz auf {letter}: (mindestens 1 GB frei nötig).", null, [], "", sw.Elapsed);
            if (Directory.Exists(folder)) return new(false, "Der Zielordner existiert bereits – bitte in einer Sekunde erneut versuchen.", null, [], "", sw.Elapsed);
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(false, "Zielordner konnte nicht angelegt werden: " + ex.Message, null, [], "", sw.Elapsed);
        }
        if (!CommandCatalog.IsReparseFreeChain(root, folder))
            return new(false, "Der Zielordner enthält eine Verknüpfung (Junction/Symlink) und wird nicht verwendet.", null, [], "", sw.Elapsed);

        AppLog.Info("Backup", "Treibersicherung gestartet", folder);
        CommandResult r;
        try
        {
            r = await ElevatedCommandRunner.RunAsync(AllowedCommand.DriverExport, token, progress, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            RemoveIfEmpty(folder);
            return new(false, ex.Message, null, [], "", sw.Elapsed);
        }

        var drivers = ListExported(folder);
        var success = r.ExitCode == 0 && drivers.Count > 0;
        if (!r.Started || (r.Cancelled && drivers.Count == 0)) RemoveIfEmpty(folder);
        var message = success
            ? $"{drivers.Count} Treiberpakete gesichert nach {folder}."
            : r.Cancelled ? (r.Started ? "Anzeige beendet – pnputil läuft im Hintergrund weiter." : r.Output)
            : $"Treibersicherung fehlgeschlagen (Exit Code {r.ExitCode}).";
        backup.AddHistory(new BackupHistoryEntry
        {
            TimeUtc = DateTime.UtcNow - sw.Elapsed,
            Kind = "Treiber",
            Name = "Treiberpakete (Driver Store)",
            Target = folder,
            Success = success,
            Duration = sw.Elapsed,
            Message = message,
        });
        return new(success, message, folder, drivers, r.Output, sw.Elapsed);
    }

    /// <summary>Gesicherte Treiber: Unterordner je Paket mit .inf-Datei.</summary>
    public static IReadOnlyList<ExportedDriver> ListExported(string folder)
    {
        var list = new List<ExportedDriver>();
        try
        {
            if (!Directory.Exists(folder)) return list;
            foreach (var inf in Directory.EnumerateFiles(folder, "*.inf", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true }))
            {
                var rel = Path.GetRelativePath(folder, Path.GetDirectoryName(inf)!);
                list.Add(new ExportedDriver(rel == "." ? "(Hauptordner)" : rel, Path.GetFileName(inf)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Backup", "Gesicherte Treiber nicht lesbar", ex.Message);
        }
        return list.OrderBy(d => d.Package, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Vorhandene Treibersicherungen auf einem Laufwerk (neueste zuerst).</summary>
    public static IReadOnlyList<string> ListDriverBackups(char driveLetter)
    {
        var dir = Path.Combine($"{char.ToUpperInvariant(driveLetter)}:\\", BackupPaths.RootFolderName, BackupPaths.DriverFolderName);
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.GetDirectories(dir)
                .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), @"^\d{8}-\d{6}$"))
                .OrderByDescending(d => d, StringComparer.Ordinal).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>Anleitung zum Zurückspielen (wird nur angezeigt, nie automatisch ausgeführt).</summary>
    public static string RestoreInstructions(string folder) =>
        "Treiber zurückspielen (z. B. nach einer Neuinstallation von Windows):\n" +
        "1. Startmenü → „Terminal (Administrator)“ oder „Eingabeaufforderung (Administrator)“ öffnen.\n" +
        "2. Diesen Befehl eingeben (Ordner ggf. anpassen):\n" +
        $"   pnputil /add-driver \"{folder}\\*.inf\" /subdirs /install\n" +
        "3. Danach neu starten. Einzelne Geräte alternativ im Geräte-Manager: Gerät → Treiber aktualisieren → „Auf meinem Computer suchen“ → diesen Ordner wählen (Unterordner einbeziehen).\n" +
        "Hinweis: Nur Treiber zurückspielen, die zu diesem PC passen. Grafiktreiber besser frisch vom Hersteller installieren.";

    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()
                && !File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                Directory.Delete(folder, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
