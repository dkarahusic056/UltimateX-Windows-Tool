using System.Security.Cryptography;

namespace UltimateX.Core.SpaceAnalysis;

/// <summary>Gruppe inhaltsgleicher Dateien (gleiche Größe, gleicher SHA-256).</summary>
public sealed record DuplicateGroup(long Size, string Sha256, IReadOnlyList<string> Paths)
{
    /// <summary>Platz, der frei würde, wenn nur eine Kopie bliebe.</summary>
    public long WastedBytes => Size * (Paths.Count - 1);
}

public sealed record DuplicateProgress(int Done, int Total, string Phase);

public sealed record DuplicateResult(IReadOnlyList<DuplicateGroup> Groups, int UnreadableFiles, bool Cancelled)
{
    public long WastedBytes => Groups.Sum(g => g.WastedBytes);
}

/// <summary>Hash-Funktionen (austauschbar für Tests).</summary>
public interface IFileHasher
{
    /// <summary>Hash über die ersten <paramref name="bytes"/> Byte.</summary>
    string PartialHash(string path, int bytes, CancellationToken ct);

    /// <summary>SHA-256 über den gesamten Inhalt.</summary>
    string FullHash(string path, CancellationToken ct);
}

/// <summary>Liest Dateien nur lesend und teilt den Zugriff mit anderen Programmen (FileShare.ReadWrite | Delete).</summary>
public sealed class Sha256FileHasher : IFileHasher
{
    public static Sha256FileHasher Instance { get; } = new();

    public string PartialHash(string path, int bytes, CancellationToken ct)
    {
        using var fs = Open(path);
        var buffer = new byte[bytes];
        var read = 0;
        while (read < bytes)
        {
            ct.ThrowIfCancellationRequested();
            var n = fs.Read(buffer, read, bytes - read);
            if (n == 0) break;
            read += n;
        }
        return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
    }

    public string FullHash(string path, CancellationToken ct)
    {
        using var fs = Open(path);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            sha.AppendData(buffer, 0, n);
        }
        return Convert.ToHexString(sha.GetHashAndReset());
    }

    private static FileStream Open(string path)
        => new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = FileOptions.SequentialScan,
            BufferSize = 0,
        });
}

/// <summary>
/// Duplikatsuche in drei Stufen: gleiche Größe → gleicher Teil-Hash (erste 64 KB) → gleicher SHA-256 über die ganze Datei.
/// Nur echte Inhaltsgleichheit führt zu einer Gruppe; nicht lesbare Dateien werden gezählt und übersprungen.
/// </summary>
public static class DuplicateFinder
{
    public const int PartialBytes = 64 * 1024;

    public static DuplicateResult Find(IReadOnlyDictionary<long, List<string>> sizeGroups, IFileHasher? hasher = null,
        IProgress<DuplicateProgress>? progress = null, CancellationToken ct = default)
    {
        hasher ??= Sha256FileHasher.Instance;
        var groups = new List<DuplicateGroup>();
        var unreadable = 0;
        var candidates = sizeGroups.Where(kv => kv.Value.Count > 1 && kv.Key > 0).OrderByDescending(kv => kv.Key).ToList();
        var total = candidates.Sum(kv => kv.Value.Count);
        var done = 0;

        try
        {
            foreach (var (size, paths) in candidates)
            {
                // Stufe 2: Teil-Hash
                var byPartial = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var p in paths.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    done++;
                    if (done % 20 == 0) progress?.Report(new DuplicateProgress(done, total, "Teil-Hash"));
                    string h;
                    try { h = hasher.PartialHash(p, PartialBytes, ct); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; continue; }
                    if (!byPartial.TryGetValue(h, out var l)) byPartial[h] = l = [];
                    l.Add(p);
                }

                // Stufe 3: voller SHA-256 (bei Dateien bis 64 KB ist der Teil-Hash bereits der volle Hash)
                foreach (var (partialHash, partialGroup) in byPartial.Where(kv => kv.Value.Count > 1))
                {
                    if (size <= PartialBytes)
                    {
                        groups.Add(new DuplicateGroup(size, partialHash, partialGroup.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()));
                        continue;
                    }
                    var byFull = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    foreach (var p in partialGroup)
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report(new DuplicateProgress(done, total, "SHA-256"));
                        string h;
                        try { h = hasher.FullHash(p, ct); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unreadable++; continue; }
                        if (!byFull.TryGetValue(h, out var l)) byFull[h] = l = [];
                        l.Add(p);
                    }
                    foreach (var (hash, l) in byFull.Where(kv => kv.Value.Count > 1))
                        groups.Add(new DuplicateGroup(size, hash, l.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList()));
                }
            }
        }
        catch (OperationCanceledException)
        {
            return new DuplicateResult(Sort(groups), unreadable, true);
        }
        progress?.Report(new DuplicateProgress(total, total, "Fertig"));
        return new DuplicateResult(Sort(groups), unreadable, false);
    }

    private static List<DuplicateGroup> Sort(List<DuplicateGroup> groups)
        => groups.OrderByDescending(g => g.WastedBytes).ToList();
}
