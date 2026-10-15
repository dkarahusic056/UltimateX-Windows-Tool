namespace UltimateX.Core.Analysis;

/// <summary>
/// SYSTEM SNAPSHOT (v1.2 §38): ein Systemstand als Abschnitte (Hardware, Treiber, Programme …) mit Schlüssel/Wert-Paaren.
/// Bewusst schlicht gehalten, damit Snapshots als JSON gespeichert und unabhängig von der App-Version verglichen werden können.
/// </summary>
public sealed record SystemSnapshotData(
    DateTime Created,
    string Label,
    string AppVersion,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Sections);

public enum ChangeKind { Added, Removed, Changed }

public sealed record SnapshotChange(string Section, ChangeKind Kind, string Key, string? Before, string? After);

public sealed record SnapshotComparison(IReadOnlyList<SnapshotChange> Changes)
{
    public int Added => Changes.Count(c => c.Kind == ChangeKind.Added);
    public int Removed => Changes.Count(c => c.Kind == ChangeKind.Removed);
    public int Modified => Changes.Count(c => c.Kind == ChangeKind.Changed);

    public IReadOnlyDictionary<string, int> PerSection
        => Changes.GroupBy(c => c.Section).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
}

public static class SnapshotComparer
{
    /// <summary>Vergleicht BEFORE mit AFTER. Abschnitte, die nur in einem Snapshot existieren, gelten als komplett hinzugefügt/entfernt.</summary>
    public static SnapshotComparison Compare(SystemSnapshotData before, SystemSnapshotData after)
    {
        var changes = new List<SnapshotChange>();
        var sections = before.Sections.Keys.Union(after.Sections.Keys, StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal);
        foreach (var section in sections)
        {
            before.Sections.TryGetValue(section, out var b);
            after.Sections.TryGetValue(section, out var a);
            b ??= new Dictionary<string, string>();
            a ??= new Dictionary<string, string>();
            foreach (var key in b.Keys.Union(a.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
            {
                var hasB = b.TryGetValue(key, out var vb);
                var hasA = a.TryGetValue(key, out var va);
                if (hasB && !hasA) changes.Add(new SnapshotChange(section, ChangeKind.Removed, key, vb, null));
                else if (!hasB && hasA) changes.Add(new SnapshotChange(section, ChangeKind.Added, key, null, va));
                else if (!string.Equals(vb, va, StringComparison.Ordinal)) changes.Add(new SnapshotChange(section, ChangeKind.Changed, key, vb, va));
            }
        }
        return new SnapshotComparison(changes);
    }
}
