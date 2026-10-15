using System.Text;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Backup;

/// <summary>
/// Ausschlussmuster für das Datei-Backup.
///  - Muster OHNE "\" oder "/" gelten für jeden Datei- UND Ordnernamen (z. B. "node_modules", "bin", "*.tmp", "~$*", "Thumbs.db").
///    Ein ausgeschlossener Ordner wird samt Inhalt übersprungen.
///  - Muster MIT "\" gelten für den Pfad relativ zur Quelle (z. B. "Cache\*", "**\logs\*.log"); "*" passt nicht über "\" hinweg, "**" schon.
///  - Platzhalter: "*" beliebig viele Zeichen, "?" genau ein Zeichen. Groß-/Kleinschreibung wird ignoriert (Windows).
/// </summary>
public sealed class ExclusionMatcher
{
    private readonly List<Regex> _namePatterns = [];
    private readonly List<Regex> _pathPatterns = [];

    public IReadOnlyList<string> Patterns { get; }

    public ExclusionMatcher(IEnumerable<string>? patterns)
    {
        var list = new List<string>();
        foreach (var raw in patterns ?? [])
        {
            var p = Normalize(raw);
            if (p is null) continue;
            list.Add(p);
            var regex = new Regex(ToRegex(p), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
            (p.Contains('\\') ? _pathPatterns : _namePatterns).Add(regex);
        }
        Patterns = list;
    }

    /// <summary>Bereinigt ein Muster; null = ungültig/leer.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw.Trim().Replace('/', '\\').Trim('\\');
        if (p.Length == 0 || p.Length > 260 || p.Any(char.IsControl) || p.Contains(':') || p.Contains('"') || p.Contains('<') || p.Contains('>') || p.Contains('|'))
            return null;
        return p;
    }

    /// <summary>
    /// true, wenn der Eintrag ausgeschlossen ist. <paramref name="relativePath"/> ist relativ zur Quelle ("src\bin\x.dll").
    /// </summary>
    public bool IsExcluded(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return false;
        var rel = relativePath.Replace('/', '\\').Trim('\\');
        // Namensmuster gelten für jeden Teil des Pfads (ein ausgeschlossener Ordner schließt seinen Inhalt aus).
        foreach (var segment in rel.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            foreach (var r in _namePatterns)
                if (r.IsMatch(segment)) return true;
        foreach (var r in _pathPatterns)
            if (r.IsMatch(rel)) return true;
        return false;
    }

    private static string ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '*')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                {
                    i++;
                    // "**\" darf auch null Ordner abdecken
                    if (i + 1 < pattern.Length && pattern[i + 1] == '\\') { i++; sb.Append(@"(?:.*\\)?"); }
                    else sb.Append(".*");
                }
                else
                {
                    sb.Append(@"[^\\]*");
                }
            }
            else if (c == '?')
            {
                sb.Append(@"[^\\]");
            }
            else
            {
                sb.Append(Regex.Escape(c.ToString()));
            }
        }
        sb.Append('$');
        return sb.ToString();
    }
}
