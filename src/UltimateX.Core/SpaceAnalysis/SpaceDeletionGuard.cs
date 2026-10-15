using UltimateX.Core.Security;

namespace UltimateX.Core.SpaceAnalysis;

/// <summary>
/// Regeln für „In den Papierkorb verschieben“ auf der Seite Speicherplatz-Analyse.
/// Nur einzelne, vom Benutzer ausgewählte Dateien; nie in Windows, Program Files (x86), ProgramData oder anderen
/// geschützten Systemordnern (<see cref="PathGuard"/>), nie Reparse-Points, nie Laufwerkswurzeln oder Ordner.
/// </summary>
public static class SpaceDeletionGuard
{
    private static readonly StringComparison Cmp = StringComparison.OrdinalIgnoreCase;

    /// <summary>Zusätzlich zu <see cref="PathGuard.ProtectedRoots"/> gesperrte Wurzeln.</summary>
    public static IReadOnlyList<string> ExtraProtectedRoots { get; } = BuildExtra();

    private static List<string> BuildExtra()
    {
        var list = new List<string>();
        foreach (var p in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), // C:\ProgramData
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try { list.Add(PathGuard.Normalize(p)); }
            catch (ArgumentException) { }
        }
        return list;
    }

    /// <summary>Reine Pfadprüfung (ohne Dateisystem): darf dieser Pfad grundsätzlich in den Papierkorb?</summary>
    public static bool IsAllowedPath(string path, IEnumerable<string> extraProtectedRoots, out string reason)
    {
        reason = string.Empty;
        string n;
        try { n = PathGuard.Normalize(path); }
        catch (ArgumentException ex) { reason = ex.Message; return false; }

        var root = Path.GetPathRoot(n);
        if (string.IsNullOrEmpty(root) || n.Equals(root.TrimEnd('\\', '/'), Cmp) || n.Equals(root, Cmp))
        {
            reason = "Laufwerkswurzeln werden nicht gelöscht.";
            return false;
        }
        if (PathGuard.IsProtected(n) || extraProtectedRoots.Any(r => PathGuard.IsWithin(n, r)))
        {
            reason = "Geschützter Systemordner (Windows, Programme, ProgramData) – hier wird nichts gelöscht.";
            return false;
        }
        return true;
    }

    /// <summary>Vollständige Prüfung vor dem Löschen: Pfadregeln + existierende normale Datei, kein Reparse-Point.</summary>
    public static bool CanRecycle(string path, out string reason)
    {
        if (!IsAllowedPath(path, ExtraProtectedRoots, out reason)) return false;
        var n = PathGuard.Normalize(path);
        if (!PathGuard.IsSafeForModification(n, [Path.GetPathRoot(n)!], out reason)) return false;
        try
        {
            if (!File.Exists(n)) { reason = "Datei existiert nicht mehr."; return false; }
            var attr = File.GetAttributes(n);
            if (attr.HasFlag(FileAttributes.Directory)) { reason = "Nur Dateien, keine Ordner."; return false; }
            if (attr.HasFlag(FileAttributes.System)) { reason = "Systemdatei – wird nicht gelöscht."; return false; }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            reason = "Datei nicht zugreifbar.";
            return false;
        }
        return true;
    }
}
