using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.SystemTools;

namespace UltimateX.Services.Cleanup;

public enum CleanupCategory
{
    RecycleBin,
    UserTemp,
    WindowsTemp,
    ThumbnailCache,
    ErrorReports,
    ShaderCache,
}

public sealed record CleanupCandidate(
    CleanupCategory Category,
    string Title,
    string Description,
    IReadOnlyList<string> Roots,
    long FileCount,
    long SizeBytes,
    bool RecommendedByDefault,
    string? Note);

public sealed record CleanupResult(long DeletedFiles, long FreedBytes, long SkippedFiles);

/// <summary>
/// Storage Cleaner (§52). Bereinigt ausschließlich fest definierte, von Windows als temporär
/// geltende Orte. Persönliche Dateien (Dokumente, Desktop, Downloads, Bilder …) sind nie Teil einer Kategorie.
/// Sicherheitsregeln:
///  - Jede Datei wird vor dem Löschen über <see cref="PathGuard"/> gegen die Kategorie-Wurzel geprüft.
///  - Reparse-Points (Symlinks/Junctions) werden weder verfolgt noch gelöscht.
///  - Temp-Dateien werden nur gelöscht, wenn sie älter als 24 Stunden sind (keine laufenden Installationen stören).
///  - Gesperrte Dateien werden übersprungen, nie erzwungen.
///  - Die Wurzelordner selbst bleiben immer erhalten.
/// </summary>
public sealed class StorageCleanerService
{
    private static readonly TimeSpan MinAge = TimeSpan.FromHours(24);
    private readonly RecycleBinService _recycleBin = new();

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static (string Title, string Description, string[] Roots, string Pattern, bool Recommended, bool OnlyOld) Definition(CleanupCategory c) => c switch
    {
        CleanupCategory.UserTemp => ("Temporäre Dateien (Benutzer)", "Von Programmen abgelegte Zwischendateien.",
            [Path.GetTempPath()], "*", true, true),
        CleanupCategory.WindowsTemp => ("Temporäre Dateien (Windows)", "Zwischendateien von Windows. Ohne Adminrechte werden nur eigene Dateien entfernt.",
            [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")], "*", true, true),
        CleanupCategory.ThumbnailCache => ("Miniaturansichten-Cache", "Wird von Windows bei Bedarf neu erstellt. Gerade verwendete Dateien werden übersprungen.",
            [Path.Combine(LocalAppData, "Microsoft", "Windows", "Explorer")], "thumbcache_*.db", false, false),
        CleanupCategory.ErrorReports => ("Windows-Fehlerberichte", "Archivierte und wartende Fehlerberichte des Benutzers.",
            [Path.Combine(LocalAppData, "Microsoft", "Windows", "WER", "ReportArchive"), Path.Combine(LocalAppData, "Microsoft", "Windows", "WER", "ReportQueue")], "*", true, false),
        CleanupCategory.ShaderCache => ("DirectX-Shader-Cache", "Wird von Spielen/Treibern neu aufgebaut (erster Start kann kurz länger dauern).",
            [Path.Combine(LocalAppData, "D3DSCache")], "*", false, false),
        _ => ("Papierkorb", "Gelöschte Dateien aller Laufwerke.", [], "*", false, false),
    };

    /// <summary>Analyse ohne Änderung: zeigt Dateianzahl, Größe und Pfade je Kategorie (§52 "Vorher anzeigen").</summary>
    public IReadOnlyList<CleanupCandidate> Analyze(CancellationToken ct = default)
    {
        var list = new List<CleanupCandidate>();
        foreach (var cat in Enum.GetValues<CleanupCategory>())
        {
            ct.ThrowIfCancellationRequested();
            var def = Definition(cat);
            if (cat == CleanupCategory.RecycleBin)
            {
                var rb = _recycleBin.Query();
                list.Add(new CleanupCandidate(cat, def.Title, def.Description, ["Papierkorb (alle Laufwerke)"], rb?.ItemCount ?? 0, rb?.SizeBytes ?? 0, false,
                    "Endgültiges Löschen – nicht rückgängig zu machen."));
                continue;
            }

            long count = 0, size = 0;
            foreach (var file in EnumerateCandidates(def.Roots, def.Pattern, def.OnlyOld))
            {
                count++;
                size += file.Length;
            }
            list.Add(new CleanupCandidate(cat, def.Title, def.Description, def.Roots, count, size, def.Recommended,
                def.OnlyOld ? "Nur Dateien älter als 24 Stunden." : null));
        }
        return list;
    }

    public CleanupResult Clean(IEnumerable<CleanupCategory> categories, IntPtr ownerWindow, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        long deleted = 0, freed = 0, skipped = 0;
        foreach (var cat in categories.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var def = Definition(cat);
            progress?.Report(def.Title);

            if (cat == CleanupCategory.RecycleBin)
            {
                var before = _recycleBin.Query();
                if (before is { ItemCount: > 0 } && _recycleBin.Empty(ownerWindow).Success)
                {
                    deleted += before.ItemCount;
                    freed += before.SizeBytes;
                }
                continue;
            }

            foreach (var file in EnumerateCandidates(def.Roots, def.Pattern, def.OnlyOld).ToList())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!PathGuard.IsSafeForModification(file.FullName, def.Roots, out _)) { skipped++; continue; }
                    var len = file.Length;
                    if (file.IsReadOnly) { skipped++; continue; } // schreibgeschützte Dateien bewusst nicht anfassen
                    file.Delete();
                    deleted++;
                    freed += len;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++; // gesperrt oder keine Rechte → überspringen
                }
            }
            foreach (var root in def.Roots) RemoveEmptySubdirectories(root);
        }

        AppLog.Info("Cleanup", "Speicherbereinigung durchgeführt",
            $"Kategorien={string.Join(",", categories)}, Dateien={deleted}, Freigegeben={freed}, Übersprungen={skipped}");
        return new CleanupResult(deleted, freed, skipped);
    }

    private static IEnumerable<FileInfo> EnumerateCandidates(IEnumerable<string> roots, string pattern, bool onlyOld)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = pattern == "*",
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            ReturnSpecialDirectories = false,
        };
        var cutoff = DateTime.UtcNow - MinAge;
        foreach (var root in roots)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) continue;
            var rootInfo = new DirectoryInfo(root);
            if (rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

            IEnumerable<FileInfo> files;
            try { files = rootInfo.EnumerateFiles(pattern, options); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

            foreach (var f in files)
            {
                if (onlyOld && f.LastWriteTimeUtc > cutoff) continue;
                yield return f;
            }
        }
    }

    private static void RemoveEmptySubdirectories(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            // Tiefste Ordner zuerst; die Wurzel selbst bleibt immer bestehen.
            var dirs = new DirectoryInfo(root).EnumerateDirectories("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            }).OrderByDescending(d => d.FullName.Length).ToList();
            foreach (var d in dirs)
            {
                try
                {
                    if (!PathGuard.IsSafeForModification(d.FullName, [root], out _)) continue;
                    if (!d.EnumerateFileSystemInfos().Any()) d.Delete(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
