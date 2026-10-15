using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Backup;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Services.Backup;

public sealed record BackupRunOptions(bool StoreHashes = true, int VerifySampleSize = 20);

public sealed record BackupRunResult(
    bool Success,
    BackupVersionStatus Status,
    string Message,
    string? VersionPath,
    int Files,
    int Copied,
    int Linked,
    int LinkFallbacks,
    long BytesCopied,
    long TotalBytes,
    IReadOnlyList<BackupSkippedEntry> Skipped,
    string? VerifyResult,
    IReadOnlyList<string> DeletedVersions,
    TimeSpan Duration);

public sealed record BackupVersionInfo(string Name, string Path, DateTime Created, BackupVersionStatus? Status, int Files, long TotalBytes, long CopiedBytes)
{
    public bool IsComplete => Status is BackupVersionStatus.Completed or BackupVersionStatus.CompletedWithWarnings;
}

public sealed record RestoreRequest(string VersionPath, IReadOnlyList<BackupManifestEntry> Entries, string SelectedPrefix,
    string? DestinationFolder, RestoreConflictMode Mode);

public sealed record RestorePreview(int Files, long Bytes, int Conflicts, int Blocked, IReadOnlyList<string> BlockedReasons, string DestinationText);

public sealed record RestoreResult(int Restored, int SkippedExisting, int Replaced, int WrittenAsCopy, int Failed, IReadOnlyList<string> Problems, bool Cancelled)
{
    public string Summary => Cancelled
        ? $"Abgebrochen – {Restored} Datei(en) wiederhergestellt."
        : $"{Restored} Datei(en) wiederhergestellt · {Replaced} ersetzt · {WrittenAsCopy} als Kopie · {SkippedExisting} vorhandene behalten · {Failed} Fehler";
}

/// <summary>
/// Datei-Backup mit Versionen (ohne Adminrechte). Jede Version ist ein vollständiger, mit dem Explorer durchsuchbarer Ordner;
/// unveränderte Dateien werden per Hardlink aus der letzten vollständigen Version übernommen (nur gleiches NTFS-Volume, sonst Kopie).
/// Geöffnete/gesperrte Dateien werden übersprungen und im Bericht aufgeführt – Schattenkopien (VSS) sind ohne Adminrechte nicht möglich.
/// Gelöscht werden ausschließlich alte Versionsordner unterhalb von UltimateX-Backup\&lt;Profil&gt; gemäß Aufbewahrung.
/// </summary>
public sealed class FileBackupEngine
{
    private const int BufferSize = 1 << 20;
    private const int CloudRecallOnOpen = 0x40000;
    private const int CloudRecallOnDataAccess = 0x400000;

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Func<string, string, int> _hardLinker;
    private readonly Action<string> _recycle;

    /// <param name="hardLinker">(neuer Pfad, vorhandene Datei) → 0 bei Erfolg, sonst Win32-Fehlercode. Standard: CreateHardLink.</param>
    /// <param name="recycle">Verschiebt eine vorhandene Datei vor dem Ersetzen in den Papierkorb.</param>
    public FileBackupEngine(Func<string, string, int>? hardLinker = null, Action<string>? recycle = null)
    {
        _hardLinker = hardLinker ?? ((n, e) => BackupNative.TryCreateHardLink(n, e, out var err) ? 0 : (err == 0 ? -1 : err));
        _recycle = recycle ?? (p => Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(p,
            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin));
    }

    /// <summary>Zeitquelle für Versionsnamen (Tests).</summary>
    public Func<DateTime> Clock { get; init; } = () => DateTime.Now;

    // ======================================================================================
    // Sichern
    // ======================================================================================

    public Task<BackupRunResult> RunAsync(BackupProfile profile, string targetPath, BackupRunOptions options,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Run(profile, targetPath, options, progress, ct), CancellationToken.None);

    public BackupRunResult Run(BackupProfile profile, string targetPath, BackupRunOptions options,
        IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        BackupRunResult Fail(string msg) => new(false, BackupVersionStatus.Failed, msg, null, 0, 0, 0, 0, 0, 0, [], null, [], sw.Elapsed);

        var check = ValidateProfileAgainstTarget(profile, targetPath);
        if (check is not null) return Fail(check);

        var profileRoot = BackupPaths.ProfileRoot(targetPath, profile.FolderName);
        var driveRoot = Path.GetPathRoot(Path.GetFullPath(targetPath))!;
        try
        {
            Directory.CreateDirectory(profileRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Fail("Der Zielordner kann nicht angelegt werden: " + ex.Message);
        }
        if (!CommandCatalog.IsReparseFreeChain(driveRoot, profileRoot))
            return Fail("Der Zielordner enthält eine Verknüpfung (Junction/Symlink). Aus Sicherheitsgründen wird dort nicht gesichert.");

        // Basis: letzte vollständige Version
        var previous = ListVersions(profileRoot).FirstOrDefault(v => v.IsComplete);
        var prevManifest = previous is null ? null : LoadManifest(previous.Path);
        var prevIndex = BackupPlanner.Index(prevManifest);

        // 1) Quellen erfassen
        progress?.Report(new BackupProgress("Dateien werden erfasst", 0, 0, 0, 0, null));
        var skipped = new List<BackupSkippedEntry>();
        var files = Scan(profile, skipped, ct);
        if (ct.IsCancellationRequested) return new(false, BackupVersionStatus.Cancelled, "Abgebrochen.", null, 0, 0, 0, 0, 0, 0, skipped, null, [], sw.Elapsed);

        // 2) Planen (Kopie oder Hardlink)
        var plan = files.Select(f => BackupPlanner.Decide(f, prevIndex, profile.CompareMode,
            profile.CompareMode == BackupCompareMode.SizeTimeAndHash ? file => TryHashSource(file.FullPath) : null)).ToList();
        var canLink = IsNtfs(driveRoot);
        long bytesToWrite = plan.Where(p => p.Action == BackupFileAction.Copy || !canLink).Sum(p => p.File.Size);
        long totalBytes = files.Sum(f => f.Size);

        var free = FreeBytes(driveRoot);
        if (free is { } fb && fb < BackupPlanner.RequiredFreeBytes(bytesToWrite))
            return Fail($"Zu wenig Platz auf dem Ziel: benötigt ca. {FormatBytes(BackupPlanner.RequiredFreeBytes(bytesToWrite))}, frei {FormatBytes(fb)}.");

        // 3) Versionsordner + Manifest „läuft“
        var now = Clock();
        var name = BackupPaths.VersionName(now);
        while (Directory.Exists(Path.Combine(profileRoot, name))) { now = now.AddSeconds(1); name = BackupPaths.VersionName(now); }
        var versionDir = Path.Combine(profileRoot, name);
        Directory.CreateDirectory(versionDir);
        var manifest = new BackupManifest
        {
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            VersionName = name,
            Computer = Environment.MachineName,
            StartedUtc = DateTime.UtcNow,
            BaseVersion = previous?.Name,
            Sources = profile.Sources.Select(s => s.Path).ToList(),
            Exclusions = profile.Exclusions.ToList(),
            TotalBytes = totalBytes,
        };
        WriteManifest(versionDir, manifest);
        AppLog.Info("Backup", $"Datei-Backup gestartet: {profile.Name}", $"{files.Count} Dateien · Basis {previous?.Name ?? "keine"} · Ziel {versionDir}");

        // 4) Kopieren / verlinken
        int copied = 0, linked = 0, fallbacks = 0, done = 0;
        long bytesDone = 0, bytesCopied = 0;
        var lastReport = Stopwatch.StartNew();
        var status = BackupVersionStatus.Completed;
        string? fatal = null;
        try
        {
            foreach (var item in plan)
            {
                if (ct.IsCancellationRequested) { status = BackupVersionStatus.Cancelled; break; }
                var dest = Path.Combine(versionDir, item.File.StorePath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                BackupManifestEntry? entry = null;
                if (item.Action == BackupFileAction.Link && previous is not null && item.Previous is not null)
                {
                    var prevFile = Path.Combine(previous.Path, item.File.StorePath);
                    var prevInfo = new FileInfo(prevFile);
                    if (prevInfo.Exists && prevInfo.Length == item.Previous.Size && prevInfo.LinkTarget is null)
                    {
                        if (canLink && _hardLinker(dest, prevFile) == 0)
                        {
                            linked++;
                            entry = new BackupManifestEntry { Path = item.File.StorePath, Size = item.Previous.Size, LastWriteUtc = item.File.LastWriteUtc, Sha256 = item.Previous.Sha256, Linked = true };
                        }
                        else
                        {
                            // Fallback: unveränderte Datei aus der letzten Version kopieren (kein Zugriff auf die evtl. gesperrte Quelle nötig).
                            using var src = new FileStream(prevFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);
                            var (n, h) = CopyStream(src, dest, item.File.LastWriteUtc, options.StoreHashes, ct);
                            fallbacks++;
                            bytesCopied += n;
                            entry = new BackupManifestEntry { Path = item.File.StorePath, Size = n, LastWriteUtc = item.File.LastWriteUtc, Sha256 = h ?? item.Previous.Sha256 };
                        }
                    }
                }

                if (entry is null)
                {
                    FileStream? src = null;
                    try
                    {
                        src = OpenSource(item.File.FullPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add(new BackupSkippedEntry { Path = item.File.FullPath, Reason = DescribeOpenError(ex) });
                    }
                    if (src is not null)
                    {
                        using (src)
                        {
                            try
                            {
                                var (n, h) = CopyStream(src, dest, item.File.LastWriteUtc, options.StoreHashes, ct);
                                copied++;
                                bytesCopied += n;
                                entry = new BackupManifestEntry { Path = item.File.StorePath, Size = n, LastWriteUtc = item.File.LastWriteUtc, Sha256 = h };
                            }
                            catch (SourceReadException ex)
                            {
                                // Lesefehler der Quelle (z. B. gesperrter Bereich) → Datei überspringen, Backup läuft weiter.
                                skipped.Add(new BackupSkippedEntry { Path = item.File.FullPath, Reason = DescribeOpenError(ex.InnerException ?? ex) });
                            }
                        }
                    }
                }

                if (entry is not null) manifest.Files.Add(entry);
                done++;
                bytesDone += item.File.Size;
                if (lastReport.ElapsedMilliseconds >= 200)
                {
                    lastReport.Restart();
                    progress?.Report(new BackupProgress("Sicherung läuft", done, plan.Count, bytesDone, totalBytes, item.File.FullPath));
                }
            }
        }
        catch (OperationCanceledException)
        {
            status = BackupVersionStatus.Cancelled;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Schreibfehler am Ziel (z. B. Platte voll oder abgezogen) → Version unvollständig.
            status = BackupVersionStatus.Failed;
            fatal = "Schreibfehler am Ziel: " + ex.Message;
            AppLog.Error("Backup", "Datei-Backup abgebrochen (Schreibfehler am Ziel)", ex);
        }

        manifest.Skipped = skipped;
        manifest.CopiedBytes = bytesCopied;
        manifest.CopiedFiles = copied + fallbacks;
        manifest.LinkedFiles = linked;
        manifest.CompletedUtc = DateTime.UtcNow;
        if (status == BackupVersionStatus.Completed && skipped.Count > 0) status = BackupVersionStatus.CompletedWithWarnings;

        // 5) Prüfen
        string? verify = null;
        if (status is BackupVersionStatus.Completed or BackupVersionStatus.CompletedWithWarnings)
        {
            progress?.Report(new BackupProgress("Sicherung wird geprüft", done, plan.Count, bytesDone, totalBytes, null));
            var (ok, message) = Verify(versionDir, manifest, options.VerifySampleSize);
            verify = message;
            if (!ok) status = BackupVersionStatus.Failed;
        }
        manifest.VerifyResult = verify;
        manifest.Status = status;
        TryWriteManifest(versionDir, manifest);

        // 6) Aufbewahrung – nur nach erfolgreicher, geprüfter Sicherung
        var deleted = new List<string>();
        var success = status is BackupVersionStatus.Completed or BackupVersionStatus.CompletedWithWarnings;
        if (success)
        {
            progress?.Report(new BackupProgress("Alte Versionen werden aufgeräumt", done, plan.Count, bytesDone, totalBytes, null));
            deleted.AddRange(ApplyRetention(profileRoot, profile.KeepVersions));
        }

        var msg = status switch
        {
            BackupVersionStatus.Completed => $"Sicherung erfolgreich: {manifest.Files.Count} Dateien ({copied + fallbacks} kopiert, {linked} unverändert verlinkt).",
            BackupVersionStatus.CompletedWithWarnings => $"Sicherung abgeschlossen mit Hinweisen: {manifest.Files.Count} Dateien gesichert, {skipped.Count} übersprungen (siehe Bericht).",
            BackupVersionStatus.Cancelled => "Sicherung abgebrochen – die unvollständige Version wird nicht als Basis verwendet.",
            _ => fatal ?? verify ?? "Sicherung fehlgeschlagen.",
        };
        AppLog.Info("Backup", $"Datei-Backup {profile.Name}: {status}", $"{msg} · {verify} · Dauer {sw.Elapsed:hh\\:mm\\:ss}");
        progress?.Report(new BackupProgress("Fertig", done, plan.Count, bytesDone, totalBytes, null));
        return new BackupRunResult(success, status, msg, versionDir, manifest.Files.Count, copied + fallbacks, linked, fallbacks, bytesCopied, totalBytes,
            skipped, verify, deleted, sw.Elapsed);
    }

    public const string TargetUnavailableMessage = "Das Ziel ist nicht verfügbar (Platte angeschlossen?).";

    /// <summary>Prüft Quellen und Ziel. Rückgabe: Fehlermeldung oder null.</summary>
    public static string? ValidateProfileAgainstTarget(BackupProfile profile, string targetPath)
    {
        if (profile.Sources.Count == 0) return "Das Profil enthält keine Quellen.";
        if (!BackupPaths.IsValidFolderName(profile.FolderName)) return "Ungültiger Profilordner.";
        if (string.IsNullOrWhiteSpace(targetPath) || !Path.IsPathFullyQualified(targetPath) || targetPath.StartsWith(@"\\", StringComparison.Ordinal))
            return "Bitte ein lokales Ziel (z. B. E:\\) wählen. Netzwerkpfade werden nicht unterstützt.";
        if (PathGuard.IsProtected(targetPath)) return "Das Ziel liegt in einem geschützten Systemordner.";
        var backupRoot = BackupPaths.BackupRoot(targetPath);
        foreach (var s in profile.Sources)
        {
            if (string.IsNullOrWhiteSpace(s.Path) || !Path.IsPathFullyQualified(s.Path) || s.Path.StartsWith(@"\\", StringComparison.Ordinal))
                return $"Ungültige Quelle: {s.Path}";
            if (PathGuard.IsWithin(backupRoot, s.Path))
                return $"Das Ziel liegt innerhalb der Quelle {s.Path} – das Backup würde sich selbst sichern.";
            if (PathGuard.IsWithin(s.Path, backupRoot))
                return $"Die Quelle {s.Path} liegt im Backup-Ordner.";
        }
        // Zuletzt: Verfügbarkeit (beim Speichern eines Profils darf die Platte fehlen – BackupService.Validate ignoriert nur diese Meldung).
        if (!Directory.Exists(targetPath)) return TargetUnavailableMessage;
        return null;
    }

    private List<BackupSourceFile> Scan(BackupProfile profile, List<BackupSkippedEntry> skipped, CancellationToken ct)
    {
        var matcher = new ExclusionMatcher(profile.Exclusions);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<BackupSourceFile>();

        void AddFile(FileInfo fi)
        {
            if (fi.LinkTarget is not null) { skipped.Add(new BackupSkippedEntry { Path = fi.FullName, Reason = "Symbolischer Link – nicht gesichert" }); return; }
            var attr = (int)fi.Attributes;
            if ((attr & (CloudRecallOnDataAccess | CloudRecallOnOpen)) != 0 || fi.Attributes.HasFlag(FileAttributes.Offline))
            {
                skipped.Add(new BackupSkippedEntry { Path = fi.FullName, Reason = "Nur online verfügbar (Cloud-Datei wird nicht heruntergeladen)" });
                return;
            }
            string store;
            try { store = BackupPaths.ToStoreRelative(fi.FullName); }
            catch (ArgumentException) { skipped.Add(new BackupSkippedEntry { Path = fi.FullName, Reason = "Pfad wird nicht unterstützt" }); return; }
            if (!seen.Add(store)) return; // überlappende Quellen (z. B. OneDrive + Dokumente)
            files.Add(new BackupSourceFile(fi.FullName, store, fi.Length, fi.LastWriteTimeUtc));
        }

        foreach (var source in profile.Sources)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (source.IsFile)
                {
                    var fi = new FileInfo(source.Path);
                    if (!fi.Exists) { skipped.Add(new BackupSkippedEntry { Path = source.Path, Reason = "Quelle nicht gefunden" }); continue; }
                    AddFile(fi);
                    continue;
                }
                var root = new DirectoryInfo(source.Path);
                if (!root.Exists) { skipped.Add(new BackupSkippedEntry { Path = source.Path, Reason = "Quelle nicht gefunden" }); continue; }
                if (root.LinkTarget is not null) { skipped.Add(new BackupSkippedEntry { Path = source.Path, Reason = "Quelle ist eine Verknüpfung (Junction/Symlink) – nicht verfolgt" }); continue; }

                var stack = new Stack<(DirectoryInfo Dir, string Rel)>();
                stack.Push((root, string.Empty));
                while (stack.Count > 0)
                {
                    if (ct.IsCancellationRequested) break;
                    var (dir, rel) = stack.Pop();
                    List<FileSystemInfo> entries;
                    try
                    {
                        entries = dir.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }).ToList();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        skipped.Add(new BackupSkippedEntry { Path = dir.FullName, Reason = ex is UnauthorizedAccessException ? "Zugriff verweigert" : ex.Message });
                        continue;
                    }
                    foreach (var e in entries)
                    {
                        var r = rel.Length == 0 ? e.Name : rel + "\\" + e.Name;
                        if (matcher.IsExcluded(r)) continue;
                        if (e is DirectoryInfo d)
                        {
                            if (d.LinkTarget is not null)
                            {
                                skipped.Add(new BackupSkippedEntry { Path = d.FullName, Reason = "Verknüpfung (Junction/Symlink) – nicht verfolgt" });
                                continue;
                            }
                            stack.Push((d, r));
                        }
                        else if (e is FileInfo f)
                        {
                            if (f.Name.EndsWith(BackupPaths.PartialSuffix, StringComparison.OrdinalIgnoreCase)) continue;
                            AddFile(f);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
            {
                skipped.Add(new BackupSkippedEntry { Path = source.Path, Reason = ex.Message });
            }
        }
        return files;
    }

    private static FileStream OpenSource(string path)
        => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan);

    private static string DescribeOpenError(Exception ex)
    {
        if (ex is UnauthorizedAccessException) return "Zugriff verweigert";
        if (ex is FileNotFoundException or DirectoryNotFoundException) return "Während der Sicherung gelöscht oder verschoben";
        var code = ex.HResult & 0xFFFF;
        return code is 32 or 33
            ? "Gesperrt – von einem anderen Programm geöffnet (z. B. Outlook, Browser). Programm schließen und erneut sichern."
            : ex.Message;
    }

    private static string? TryHashSource(string path)
    {
        try
        {
            using var s = OpenSource(path);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // → wird kopiert (bzw. beim Kopieren als gesperrt gemeldet)
        }
    }

    /// <summary>Kopiert in eine Teildatei (*.uxpart) und benennt erst danach um – unvollständige Dateien sehen nie fertig aus.</summary>
    private static (long Bytes, string? Hash) CopyStream(Stream src, string dest, DateTime lastWriteUtc, bool hash, CancellationToken ct)
    {
        var tmp = dest + BackupPaths.PartialSuffix;
        long total = 0;
        using var ih = hash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        try
        {
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = ReadSource(src, buffer)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(buffer, 0, read);
                    ih?.AppendData(buffer, 0, read);
                    total += read;
                }
            }
            File.SetLastWriteTimeUtc(tmp, lastWriteUtc);
            File.Move(tmp, dest, overwrite: false);
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
        return (total, ih is null ? null : Convert.ToHexString(ih.GetHashAndReset()));
    }

    private static int ReadSource(Stream src, byte[] buffer)
    {
        try { return src.Read(buffer, 0, buffer.Length); }
        catch (IOException ex) { throw new SourceReadException(ex); }
    }

    /// <summary>Lesefehler der Quelle – im Gegensatz zu Schreibfehlern am Ziel kein Grund, das ganze Backup abzubrechen.</summary>
    private sealed class SourceReadException(IOException inner) : IOException(inner.Message, inner);

    /// <summary>Prüfung nach dem Backup: Anzahl/Größen aller Dateien + SHA-256-Stichprobe.</summary>
    public static (bool Ok, string Message) Verify(string versionDir, BackupManifest manifest, int sampleSize)
    {
        int missing = 0, sizeMismatch = 0;
        foreach (var e in manifest.Files)
        {
            var fi = new FileInfo(Path.Combine(versionDir, e.Path));
            if (!fi.Exists) missing++;
            else if (fi.Length != e.Size) sizeMismatch++;
        }
        var onDisk = 0;
        var manifestPath = Path.Combine(versionDir, BackupPaths.ManifestFileName);
        try
        {
            onDisk = Directory.EnumerateFiles(versionDir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 })
                .Count(f => !f.Equals(manifestPath, StringComparison.OrdinalIgnoreCase) && !f.Equals(manifestPath + ".tmp", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { onDisk = -1; }

        int sampled = 0, sampleOk = 0, sampleBad = 0;
        foreach (var e in BackupPlanner.Sample(manifest.Files, sampleSize))
        {
            var backupFile = Path.Combine(versionDir, e.Path);
            string? expected = e.Sha256;
            if (expected is null)
            {
                // Ohne gespeicherten Hash: mit der Quelle vergleichen, sofern sie seitdem unverändert ist.
                try
                {
                    var src = new FileInfo(BackupPaths.ToOriginalPath(e.Path));
                    if (src.Exists && src.Length == e.Size && (src.LastWriteTimeUtc - e.LastWriteUtc).Duration() <= BackupPlanner.TimeTolerance)
                        expected = TryHashSource(src.FullName);
                }
                catch (ArgumentException) { expected = null; }
            }
            if (expected is null) continue;
            var actual = TryHashSource(backupFile);
            if (actual is null) continue;
            sampled++;
            if (actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) sampleOk++; else sampleBad++;
        }

        var ok = missing == 0 && sizeMismatch == 0 && sampleBad == 0 && (onDisk < 0 || onDisk == manifest.Files.Count);
        var countText = onDisk < 0 ? "Anzahl am Ziel: N/A" : $"{onDisk.ToString(CultureInfo.InvariantCulture)} von {manifest.Files.Count.ToString(CultureInfo.InvariantCulture)} Dateien am Ziel";
        var msg = $"Prüfung: {countText}, {missing} fehlen, {sizeMismatch} mit falscher Größe; Stichprobe {sampleOk}/{sampled} Hashes identisch.";
        return (ok, ok ? msg : "Prüfung fehlgeschlagen – " + msg);
    }

    // ======================================================================================
    // Versionen, Manifest, Aufbewahrung
    // ======================================================================================

    public static IReadOnlyList<BackupVersionInfo> ListVersions(string profileRoot)
    {
        var list = new List<BackupVersionInfo>();
        if (!Directory.Exists(profileRoot)) return list;
        IEnumerable<string> dirs;
        try { dirs = Directory.GetDirectories(profileRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return list; }
        foreach (var d in dirs)
        {
            var name = Path.GetFileName(d);
            if (!BackupPaths.IsVersionName(name)) continue;
            if (new DirectoryInfo(d).LinkTarget is not null) continue;
            var m = LoadManifest(d);
            list.Add(new BackupVersionInfo(name, d, BackupPaths.ParseVersionName(name) ?? DateTime.MinValue, m?.Status, m?.Files.Count ?? 0, m?.TotalBytes ?? 0, m?.CopiedBytes ?? 0));
        }
        return list.OrderByDescending(v => v.Name, StringComparer.Ordinal).ToList();
    }

    public static BackupManifest? LoadManifest(string versionDir)
    {
        var path = Path.Combine(versionDir, BackupPaths.ManifestFileName);
        try
        {
            if (!File.Exists(path)) return null;
            using var s = File.OpenRead(path);
            var m = JsonSerializer.Deserialize<BackupManifest>(s, Json);
            if (m is null) return null;
            // Manifeste sind nicht vertrauenswürdig (Platte kann verändert worden sein): ungültige Pfade verwerfen.
            m.Files = m.Files.Where(f => BackupPaths.IsValidStoreRelative(f.Path)).ToList();
            return m;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppLog.Warn("Backup", "Manifest nicht lesbar", $"{path}: {ex.Message}");
            return null;
        }
    }

    private static void WriteManifest(string versionDir, BackupManifest manifest)
    {
        var path = Path.Combine(versionDir, BackupPaths.ManifestFileName);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, Json));
        File.Move(tmp, path, overwrite: true);
    }

    private static void TryWriteManifest(string versionDir, BackupManifest manifest)
    {
        try { WriteManifest(versionDir, manifest); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Backup", "Manifest konnte nicht geschrieben werden", ex.Message);
        }
    }

    /// <summary>Löscht Versionen über der Aufbewahrungsgrenze (siehe <see cref="BackupPlanner.SelectVersionsToDelete"/>).</summary>
    public static IReadOnlyList<string> ApplyRetention(string profileRoot, int keep)
    {
        var versions = ListVersions(profileRoot);
        var toDelete = BackupPlanner.SelectVersionsToDelete(versions.Select(v => (v.Name, v.IsComplete)), keep);
        var deleted = new List<string>();
        foreach (var name in toDelete)
        {
            var r = DeleteVersion(profileRoot, name);
            if (r.Success) deleted.Add(name);
            else AppLog.Warn("Backup", "Alte Version nicht gelöscht", $"{name}: {r.Message}");
        }
        return deleted;
    }

    /// <summary>
    /// Löscht genau einen Versionsordner. Sicherheitsprüfungen: gültiger Versionsname, liegt direkt unter dem Profilordner
    /// (unterhalb von UltimateX-Backup), kein geschützter Systempfad, keine Junction/Symlink-Kette, enthält ein ULTIMATE-X-Manifest.
    /// Reparse-Points INNERHALB werden nur als Verknüpfung entfernt, nie verfolgt. Hardlinks: nur dieser Verweis verschwindet,
    /// andere Versionen behalten ihre Daten.
    /// </summary>
    public static Core.Common.OperationResult DeleteVersion(string profileRoot, string versionName)
    {
        if (!BackupPaths.IsVersionName(versionName)) return Core.Common.OperationResult.Fail("Kein gültiger Versionsname.");
        string root, path;
        try
        {
            root = PathGuard.Normalize(profileRoot);
            path = PathGuard.Normalize(Path.Combine(root, versionName));
        }
        catch (ArgumentException ex) { return Core.Common.OperationResult.Fail(ex.Message); }

        var parent = Path.GetDirectoryName(root);
        if (parent is null || !Path.GetFileName(parent).Equals(BackupPaths.RootFolderName, StringComparison.OrdinalIgnoreCase))
            return Core.Common.OperationResult.Fail("Der Ordner liegt nicht unterhalb von UltimateX-Backup.");
        if (!string.Equals(Path.GetDirectoryName(path), root, StringComparison.OrdinalIgnoreCase))
            return Core.Common.OperationResult.Fail("Pfad liegt nicht direkt im Profilordner.");
        if (!PathGuard.IsSafeForModification(path, [root], out var reason)) return Core.Common.OperationResult.Fail(reason);
        if (!CommandCatalog.IsReparseFreeChain(Path.GetPathRoot(path)!, path)) return Core.Common.OperationResult.Fail("Verknüpfung im Pfad.");
        if (!File.Exists(Path.Combine(path, BackupPaths.ManifestFileName))) return Core.Common.OperationResult.Fail("Kein ULTIMATE-X-Manifest – Ordner wird nicht gelöscht.");
        try
        {
            DeleteTree(path);
            AppLog.Info("Backup", "Alte Backup-Version gelöscht (Aufbewahrung)", path);
            return Core.Common.OperationResult.Ok("Gelöscht.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Core.Common.OperationResult.Fail(ex.Message);
        }
    }

    private static void DeleteTree(string dir)
    {
        foreach (var e in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 }))
        {
            if (e.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Nur die Verknüpfung selbst entfernen, nie ihr Ziel.
                if (e is DirectoryInfo d) d.Delete(false); else e.Delete();
            }
            else if (e is DirectoryInfo d)
            {
                DeleteTree(d.FullName);
            }
            else
            {
                if (e.Attributes.HasFlag(FileAttributes.ReadOnly)) e.Attributes &= ~FileAttributes.ReadOnly;
                e.Delete();
            }
        }
        Directory.Delete(dir, false);
    }

    // ======================================================================================
    // Wiederherstellen
    // ======================================================================================

    /// <summary>Alle Manifest-Einträge gleich oder unterhalb des gewählten Speicherpfads.</summary>
    public static IReadOnlyList<BackupManifestEntry> SelectEntries(BackupManifest manifest, string selectedPrefix)
        => manifest.Files.Where(f => BackupPaths.IsUnderStorePrefix(f.Path, selectedPrefix)).ToList();

    private static string? MapDestination(RestoreRequest req, BackupManifestEntry e, out string? blocked)
    {
        blocked = null;
        string dest;
        try
        {
            dest = req.DestinationFolder is null
                ? BackupPaths.ToOriginalPath(e.Path)
                : BackupPaths.MapToFolder(e.Path, req.SelectedPrefix, req.DestinationFolder);
        }
        catch (ArgumentException ex) { blocked = $"{e.Path}: {ex.Message}"; return null; }
        if (PathGuard.IsProtected(dest)) { blocked = $"{dest}: geschützter Systemordner – wird nicht wiederhergestellt"; return null; }
        var root = Path.GetPathRoot(dest);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) { blocked = $"{dest}: Laufwerk nicht vorhanden"; return null; }
        if (Directory.Exists(dest)) { blocked = $"{dest}: dort existiert ein Ordner gleichen Namens"; return null; }
        return dest;
    }

    public static RestorePreview Preview(RestoreRequest req)
    {
        int files = 0, conflicts = 0, blocked = 0;
        long bytes = 0;
        var reasons = new List<string>();
        foreach (var e in req.Entries)
        {
            var dest = MapDestination(req, e, out var why);
            if (dest is null) { blocked++; if (why is not null && reasons.Count < 50) reasons.Add(why); continue; }
            files++;
            bytes += e.Size;
            if (File.Exists(dest)) conflicts++;
        }
        var destText = req.DestinationFolder ?? "Originalort";
        return new RestorePreview(files, bytes, conflicts, blocked, reasons, destText);
    }

    public Task<RestoreResult> RestoreAsync(RestoreRequest req, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
        => Task.Run(() => Restore(req, progress, ct), CancellationToken.None);

    public RestoreResult Restore(RestoreRequest req, IProgress<BackupProgress>? progress = null, CancellationToken ct = default)
    {
        int restored = 0, keptExisting = 0, replaced = 0, asCopy = 0, failed = 0, done = 0;
        long bytesDone = 0, total = req.Entries.Sum(e => e.Size);
        var problems = new List<string>();
        var stamp = Clock();
        var sw = Stopwatch.StartNew();
        foreach (var e in req.Entries)
        {
            if (ct.IsCancellationRequested)
                return new RestoreResult(restored, keptExisting, replaced, asCopy, failed, problems, true);
            done++;
            bytesDone += e.Size;
            var dest = MapDestination(req, e, out var why);
            if (dest is null) { failed++; if (why is not null && problems.Count < 200) problems.Add(why); continue; }
            var source = Path.Combine(req.VersionPath, e.Path);
            if (!File.Exists(source)) { failed++; problems.Add($"{e.Path}: in der Sicherung nicht gefunden"); continue; }

            var decision = RestoreConflicts.Decide(dest, req.Mode, File.Exists, stamp);
            try
            {
                switch (decision.Action)
                {
                    case RestoreAction.Skip:
                        keptExisting++;
                        continue;
                    case RestoreAction.ReplaceExisting:
                        _recycle(dest);
                        replaced++;
                        break;
                    case RestoreAction.WriteAsCopy:
                        asCopy++;
                        break;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(decision.DestinationPath)!);
                using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                    CopyStream(src, decision.DestinationPath, e.LastWriteUtc, false, ct);
                restored++;
            }
            catch (OperationCanceledException)
            {
                return new RestoreResult(restored, keptExisting, replaced, asCopy, failed, problems, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed++;
                if (problems.Count < 200) problems.Add($"{dest}: {ex.Message}");
            }
            if (sw.ElapsedMilliseconds >= 200)
            {
                sw.Restart();
                progress?.Report(new BackupProgress("Wiederherstellung läuft", done, req.Entries.Count, bytesDone, total, dest));
            }
        }
        AppLog.Info("Backup", "Wiederherstellung abgeschlossen", $"{restored} wiederhergestellt · {replaced} ersetzt · {asCopy} als Kopie · {keptExisting} behalten · {failed} Fehler");
        return new RestoreResult(restored, keptExisting, replaced, asCopy, failed, problems, false);
    }

    // ======================================================================================
    // Hilfen
    // ======================================================================================

    private static bool IsNtfs(string driveRoot)
    {
        try { return string.Equals(new DriveInfo(driveRoot).DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }

    private static long? FreeBytes(string driveRoot)
    {
        try { return new DriveInfo(driveRoot).AvailableFreeSpace; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    internal static string FormatBytes(long b)
        => b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB"
            : b >= 1L << 20 ? (b / (double)(1L << 20)).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " MB"
            : (b / 1024.0).ToString("0", CultureInfo.GetCultureInfo("de-DE")) + " KB";
}
