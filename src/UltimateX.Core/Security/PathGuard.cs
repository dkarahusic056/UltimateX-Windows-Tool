namespace UltimateX.Core.Security;

/// <summary>
/// Pfadschutz (§68, §69). Jede Dateioperation, die etwas verändert (verschieben, löschen, schreiben),
/// muss ihren Zielpfad hier prüfen lassen.
///  - Pfade werden kanonisiert (".." aufgelöst), danach wird geprüft, ob sie innerhalb einer erlaubten Wurzel liegen.
///  - Geschützte Systemordner sind niemals Ziel automatisierter Änderungen.
///  - Reparse-Points (Symlinks/Junctions) werden nicht verfolgt.
/// </summary>
public static class PathGuard
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    /// <summary>Ordner, die niemals automatisiert verändert werden.</summary>
    public static IReadOnlyList<string> ProtectedRoots { get; } = BuildProtectedRoots();

    private static List<string> BuildProtectedRoots()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (!string.IsNullOrWhiteSpace(p)) list.Add(Normalize(p));
        }

        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Add(windows);
        Add(Environment.GetFolderPath(Environment.SpecialFolder.System));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        var systemDrive = Path.GetPathRoot(windows);
        if (!string.IsNullOrEmpty(systemDrive))
        {
            Add(Path.Combine(systemDrive, "Boot"));
            Add(Path.Combine(systemDrive, "EFI"));
            Add(Path.Combine(systemDrive, "Recovery"));
            Add(Path.Combine(systemDrive, "System Volume Information"));
        }
        return list;
    }

    /// <summary>Kanonisiert einen Pfad ohne Dateisystemzugriff; entfernt abschließende Trennzeichen.</summary>
    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Leerer Pfad.", nameof(path));
        if (path.IndexOf('\0', StringComparison.Ordinal) >= 0) throw new ArgumentException("Ungültiges Zeichen im Pfad.", nameof(path));
        // Geräte- und UNC-Pfade werden für Änderungen nicht unterstützt.
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("UNC- und Gerätepfade sind nicht erlaubt.", nameof(path));
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Nur absolute Pfade sind erlaubt.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
    }

    /// <summary>True, wenn <paramref name="candidate"/> gleich <paramref name="root"/> ist oder darin liegt.</summary>
    public static bool IsWithin(string candidate, string root)
    {
        var c = Normalize(candidate);
        var r = Normalize(root);
        if (c.Equals(r, Cmp)) return true;
        var rWithSep = r.EndsWith(Path.DirectorySeparatorChar) ? r : r + Path.DirectorySeparatorChar;
        return c.StartsWith(rWithSep, Cmp);
    }

    /// <summary>
    /// Einzige Ausnahmen innerhalb geschützter Wurzeln: Ordner, deren Inhalt Windows selbst als
    /// temporär/verwerfbar definiert (Speicherbereinigung, §52). Die Ordner selbst werden nie gelöscht.
    /// </summary>
    public static IReadOnlyList<string> CleanableSystemRoots { get; } =
    [
        Normalize(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp")),
    ];

    public static bool IsProtected(string path)
    {
        string n;
        try { n = Normalize(path); }
        catch (ArgumentException) { return true; } // Unklare Pfade gelten als geschützt (Fail Securely).
        if (CleanableSystemRoots.Any(r => !n.Equals(r, Cmp) && IsWithin(n, r))) return false;
        return ProtectedRoots.Any(p => IsWithin(n, p));
    }

    /// <summary>
    /// Prüft, ob ein Pfad für eine verändernde Operation zulässig ist:
    /// innerhalb einer der erlaubten Wurzeln, nicht geschützt, kein Reparse-Point.
    /// </summary>
    public static bool IsSafeForModification(string path, IEnumerable<string> allowedRoots, out string reason)
    {
        reason = string.Empty;
        string n;
        try { n = Normalize(path); }
        catch (ArgumentException ex) { reason = ex.Message; return false; }

        if (IsProtected(n)) { reason = "Geschützter Systempfad."; return false; }
        if (!allowedRoots.Any(r => IsWithin(n, r))) { reason = "Pfad liegt außerhalb des erlaubten Bereichs."; return false; }

        try
        {
            if (File.Exists(n) || Directory.Exists(n))
            {
                var attr = File.GetAttributes(n);
                if (attr.HasFlag(FileAttributes.ReparsePoint)) { reason = "Symbolische Links/Junctions werden nicht verändert."; return false; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = "Pfad nicht zugreifbar.";
            return false;
        }
        return true;
    }
}
