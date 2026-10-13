using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltimateX.Core.Settings;

namespace UltimateX.Core.Localization;

/// <summary>
/// Laufzeit-Übersetzung (§84). Deutsch ist die Quellsprache; für Englisch liegt ein eingebettetes
/// Wörterbuch (en.json) vor. Es enthält exakte Texte und Vorlagen mit Platzhaltern ({0}, {1} …),
/// sodass auch zusammengesetzte Meldungen ("12 Dateien bereinigt") übersetzt werden. Platzhalter-Inhalte
/// werden ihrerseits übersetzt (z. B. Zustandswörter). Nicht übersetzbare Texte bleiben unverändert –
/// es wird nie etwas erfunden oder abgeschnitten.
/// </summary>
public static class Loc
{
    private sealed record Template(Regex Pattern, string English, string Anchor, int Holes);

    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal);
    private static readonly List<Template> Templates = [];
    private static readonly Dictionary<string, string> RawTemplates = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);
    private static readonly Regex SentenceSplit = new(@"(?<=[.!?])\s+(?=[\p{Lu}„""(•])", RegexOptions.CultureInvariant);
    private const int MaxCache = 20_000;

    /// <summary>Aktive Sprache: "de" oder "en".</summary>
    public static string Language { get; private set; } = "de";

    public static bool IsEnglish => Language == "en";

    public static CultureInfo Culture => CultureInfo.GetCultureInfo(IsEnglish ? "en-US" : "de-DE");

    /// <summary>Anzahl geladener Einträge (für Tests/Diagnose).</summary>
    public static int EntryCount => Exact.Count + Templates.Count;

    /// <summary>Ermittelt die Sprache aus der Einstellung ("Automatisch" = Windows-Anzeigesprache) und lädt das Wörterbuch.</summary>
    public static void Initialize(AppLanguage setting)
    {
        Language = setting switch
        {
            AppLanguage.Deutsch => "de",
            AppLanguage.English => "en",
            _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase) ? "de" : "en",
        };
        Exact.Clear();
        Templates.Clear();
        RawTemplates.Clear();
        Cache.Clear();
        if (!IsEnglish) return;

        using var stream = typeof(Loc).Assembly.GetManifestResourceStream("UltimateX.Core.Localization.en.json");
        if (stream is null) { Language = "de"; return; }
        Load(stream);
    }

    internal static void Load(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.GetProperty("exact").EnumerateObject())
            Exact[p.Name] = p.Value.GetString() ?? p.Name;

        var list = new List<Template>();
        foreach (var t in doc.RootElement.GetProperty("templates").EnumerateArray())
        {
            var de = t[0].GetString();
            var en = t[1].GetString();
            if (string.IsNullOrEmpty(de) || en is null) continue;
            var (regex, anchor, holes) = Compile(de);
            if (holes == 0) { Exact[de] = en; continue; }
            RawTemplates[de] = en;
            list.Add(new Template(regex, en, anchor, holes));
        }
        // Spezifischere Vorlagen (mehr fester Text) zuerst.
        Templates.AddRange(list.OrderByDescending(t => t.Pattern.ToString().Length - t.Holes * 5));
    }

    /// <summary>Für Tests: Sprache erzwingen und Wörterbuch aus einem Stream laden.</summary>
    internal static void InitializeForTests(Stream json)
    {
        Language = "en";
        Exact.Clear();
        Templates.Clear();
        RawTemplates.Clear();
        Cache.Clear();
        Load(json);
    }

    internal static void ResetForTests()
    {
        Language = "de";
        Exact.Clear();
        Templates.Clear();
        RawTemplates.Clear();
        Cache.Clear();
    }

    private static (Regex Regex, string Anchor, int Holes) Compile(string template)
    {
        var sb = new StringBuilder("^");
        var anchor = "";
        int holes = 0, i = 0;
        var literal = new StringBuilder();
        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            var lit = literal.ToString();
            if (lit.Trim().Length > anchor.Trim().Length) anchor = lit;
            sb.Append(Regex.Escape(lit));
            literal.Clear();
        }
        while (i < template.Length)
        {
            var m = Regex.Match(template[i..], @"^\{(\d+)\}");
            if (m.Success)
            {
                FlushLiteral();
                sb.Append("(?<h").Append(m.Groups[1].Value).Append(">.*?)");
                holes++;
                i += m.Length;
                continue;
            }
            literal.Append(template[i]);
            i++;
        }
        FlushLiteral();
        sb.Append('$');
        return (new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), anchor, holes);
    }

    /// <summary>Übersetzt einen Anzeigetext. Im deutschen Modus wird der Text unverändert zurückgegeben.</summary>
    public static string T(string? text)
    {
        if (text is null) return "";
        if (!IsEnglish || text.Length == 0) return text;
        if (Cache.TryGetValue(text, out var cached)) return cached;
        var result = Translate(text, 0);
        if (Cache.Count < MaxCache) Cache[text] = result;
        return result;
    }

    /// <summary>Formatierte Übersetzung: das deutsche Format wird übersetzt, dann mit den Argumenten gefüllt.</summary>
    public static string F(string germanFormat, params object?[] args)
    {
        var fmt = IsEnglish && (RawTemplates.TryGetValue(germanFormat, out var en) || Exact.TryGetValue(germanFormat, out en)) ? en : germanFormat;
        try { return string.Format(Culture, fmt, args); }
        catch (FormatException) { return string.Format(Culture, germanFormat, args); }
    }

    private static string Translate(string s, int depth)
    {
        if (depth > 5 || s.Length == 0) return s;
        if (Exact.TryGetValue(s, out var exact)) return exact;

        // Führende/abschließende Leerzeichen und Zeilenumbrüche erhalten.
        var trimmed = s.Trim();
        if (trimmed.Length == 0) return s;
        if (trimmed.Length != s.Length)
        {
            var inner = Translate(trimmed, depth + 1);
            if (!ReferenceEquals(inner, trimmed) && inner != trimmed)
            {
                var start = s.IndexOf(trimmed, StringComparison.Ordinal);
                return s[..start] + inner + s[(start + trimmed.Length)..];
            }
            return s;
        }

        foreach (var t in Templates)
        {
            if (t.Anchor.Length > 0 && !s.Contains(t.Anchor, StringComparison.Ordinal)) continue;
            var m = t.Pattern.Match(s);
            if (!m.Success) continue;
            var result = t.English;
            for (var h = 0; h < t.Holes; h++)
            {
                var g = m.Groups["h" + h.ToString(CultureInfo.InvariantCulture)];
                if (!g.Success) continue;
                result = result.Replace("{" + h.ToString(CultureInfo.InvariantCulture) + "}", Translate(g.Value, depth + 1), StringComparison.Ordinal);
            }
            return result;
        }

        // Satzzeichen am Ende, das in der Vorlage fehlt ("… freigegeben.")
        var last = s[^1];
        if (s.Length > 2 && (last == '.' || last == ':' || last == '!' || last == '?'))
        {
            var core = s[..^1];
            var tr = Translate(core, depth + 1);
            if (tr != core) return tr + last;
        }

        // Zusammengesetzte Meldungen: zeilen-, abschnitts- und satzweise übersetzen.
        foreach (var sep in new[] { "\n", " · ", " | " })
        {
            if (!s.Contains(sep, StringComparison.Ordinal)) continue;
            var parts = s.Split(sep);
            var changed = false;
            for (var p = 0; p < parts.Length; p++)
            {
                var tr = Translate(parts[p], depth + 1);
                if (tr != parts[p]) { parts[p] = tr; changed = true; }
            }
            if (changed) return string.Join(sep, parts);
        }

        var sentences = SentenceSplit.Split(s);
        if (sentences.Length > 1)
        {
            var changed = false;
            for (var p = 0; p < sentences.Length; p++)
            {
                var tr = Translate(sentences[p], depth + 1);
                if (tr != sentences[p]) { sentences[p] = tr; changed = true; }
            }
            if (changed) return string.Join(" ", sentences);
        }

        // "Bezeichnung: Wert"
        var colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && colon < 60)
        {
            var left = s[..colon];
            var right = s[(colon + 2)..];
            var l = Translate(left, depth + 1);
            var r = Translate(right, depth + 1);
            if (l != left || r != right) return l + ": " + r;
        }
        return s;
    }
}
