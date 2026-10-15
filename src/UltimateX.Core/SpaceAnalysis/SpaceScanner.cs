using UltimateX.Core.Security;

namespace UltimateX.Core.SpaceAnalysis;

/// <summary>Ordner im Größenbaum. Größe = Summe aller Dateien darunter (logische Dateigröße).</summary>
public sealed class FolderNode
{
    public FolderNode(string name, string fullPath, FolderNode? parent)
    {
        Name = name;
        FullPath = fullPath;
        Parent = parent;
    }

    public string Name { get; }
    public string FullPath { get; }
    public FolderNode? Parent { get; }
    public List<FolderNode> Children { get; } = [];

    /// <summary>Größe aller Dateien direkt in diesem Ordner.</summary>
    public long OwnBytes { get; internal set; }
    public int OwnFiles { get; internal set; }

    /// <summary>Größe inkl. Unterordner (nach dem Scan berechnet).</summary>
    public long TotalBytes { get; internal set; }
    public long TotalFiles { get; internal set; }

    /// <summary>Ordner war nicht lesbar (Inhalt fehlt in der Summe).</summary>
    public bool AccessDenied { get; internal set; }
}

public sealed record ScannedFile(string FullPath, long Size, DateTime LastWriteUtc);

public sealed record FileTypeStat(string Extension, long Files, long Bytes);

public sealed record SpaceScanProgress(long Files, long Folders, long Bytes, string CurrentFolder);

public sealed record SpaceScanResult(
    FolderNode Root,
    IReadOnlyList<ScannedFile> LargestFiles,
    IReadOnlyList<FileTypeStat> FileTypes,
    IReadOnlyDictionary<long, List<string>> SizeGroups,
    long TotalFiles,
    long TotalFolders,
    long TotalBytes,
    int AccessErrors,
    int SkippedReparsePoints,
    long CloudOnlyFiles,
    bool Cancelled);

/// <summary>Einstellungen für den Speicherplatz-Scan.</summary>
public sealed record SpaceScanOptions
{
    /// <summary>Anzahl der größten Dateien (Top-Liste).</summary>
    public int TopFiles { get; init; } = 100;

    /// <summary>Kleinere Dateien werden nicht für die Duplikatsuche vorgemerkt (spart Zeit und Speicher).</summary>
    public long DuplicateMinBytes { get; init; } = 1024 * 1024;
}

/// <summary>
/// Liest einen Ordner rekursiv und ausschließlich lesend:
///  - Reparse-Points (Junctions, Symlinks, Mount-Points) werden NICHT verfolgt, sondern gezählt.
///  - Nur online verfügbare Cloud-Dateien (OneDrive „Dateien bei Bedarf“) werden nicht geöffnet und nicht mitgezählt.
///  - Zugriffsfehler werden gezählt, der Scan läuft weiter.
///  - Abbrechen über das CancellationToken liefert das bisherige (unvollständige) Ergebnis mit <c>Cancelled = true</c>.
/// Hinweis: Hardlinks (z. B. in C:\Windows\WinSxS) werden je Pfad gezählt – Summen können daher über der belegten Größe liegen.
/// </summary>
public static class SpaceScanner
{
    // FILE_ATTRIBUTE_RECALL_ON_OPEN (0x40000) / FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS (0x400000): Inhalt liegt nur in der Cloud.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    public static SpaceScanResult Scan(string rootPath, SpaceScanOptions? options = null, IProgress<SpaceScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new SpaceScanOptions();
        var rootFull = PathGuard.Normalize(rootPath);
        if (!Directory.Exists(rootFull)) throw new DirectoryNotFoundException(rootFull);
        if (!string.Equals(Path.GetPathRoot(rootFull), rootFull, StringComparison.OrdinalIgnoreCase)
            && new DirectoryInfo(rootFull).Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new ArgumentException("Der gewählte Ordner ist eine Verknüpfung (Junction/Symlink) und wird nicht verfolgt.", nameof(rootPath));

        var root = new FolderNode(rootFull, rootFull, null);
        var order = new List<FolderNode>();
        var stack = new Stack<FolderNode>();
        stack.Push(root);

        var top = new PriorityQueue<ScannedFile, long>();
        var types = new Dictionary<string, (long Files, long Bytes)>(StringComparer.OrdinalIgnoreCase);
        var sizeGroups = new Dictionary<long, List<string>>();
        long files = 0, folders = 0, bytes = 0, cloudOnly = 0;
        int accessErrors = 0, reparse = 0;
        var cancelled = false;
        var lastReport = Environment.TickCount64;

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        while (stack.Count > 0)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            var node = stack.Pop();
            order.Add(node);
            folders++;

            try
            {
                var dir = new DirectoryInfo(node.FullPath);
                using var e = dir.EnumerateFileSystemInfos("*", enumOptions).GetEnumerator();
                while (true)
                {
                    FileSystemInfo info;
                    try
                    {
                        if (!e.MoveNext()) break;
                        info = e.Current;
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
                    {
                        accessErrors++;
                        node.AccessDenied = true;
                        break;
                    }

                    FileAttributes attr;
                    try { attr = info.Attributes; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { accessErrors++; continue; }

                    if (info is DirectoryInfo sub)
                    {
                        if (attr.HasFlag(FileAttributes.ReparsePoint)) { reparse++; continue; }
                        var child = new FolderNode(sub.Name, sub.FullName, node);
                        node.Children.Add(child);
                        stack.Push(child);
                        continue;
                    }

                    if (info is not FileInfo fi) continue;
                    if ((attr & (RecallOnOpen | RecallOnDataAccess | FileAttributes.Offline)) != 0) { cloudOnly++; continue; }

                    long len;
                    DateTime lastWrite;
                    try { len = fi.Length; lastWrite = fi.LastWriteTimeUtc; }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { accessErrors++; continue; }

                    files++;
                    bytes += len;
                    node.OwnBytes += len;
                    node.OwnFiles++;

                    var ext = ExtensionKey(fi.Name);
                    types[ext] = types.TryGetValue(ext, out var t) ? (t.Files + 1, t.Bytes + len) : (1, len);

                    if (options.TopFiles > 0)
                    {
                        if (top.Count < options.TopFiles) top.Enqueue(new ScannedFile(fi.FullName, len, lastWrite), len);
                        else if (top.TryPeek(out _, out var smallest) && len > smallest) top.EnqueueDequeue(new ScannedFile(fi.FullName, len, lastWrite), len);
                    }

                    // Reparse-Point-Dateien (Dedup, Symlinks) nicht für die Duplikatsuche vormerken – Inhalt liegt evtl. woanders.
                    if (len >= options.DuplicateMinBytes && len > 0 && !attr.HasFlag(FileAttributes.ReparsePoint))
                    {
                        if (!sizeGroups.TryGetValue(len, out var list)) sizeGroups[len] = list = [];
                        list.Add(fi.FullName);
                    }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                accessErrors++;
                node.AccessDenied = true;
            }

            if (progress is not null && Environment.TickCount64 - lastReport > 250)
            {
                lastReport = Environment.TickCount64;
                progress.Report(new SpaceScanProgress(files, folders, bytes, node.FullPath));
            }
        }

        // Summen von unten nach oben (umgekehrte Besuchsreihenfolge: Kinder vor Eltern).
        for (var i = order.Count - 1; i >= 0; i--)
        {
            var n = order[i];
            n.TotalBytes += n.OwnBytes;
            n.TotalFiles += n.OwnFiles;
            if (n.Parent is { } p)
            {
                p.TotalBytes += n.TotalBytes;
                p.TotalFiles += n.TotalFiles;
            }
        }
        SortBySize(order);
        progress?.Report(new SpaceScanProgress(files, folders, bytes, rootFull));

        var largest = new List<ScannedFile>(top.Count);
        while (top.TryDequeue(out var f, out _)) largest.Add(f);
        largest.Reverse();

        var typeList = types.Select(kv => new FileTypeStat(kv.Key, kv.Value.Files, kv.Value.Bytes))
            .OrderByDescending(t => t.Bytes).ToList();

        // Nur Größen mit mindestens zwei Dateien sind Duplikat-Kandidaten.
        var candidates = sizeGroups.Where(kv => kv.Value.Count > 1).ToDictionary(kv => kv.Key, kv => kv.Value);

        return new SpaceScanResult(root, largest, typeList, candidates, files, folders, bytes, accessErrors, reparse, cloudOnly, cancelled);
    }

    private static void SortBySize(List<FolderNode> nodes)
    {
        foreach (var n in nodes)
            n.Children.Sort((a, b) => b.TotalBytes.CompareTo(a.TotalBytes));
    }

    /// <summary>Dateityp-Schlüssel: Endung in Kleinbuchstaben inkl. Punkt, „(ohne)“ ohne Endung.</summary>
    public static string ExtensionKey(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(ext) || ext.Length > 16 ? "(ohne)" : ext.ToLowerInvariant();
    }

    /// <summary>Anteil (0–100) für Balken; 0 bei leerem Elternordner.</summary>
    public static double Percent(long part, long whole) => whole <= 0 ? 0 : Math.Clamp(part * 100.0 / whole, 0, 100);
}
