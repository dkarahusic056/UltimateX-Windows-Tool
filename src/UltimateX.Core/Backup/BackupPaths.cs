using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Backup;

/// <summary>
/// Feste Ablagestruktur des Backup-Centers:
/// <c>&lt;Ziel&gt;\UltimateX-Backup\&lt;Profil&gt;\&lt;yyyy-MM-dd_HHmmss&gt;\&lt;Laufwerk&gt;\&lt;Pfad&gt;</c>.
/// Quellpfade werden als "Speicherpfad" abgelegt: "C:\Users\a\Dokumente\x.txt" → "C\Users\a\Dokumente\x.txt".
/// So ist jede Version mit dem Explorer durchsuchbar und der Originalort eindeutig rekonstruierbar.
/// </summary>
public static partial class BackupPaths
{
    public const string RootFolderName = "UltimateX-Backup";
    public const string MarkerFileName = "target.json";
    public const string ManifestFileName = "ultimatex-manifest.json";
    public const string DriverFolderName = "Treiber";
    public const string PartialSuffix = ".uxpart";
    public const string VersionFormat = "yyyy-MM-dd_HHmmss";

    /// <summary>Ordnernamen, die nicht als Profilordner verwendet werden dürfen.</summary>
    private static readonly string[] ReservedFolderNames = [DriverFolderName, "WindowsImageBackup", "target", "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}_\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionNameRegex();

    [GeneratedRegex(@"^[\p{L}\p{Nd} _-]{1,60}$", RegexOptions.CultureInvariant)]
    private static partial Regex FolderNameRegex();

    public static string BackupRoot(string targetPath) => Path.Combine(targetPath, RootFolderName);

    public static string MarkerPath(string targetPath) => Path.Combine(BackupRoot(targetPath), MarkerFileName);

    public static string ProfileRoot(string targetPath, string folderName)
    {
        if (!IsValidFolderName(folderName)) throw new ArgumentException("Ungültiger Profilordner.", nameof(folderName));
        return Path.Combine(BackupRoot(targetPath), folderName);
    }

    public static string VersionName(DateTime local) => local.ToString(VersionFormat, CultureInfo.InvariantCulture);

    public static bool IsVersionName(string? name)
        => name is not null && VersionNameRegex().IsMatch(name)
           && DateTime.TryParseExact(name, VersionFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static DateTime? ParseVersionName(string name)
        => IsVersionName(name) ? DateTime.ParseExact(name, VersionFormat, CultureInfo.InvariantCulture, DateTimeStyles.None) : null;

    public static bool IsValidFolderName(string? name)
        => name is not null && FolderNameRegex().IsMatch(name) && name.Trim() == name
           && !ReservedFolderNames.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Bildet aus einem frei gewählten Profilnamen einen sicheren Ordnernamen.</summary>
    public static string MakeFolderName(string profileName, IEnumerable<string>? existing = null)
    {
        var sb = new StringBuilder();
        foreach (var c in profileName.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') sb.Append(c);
            else if (c is '.' or ',' or '/' or '\\' or ':' or '+' or '&') sb.Append('_');
            if (sb.Length >= 50) break;
        }
        var name = sb.ToString().Trim().Trim('_').Trim();
        if (name.Length == 0) name = "Profil";
        if (ReservedFolderNames.Any(r => r.Equals(name, StringComparison.OrdinalIgnoreCase))) name += "-Profil";
        var taken = new HashSet<string>(existing ?? [], StringComparer.OrdinalIgnoreCase);
        var candidate = name;
        for (var i = 2; taken.Contains(candidate); i++) candidate = $"{name}-{i.ToString(CultureInfo.InvariantCulture)}";
        return candidate;
    }

    /// <summary>"C:\Users\a\x.txt" → "C\Users\a\x.txt". Nur vollständige lokale Pfade (keine UNC-/Gerätepfade).</summary>
    public static string ToStoreRelative(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || fullPath.Length < 3 || !char.IsAsciiLetter(fullPath[0]) || fullPath[1] != ':'
            || fullPath[2] is not ('\\' or '/'))
            throw new ArgumentException("Nur lokale Pfade mit Laufwerksbuchstaben werden gesichert.", nameof(fullPath));
        var rest = fullPath[3..].Replace('/', '\\').TrimEnd('\\');
        var letter = char.ToUpperInvariant(fullPath[0]).ToString();
        var rel = rest.Length == 0 ? letter : letter + "\\" + rest;
        if (!IsValidStoreRelative(rel)) throw new ArgumentException("Ungültiger Pfad.", nameof(fullPath));
        return rel;
    }

    /// <summary>"C\Users\a\x.txt" → "C:\Users\a\x.txt".</summary>
    public static string ToOriginalPath(string storeRelative)
    {
        if (!IsValidStoreRelative(storeRelative)) throw new ArgumentException("Ungültiger Speicherpfad.", nameof(storeRelative));
        return storeRelative.Length == 1 ? storeRelative + ":\\" : storeRelative[0] + ":\\" + storeRelative[2..];
    }

    /// <summary>
    /// Prüft einen Speicherpfad aus einem Manifest (nicht vertrauenswürdig – die Backup-Platte kann verändert worden sein):
    /// erster Teil genau ein Laufwerksbuchstabe, keine leeren, "."/".."-Segmente, keine Doppelpunkte/Wildcards/Steuerzeichen.
    /// </summary>
    public static bool IsValidStoreRelative(string? rel)
    {
        if (string.IsNullOrEmpty(rel) || rel.Length > 4000) return false;
        if (rel.Contains('/') || rel.Any(c => char.IsControl(c) || c is ':' or '*' or '?' or '"' or '<' or '>' or '|')) return false;
        var parts = rel.Split('\\');
        if (parts[0].Length != 1 || !char.IsAsciiLetterUpper(parts[0][0])) return false;
        for (var i = 1; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length == 0 || p == "." || p == ".." || p.EndsWith(' ')) return false;
        }
        return true;
    }

    /// <summary>true, wenn <paramref name="path"/> gleich <paramref name="prefix"/> ist oder darunter liegt (Speicherpfade, ohne Groß-/Kleinschreibung).</summary>
    public static bool IsUnderStorePrefix(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || (path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path[prefix.Length] == '\\');

    /// <summary>
    /// Zielpfad für "Wiederherstellen in Ordner": der gewählte Eintrag (Datei/Ordner) landet mit seinem Namen im Zielordner,
    /// darunter die relative Struktur. Beispiel: Auswahl "C\Users\a\Projekt", Datei "C\Users\a\Projekt\src\x.cs", Ziel "D:\R"
    /// → "D:\R\Projekt\src\x.cs".
    /// </summary>
    public static string MapToFolder(string storeRelative, string selectedPrefix, string destinationFolder)
    {
        if (!IsValidStoreRelative(storeRelative) || !IsValidStoreRelative(selectedPrefix) || !IsUnderStorePrefix(storeRelative, selectedPrefix))
            throw new ArgumentException("Ungültige Auswahl.", nameof(storeRelative));
        var parentIdx = selectedPrefix.LastIndexOf('\\');
        var relative = parentIdx < 0 ? storeRelative : storeRelative[(parentIdx + 1)..];
        var dest = Path.GetFullPath(Path.Combine(destinationFolder, relative));
        var root = Path.GetFullPath(destinationFolder).TrimEnd('\\') + "\\";
        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Pfad verlässt den Zielordner.", nameof(storeRelative));
        return dest;
    }

    /// <summary>Datei-/Ordnernamen für die Anzeige des Zeitstempels einer Treibersicherung: yyyyMMdd-HHmmss.</summary>
    public static string DriverStamp(DateTime local) => local.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
