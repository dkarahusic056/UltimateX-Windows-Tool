using System.ComponentModel;
using System.Diagnostics;
using UltimateX.Core.Backup;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Backup;

public sealed record SystemImageFile(string Path, string BackupSet, DateTime LastWrite, long Size);

public sealed record SystemImageRunResult(bool Success, string Message, WbadminProgress Final, string Output, TimeSpan Duration, bool Cancelled);

/// <summary>
/// System-Abbild mit Windows-Bordmitteln (wbadmin, über den Admin-Helper), Wiederherstellungslaufwerk (RecoveryDrive.exe)
/// und Durchsuchen vorhandener Abbilder (VHDX). Wiederhergestellt wird über die Windows-Wiederherstellungsumgebung.
/// </summary>
public sealed class SystemImageService(BackupService backup)
{
    public const string ImageFolderName = "WindowsImageBackup";

    /// <summary>Alle bereiten Laufwerke außer dem Systemlaufwerk (Bewertung mit <see cref="Check"/>).</summary>
    public IReadOnlyList<ImageTargetDrive> GetTargetDrives()
    {
        var list = new List<ImageTargetDrive>();
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        var sys = CommandCatalog.SystemDriveLetter;
        foreach (var d in drives)
        {
            try
            {
                var letter = char.ToUpperInvariant(d.Name[0]);
                if (letter == sys || letter is < 'A' or > 'Z') continue;
                list.Add(d.IsReady
                    ? new ImageTargetDrive(letter, d.VolumeLabel, d.DriveType, d.DriveFormat, true, d.TotalSize, d.AvailableFreeSpace)
                    : new ImageTargetDrive(letter, "", d.DriveType, "", false, 0, 0));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Belegter Platz auf dem Systemlaufwerk (null = nicht ermittelbar).</summary>
    public static long? SystemDriveUsedBytes()
    {
        try
        {
            var d = new DriveInfo(CommandCatalog.SystemDriveLetter + ":\\");
            return d.IsReady ? d.TotalSize - d.TotalFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public ImageTargetVerdict Check(char letter)
    {
        var used = SystemDriveUsedBytes();
        if (used is null) return new ImageTargetVerdict(false, "Belegter Platz des Systemlaufwerks nicht ermittelbar.", 0);
        var target = GetTargetDrives().FirstOrDefault(d => d.Letter == char.ToUpperInvariant(letter));
        return SystemImageTargetCheck.Evaluate(target, CommandCatalog.SystemDriveLetter, used.Value);
    }

    /// <summary>
    /// Erstellt ein System-Abbild aller für den Start nötigen Volumes (wbadmin start backup -allCritical) auf dem Ziellaufwerk.
    /// Abbrechen beendet nur die Anzeige – wbadmin läuft erhöht weiter (ein nicht erhöhter Prozess kann ihn nicht beenden).
    /// </summary>
    public async Task<SystemImageRunResult> CreateImageAsync(char letter, IProgress<WbadminProgress>? progress, IProgress<string>? rawOutput, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var verdict = Check(letter);
        var parser = new WbadminProgressParser();
        if (!verdict.Ok) return new SystemImageRunResult(false, verdict.Message, parser.Current, "", sw.Elapsed, false);

        var chunkProgress = new SyncProgress<string>(chunk =>
        {
            rawOutput?.Report(chunk);
            progress?.Report(parser.Feed(chunk));
        });
        AppLog.Info("Backup", "System-Abbild gestartet", $"Ziel {letter}:");
        CommandResult r;
        try
        {
            r = await ElevatedCommandRunner.RunAsync(AllowedCommand.BackupSystemImage, letter.ToString(), chunkProgress, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new SystemImageRunResult(false, ex.Message, parser.Current, "", sw.Elapsed, false);
        }
        var final = parser.Flush();
        if (r.Output.Length > 0 && final.Phase is WbadminPhase.Starting) final = FeedAll(r.Output);

        var success = r.ExitCode == 0 && !r.Cancelled && final.Phase != WbadminPhase.Failed;
        var message = r.Cancelled
            ? r.Started ? "Anzeige beendet – wbadmin läuft im Hintergrund weiter. Ergebnis später unter „Abbilder anzeigen“ prüfen." : r.Output
            : success ? $"System-Abbild auf {letter}: erfolgreich erstellt." : $"System-Abbild fehlgeschlagen (Exit Code {r.ExitCode}). Details in der Ausgabe.";
        backup.AddHistory(new BackupHistoryEntry
        {
            TimeUtc = DateTime.UtcNow - sw.Elapsed,
            Kind = "System-Abbild",
            Name = "Windows (" + CommandCatalog.SystemDriveLetter + ":)",
            Target = letter + ":",
            Success = success,
            Duration = sw.Elapsed,
            Message = message,
        });
        AppLog.Info("Backup", "System-Abbild beendet", $"{message} · Dauer {sw.Elapsed:hh\\:mm\\:ss}");
        return new SystemImageRunResult(success, message, final, r.Output, sw.Elapsed, r.Cancelled);
    }

    private static WbadminProgress FeedAll(string output)
    {
        var p = new WbadminProgressParser();
        p.Feed(output);
        return p.Flush();
    }

    /// <summary>Vorhandene Abbilder laut wbadmin (erhöht, UAC).</summary>
    public static async Task<CommandResult> GetVersionsAsync(char? letter, CancellationToken ct)
    {
        try
        {
            return await ElevatedCommandRunner.RunAsync(AllowedCommand.BackupImageVersions, letter?.ToString(), null, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            return new CommandResult(-1, ex.Message, Started: false);
        }
    }

    /// <summary>Startet den Windows-Assistenten „Wiederherstellungslaufwerk erstellen“ (UAC).</summary>
    public static Task<CommandResult> StartRecoveryDriveAsync(CancellationToken ct = default)
        => ElevatedCommandRunner.RunAsync(AllowedCommand.RecoveryDriveCreate, null, null, ct);

    /// <summary>
    /// VHDX-Dateien unter &lt;X&gt;:\WindowsImageBackup\&lt;PC&gt;\Backup …\. Windows schützt den Ordner meist mit Administratorrechten –
    /// dann liefert <paramref name="problem"/> einen Hinweis (Ordner im Explorer öffnen und Zugriff bestätigen).
    /// </summary>
    public static IReadOnlyList<SystemImageFile> FindImageFiles(char letter, out string? problem)
    {
        problem = null;
        var list = new List<SystemImageFile>();
        var root = ImageRoot(letter);
        if (!Directory.Exists(root)) { problem = $"Auf {char.ToUpperInvariant(letter)}: wurde kein System-Abbild gefunden."; return list; }
        try
        {
            foreach (var pc in Directory.GetDirectories(root))
            {
                if (new DirectoryInfo(pc).LinkTarget is not null) continue;
                foreach (var set in Directory.GetDirectories(pc, "Backup*"))
                {
                    if (new DirectoryInfo(set).LinkTarget is not null) continue;
                    foreach (var f in Directory.GetFiles(set, "*.vhdx"))
                    {
                        var fi = new FileInfo(f);
                        if (fi.LinkTarget is not null) continue;
                        list.Add(new SystemImageFile(fi.FullName, $"{Path.GetFileName(pc)} · {Path.GetFileName(set)}", fi.LastWriteTime, fi.Length));
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            problem = "Windows schützt den Abbild-Ordner (nur Administratoren). Öffnen Sie ihn im Explorer und bestätigen Sie den Zugriff – danach hier erneut suchen.";
        }
        catch (IOException ex)
        {
            problem = ex.Message;
        }
        return list.OrderByDescending(x => x.LastWrite).ToList();
    }

    public static string ImageRoot(char letter) => $"{char.ToUpperInvariant(letter)}:\\{ImageFolderName}";

    /// <summary>
    /// Öffnet (bindet) eine VHDX über die Windows-Standardaktion ein. Achtung: Windows bindet die VHDX BESCHREIBBAR ein –
    /// nur Dateien herauskopieren, nichts ändern, danach im Explorer „Auswerfen“.
    /// </summary>
    public static OperationResult OpenImageFile(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (root is not { Length: 3 } || root[1] != ':' || !full.StartsWith(ImageRoot(root[0]) + "\\", StringComparison.OrdinalIgnoreCase)
                || !full.EndsWith(".vhdx", StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
                return OperationResult.Fail("Nur VHDX-Dateien aus einem WindowsImageBackup-Ordner können geöffnet werden.");
            if (!CommandCatalog.IsReparseFreeChain(root, full)) return OperationResult.Fail("Der Pfad enthält eine Verknüpfung und wird nicht geöffnet.");
            using var p = Process.Start(new ProcessStartInfo(full) { UseShellExecute = true });
            AppLog.Info("Backup", "System-Abbild (VHDX) geöffnet", full);
            return OperationResult.Ok("Windows bindet das Abbild als Laufwerk ein. Nur Dateien herauskopieren – danach im Explorer „Auswerfen“.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    /// <summary>Öffnet einen Ordner im Explorer (keine Shell-Strings: explorer.exe mit Pfad als einzelnem Argument).</summary>
    public static OperationResult OpenFolder(string folder)
    {
        try
        {
            if (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(folder))
                return OperationResult.Fail("Ordner nicht gefunden.");
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var p = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { folder }, UseShellExecute = false });
            return OperationResult.Ok("Geöffnet.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return OperationResult.Fail("Konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
