using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Analysis;

/// <summary>Ein Treffer der öffentlichen Steam-Shop-Suche (storesearch).</summary>
public sealed record SteamSearchItem(int AppId, string Name, string Type);

/// <summary>Ergebnis der Auswertung einer appdetails-Antwort: entweder ein Spiel oder eine deutsche Fehlermeldung.</summary>
public sealed record SteamAppResult(GameInfo? Game, string? Error);

/// <summary>
/// v1.3: Wertet die öffentliche Steam-Shop-API aus (ohne Steam-Konto). Die Systemanforderungen liefert Steam als HTML-Text
/// (data.pc_requirements.minimum/recommended). Daraus werden nur ausdrücklich genannte Werte übernommen –
/// unbekannte oder nicht lesbare Angaben bleiben 0 bzw. leer und erscheinen in der Bewertung als N/A.
/// Diese Klasse greift selbst nicht auf das Netzwerk zu (das macht der Dienst in UltimateX.Services).
/// </summary>
public static partial class SteamRequirements
{
    public const string StoreHost = "store.steampowered.com";

    public static string SearchUrl(string term)
        => $"https://{StoreHost}/api/storesearch/?term={Uri.EscapeDataString(term)}&l=english&cc=CH";

    public static string AppDetailsUrl(int appId)
        => $"https://{StoreHost}/api/appdetails?appids={appId.ToString(CultureInfo.InvariantCulture)}&l=english&filters=basic";

    public static string SourceText(int appId, DateTime fetched)
        => $"Steam (AppID {appId.ToString(CultureInfo.InvariantCulture)}, abgerufen am {fetched.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)})";

    // ------------------------------------------------------------------ Regex

    [GeneratedRegex(@"<\s*/?\s*(?:br|li|p|ul|ol|div|h\d)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BreakTagRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"[ \t   ]+", RegexOptions.CultureInvariant)]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"^(?<label>[A-Za-z][A-Za-z0-9 ./&\-]{0,40}?)\s*\*?\s*:\s*(?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"(?<num>\d{1,3}(?:,\d{3})+|\d+(?:[.,]\d+)?)\s*(?<unit>tib|gib|mib|tb|gb|mb)(?![a-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizeRegex();

    [GeneratedRegex(@"\b(?:directx|direct x|dx)\s*(?:version\s*)?(?<v>\d{1,2})(?:\.\d+)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DirectXInTextRegex();

    [GeneratedRegex(@"(?<v>\d{1,2})(?:\.\d+)?", RegexOptions.CultureInvariant)]
    private static partial Regex FirstNumberRegex();

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s*(?:ghz|mhz)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockRegex();

    [GeneratedRegex(@"\d+(?:[.,]\d+)?\s*(?:gb|mb)(?![a-z])(?:\s*(?:of\s+)?(?:dedicated\s+)?(?:vram|gddr\d*x?|ram|video\s+memory|video\s+ram|memory))*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GpuMemoryRegex();

    [GeneratedRegex(@"(?:\b(?:or|and)\s+)?\b(?:better|higher|equivalent|equiv|newer|above|faster|similar|comparable|greater|superior|later|up)\b\.?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BetterRegex();

    [GeneratedRegex(@"\s+or\s+|\s+oder\s+|\s*[/|,;]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlternativeSplitRegex();

    [GeneratedRegex(@"®|™|©|\((?:r|tm)\)|@", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarksRegex();

    [GeneratedRegex(@"\b(?:processor|cpu|graphics\s+card|video\s+card|card|gpu|class|compatible|dedicated|minimum|recommended|series)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoiseWordRegex();

    [GeneratedRegex(@"solid[\s\-]*state(?:\s*(?:drive|disk))?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SolidStateRegex();

    [GeneratedRegex(@"\b(?:required|requires|require|mandatory|necessary|needed|must)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RequiredWordRegex();

    [GeneratedRegex(@"\bno\s+ssd\b|\bwithout\s+(?:an?\s+)?ssd\b|\bssd\s*(?:is\s+)?(?:not|optional)\b|\bssd\b[^,;]{0,25}?\bnot\s+(?:required|needed|necessary|mandatory|recommended)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SsdNegationRegex();

    [GeneratedRegex(@"^(?:directx|direct x|dx\d|shader|opengl|vulkan|pixel shader|vertex shader)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ApiNameRegex();

    [GeneratedRegex(@"^(?:or|oder|and|und)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeadingConjunctionRegex();

    [GeneratedRegex(@"\bssd\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SsdRegex();

    private static readonly HashSet<string> VendorOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        "intel", "amd", "nvidia", "geforce", "radeon", "ati", "intel core", "amd ryzen", "nvidia geforce", "amd radeon", "core", "ryzen", "any",
    };

    // ------------------------------------------------------------------ HTML → Felder

    /// <summary>Zerlegt den Steam-HTML-Text in Textzeilen (Tags entfernt, HTML-Entities aufgelöst).</summary>
    public static IReadOnlyList<string> HtmlToLines(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];
        var text = BreakTagRegex().Replace(html, "\n");
        text = AnyTagRegex().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        var lines = new List<string>();
        foreach (var raw in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = SpacesRegex().Replace(raw.Replace('\t', ' '), " ").Trim();
            if (line.Length > 0) lines.Add(line);
        }
        return lines;
    }

    /// <summary>Ordnet eine Steam-Beschriftung (z. B. „Video Card“, „Hard Disk Space“) einem Feld zu; unbekannt → null.</summary>
    public static string? FieldFor(string label)
    {
        var sb = new StringBuilder(label.Length);
        foreach (var c in label.ToLowerInvariant()) sb.Append(c is >= 'a' and <= 'z' ? c : ' ');
        var l = SpacesRegex().Replace(sb.ToString(), " ").Trim();
        return l switch
        {
            "os" or "operating system" or "os version" or "system" => "os",
            "processor" or "processors" or "cpu" => "cpu",
            "memory" or "ram" or "system memory" or "system ram" => "ram",
            "graphics" or "graphic" or "graphics card" or "graphic card" or "graphics cards" or "video card" or "video cards" or "video" or "gpu" => "gpu",
            "vram" or "video memory" or "video ram" or "dedicated video ram" or "graphics memory" => "vram",
            "directx" or "direct x" or "dx" => "dx",
            "storage" or "ssd" or "ssd space" or "hard drive" or "hard drive space" or "hard disk" or "hard disk space" or "disk space" or "hdd" or "hdd space"
                or "free disk space" or "free hard drive space" or "drive space" or "space" or "disk" => "storage",
            "additional notes" or "additional note" or "notes" or "note" or "additional" or "other requirements" or "other" => "notes",
            "minimum" or "recommended" or "minimum requirements" or "recommended requirements" => "header",
            _ => null,
        };
    }

    private sealed class Fields
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public List<string> Other { get; } = [];
    }

    private static Fields ReadFields(string? html)
    {
        var f = new Fields();
        foreach (var line in HtmlToLines(html))
        {
            var m = LabelRegex().Match(line);
            var field = m.Success ? FieldFor(m.Groups["label"].Value) : null;
            if (field is null) { f.Other.Add(line); continue; }
            if (field == "header") continue;
            var value = m.Groups["value"].Value.Trim();
            if (value.Length == 0) continue;
            // Speicherzeile vollständig behalten – die Beschriftung kann „SSD“ enthalten.
            if (field == "storage") value = line;
            f.Values[field] = f.Values.TryGetValue(field, out var existing) ? existing + " " + value : value;
        }
        return f;
    }

    // ------------------------------------------------------------------ Werte

    /// <summary>Erste Größenangabe (MB/GB/TB) in GB; ohne Einheit → null (es wird nichts geraten).</summary>
    public static double? ParseSizeGb(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = SizeRegex().Match(text);
        return m.Success ? ToGb(m) : null;
    }

    private static double? ToGb(Match m)
    {
        var num = m.Groups["num"].Value;
        num = Regex.IsMatch(num, @"^\d{1,3}(?:,\d{3})+$", RegexOptions.CultureInvariant) ? num.Replace(",", "", StringComparison.Ordinal) : num.Replace(',', '.');
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || v <= 0) return null;
        return m.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "tb" or "tib" => v * 1024,
            "mb" or "mib" => v / 1024,
            _ => v,
        };
    }

    /// <summary>Ganze GB, aufgerundet (512 MB → 1 GB, 1,5 GB → 2 GB); 0 = unbekannt.</summary>
    public static int CeilGb(double? gb) => gb is { } v and > 0 ? (int)Math.Min(100_000, Math.Ceiling(v - 1e-9)) : 0;

    private static int ParseDirectX(string? dxField, IEnumerable<string> otherTexts)
    {
        if (!string.IsNullOrWhiteSpace(dxField))
        {
            var m = DirectXInTextRegex().Match(dxField);
            if (!m.Success) m = FirstNumberRegex().Match(dxField);
            if (m.Success && int.TryParse(m.Groups["v"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 7 and <= 12) return v;
        }
        foreach (var t in otherTexts)
        {
            var m = DirectXInTextRegex().Match(t);
            if (m.Success && int.TryParse(m.Groups["v"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 7 and <= 12) return v;
        }
        return 0;
    }

    /// <summary>Kleinste in der Grafikzeile genannte Speichergröße (z. B. „GTX 1060 6GB / RX 580 8GB“ → 6).</summary>
    public static int VramFromGraphicsText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var sizes = SizeRegex().Matches(text).Select(ToGb).Where(v => v is > 0 and <= 64).Select(v => v!.Value).ToList();
        return sizes.Count == 0 ? 0 : CeilGb(sizes.Min());
    }

    /// <summary>Teilt eine Zeile in Alternativen („A or B“, „A / B“, „A | B“, „A, B“) und bereinigt jeden Namen.</summary>
    public static IReadOnlyList<string> SplitAlternatives(string? value, bool gpu)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var s = MarksRegex().Replace(value, " ");
        if (gpu) s = GpuMemoryRegex().Replace(s, " ");
        s = BracketRegex().Replace(s, " ");
        s = ClockRegex().Replace(s, " ");
        s = BetterRegex().Replace(s, " ");
        var result = new List<string>();
        foreach (var part in AlternativeSplitRegex().Split(s))
        {
            var clean = gpu ? CleanGpuName(part) : CleanCpuName(part);
            if (clean.Length == 0 || result.Contains(clean, StringComparer.OrdinalIgnoreCase)) continue;
            result.Add(clean);
            if (result.Count >= 4) break;
        }
        return result;
    }

    private static string CleanCommon(string s)
    {
        s = MarksRegex().Replace(s, " ");
        s = BracketRegex().Replace(s, " ");
        s = ClockRegex().Replace(s, " ");
        s = BetterRegex().Replace(s, " ");
        s = NoiseWordRegex().Replace(s, " ");
        s = SpacesRegex().Replace(s, " ").Trim(' ', '-', '–', ',', '.', ';', ':', '+', '*', '(', ')', '&');
        // Rest einer Aufzählung („…, or AMD FX-8350“)
        s = LeadingConjunctionRegex().Replace(s, "");
        return s;
    }

    /// <summary>Bereinigt einen CPU-Namen (Klammern, Takt, „or better“), sodass <see cref="HardwareDatabase.EstimateCpu"/> ihn erkennt.</summary>
    public static string CleanCpuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = CleanCommon(name);
        if (s.Length < 3 || VendorOnly.Contains(s)) return string.Empty;
        // Ohne Ziffer nur behalten, wenn die Datenbank den Namen erkennt (sonst z. B. „Quad Core“).
        if (!s.Any(char.IsDigit) && !HardwareDatabase.EstimateCpu(s).Recognized) return string.Empty;
        return s;
    }

    /// <summary>Bereinigt einen GPU-Namen (Speicherangaben, Klammern, „or better“), sodass <see cref="HardwareDatabase.FindGpu"/> ihn erkennt.</summary>
    public static string CleanGpuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = GpuMemoryRegex().Replace(MarksRegex().Replace(name, " "), " ");
        s = CleanCommon(s);
        if (s.Length < 3 || VendorOnly.Contains(s) || ApiNameRegex().IsMatch(s)) return string.Empty;
        if (!s.Any(char.IsDigit) && HardwareDatabase.FindGpu(s) is null) return string.Empty;
        return s;
    }

    private static IEnumerable<string> Segments(IEnumerable<string> texts)
        => texts.SelectMany(t => Regex.Split(SolidStateRegex().Replace(t, "SSD"), @"[,;\n|–—]|\.\s", RegexOptions.CultureInvariant))
            .Where(seg => !string.IsNullOrWhiteSpace(seg));

    /// <summary>true, wenn eine SSD ausdrücklich verlangt wird („SSD required“, „Requires SSD“).</summary>
    public static bool SsdRequired(IEnumerable<string> texts)
        => Segments(texts).Any(seg => SsdRegex().IsMatch(seg) && RequiredWordRegex().IsMatch(seg) && !SsdNegationRegex().IsMatch(seg));

    /// <summary>true, wenn eine SSD erwähnt/empfohlen wird („SSD (Preferred)“, „SSD recommended“), ohne Verneinung.</summary>
    public static bool SsdMentioned(IEnumerable<string> texts)
        => Segments(texts).Any(seg => SsdRegex().IsMatch(seg) && !SsdNegationRegex().IsMatch(seg));

    // ------------------------------------------------------------------ Abschnitt / Gesamtergebnis

    /// <summary>
    /// Liest einen Abschnitt (minimum oder recommended). <paramref name="recommended"/> steuert die SSD-Regel:
    /// minimum → SSD nur bei ausdrücklicher Pflicht, recommended → SSD, sobald erwähnt/empfohlen.
    /// Liefert null, wenn der Text keine verwertbare Angabe enthält.
    /// </summary>
    public static GameRequirement? ParseSection(string? html, bool recommended)
    {
        var f = ReadFields(html);
        f.Values.TryGetValue("cpu", out var cpuText);
        f.Values.TryGetValue("gpu", out var gpuText);
        f.Values.TryGetValue("ram", out var ramText);
        f.Values.TryGetValue("vram", out var vramText);
        f.Values.TryGetValue("storage", out var storageText);
        f.Values.TryGetValue("dx", out var dxText);
        f.Values.TryGetValue("notes", out var notes);

        var cpu = SplitAlternatives(cpuText, gpu: false);
        var gpu = SplitAlternatives(gpuText, gpu: true);
        var ram = CeilGb(ParseSizeGb(ramText));
        var storage = CeilGb(ParseSizeGb(storageText));

        var vram = CeilGb(ParseSizeGb(vramText));
        if (vram == 0) vram = VramFromGraphicsText(gpuText);
        if (vram == 0)
        {
            var known = gpu.Select(HardwareDatabase.FindGpu).Where(g => g is { VramGb: > 0 }).Select(g => g!.VramGb).ToList();
            if (known.Count > 0) vram = known.Min();
        }

        var dx = ParseDirectX(dxText, new[] { gpuText, notes }.Where(t => t is not null).Select(t => t!).Concat(f.Other));

        var ssdTexts = new[] { storageText, notes }.Where(t => t is not null).Select(t => t!).Concat(f.Other).ToList();
        var ssd = recommended ? SsdMentioned(ssdTexts) : SsdRequired(ssdTexts);

        var any = cpu.Count > 0 || gpu.Count > 0 || ram > 0 || storage > 0 || vram > 0 || dx > 0;
        return any ? new GameRequirement(cpu, gpu, ram, vram, storage, ssd, dx) : null;
    }

    /// <summary>
    /// Mindest- und Empfehlungsanforderungen aus beiden HTML-Texten. Fehlt „recommended“, werden die Mindestwerte verwendet;
    /// fehlt „minimum“, die empfohlenen. Fehlende Einzelwerte der Empfehlung werden mit den Mindestwerten aufgefüllt
    /// (die Empfehlung kann nicht unter dem Minimum liegen). Beides leer → null.
    /// </summary>
    public static (GameRequirement Minimum, GameRequirement Recommended)? ParseRequirements(string? minimumHtml, string? recommendedHtml)
    {
        var min = ParseSection(minimumHtml, recommended: false);
        var rec = ParseSection(recommendedHtml, recommended: true);
        if (min is null && rec is null) return null;

        min ??= ParseSection(recommendedHtml, recommended: false)!;
        rec ??= ParseSection(minimumHtml, recommended: true)!;

        rec = rec with
        {
            Cpu = rec.Cpu.Count > 0 ? rec.Cpu : min.Cpu,
            Gpu = rec.Gpu.Count > 0 ? rec.Gpu : min.Gpu,
            RamGb = rec.RamGb > 0 ? rec.RamGb : min.RamGb,
            VramGb = rec.VramGb > 0 ? rec.VramGb : min.VramGb,
            StorageGb = rec.StorageGb > 0 ? rec.StorageGb : min.StorageGb,
            DirectX = rec.DirectX > 0 ? rec.DirectX : min.DirectX,
            Ssd = rec.Ssd || min.Ssd,
        };
        return (min, rec);
    }

    // ------------------------------------------------------------------ JSON der Steam-API

    /// <summary>Entfernt Markenzeichen und überflüssige Leerzeichen aus einem Spielnamen.</summary>
    public static string CleanGameName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var s = name.Replace("™", " ", StringComparison.Ordinal).Replace("®", " ", StringComparison.Ordinal).Replace("©", " ", StringComparison.Ordinal);
        s = SpacesRegex().Replace(s, " ").Trim();
        return s.Length > 200 ? s[..200] : s;
    }

    /// <summary>Wertet die Antwort von /api/appdetails aus. Ungültige oder leere Antworten liefern eine deutsche Fehlermeldung.</summary>
    public static SteamAppResult ParseAppDetails(string? json, int appId, DateTime fetched)
    {
        var id = appId.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(json)) return new SteamAppResult(null, "Steam hat eine leere Antwort geliefert.");
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty(id, out var entry) || entry.ValueKind != JsonValueKind.Object)
                return new SteamAppResult(null, $"Steam hat keine Daten zur AppID {id} geliefert.");
            if (!entry.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True
                || !entry.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return new SteamAppResult(null, $"Steam kennt die AppID {id} nicht oder sie ist in dieser Region nicht verfügbar.");

            var name = CleanGameName(data.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null);
            if (name.Length == 0) name = $"Steam-App {id}";

            string? minHtml = null, recHtml = null;
            if (data.TryGetProperty("pc_requirements", out var req) && req.ValueKind == JsonValueKind.Object)
            {
                if (req.TryGetProperty("minimum", out var mi) && mi.ValueKind == JsonValueKind.String) minHtml = mi.GetString();
                if (req.TryGetProperty("recommended", out var re) && re.ValueKind == JsonValueKind.String) recHtml = re.GetString();
            }
            var parsed = ParseRequirements(minHtml, recHtml);
            if (parsed is null)
                return new SteamAppResult(null, $"Steam enthält für „{name}“ keine lesbaren PC-Systemanforderungen.");

            var game = new GameInfo(name, [], appId, false, false, [], parsed.Value.Minimum, parsed.Value.Recommended,
                Online: true, Source: SourceText(appId, fetched));
            return new SteamAppResult(game, null);
        }
        catch (JsonException)
        {
            return new SteamAppResult(null, "Die Antwort von Steam ist ungültig oder unvollständig.");
        }
    }

    /// <summary>Wertet die Antwort von /api/storesearch aus (nur Einträge vom Typ „app“).</summary>
    public static IReadOnlyList<SteamSearchItem> ParseSearch(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
                return [];
            var list = new List<SteamSearchItem>();
            foreach (var it in items.EnumerateArray())
            {
                if (it.ValueKind != JsonValueKind.Object) continue;
                var type = it.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "app" : "app";
                if (!type.Equals("app", StringComparison.OrdinalIgnoreCase)) continue;
                if (!it.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out var appId) || appId <= 0) continue;
                var name = CleanGameName(it.TryGetProperty("name", out var nm) && nm.ValueKind == JsonValueKind.String ? nm.GetString() : null);
                if (name.Length == 0) continue;
                list.Add(new SteamSearchItem(appId, name, type));
                if (list.Count >= 50) break;
            }
            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static readonly string[] ExtraWords =
        ["soundtrack", "dlc", "demo", "artbook", "season", "server", "sdk", "editor", "playtest", "benchmark", "ost", "expansion", "upgrade", "pack", "bundle"];

    private static string[] Words(string s)
        => Regex.Split(s.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", RegexOptions.CultureInvariant).Where(w => w.Length > 0).ToArray();

    /// <summary>
    /// Wählt den passendsten Suchtreffer über den normalisierten Namen: exakt &gt; beginnt mit &gt; enthält &gt; Wortüberschneidung.
    /// Zusatzinhalte (Soundtrack, DLC, Demo …) werden abgewertet, wenn sie nicht gesucht wurden. Kein ähnlicher Treffer → null.
    /// </summary>
    public static SteamSearchItem? PickBestMatch(IEnumerable<SteamSearchItem> items, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var q = GameCompatibility.Normalize(CleanGameName(query));
        if (q.Length == 0) return null;
        var qWords = Words(query);
        SteamSearchItem? best = null;
        var bestScore = 0;
        foreach (var item in items)
        {
            var n = GameCompatibility.Normalize(CleanGameName(item.Name));
            if (n.Length == 0) continue;
            int score;
            if (n == q) score = 1000;
            else if (n.StartsWith(q, StringComparison.Ordinal)) score = 600 - Math.Min(300, n.Length - q.Length);
            else if (n.Length >= 4 && q.StartsWith(n, StringComparison.Ordinal)) score = 500 - Math.Min(300, q.Length - n.Length);
            else if (q.Length >= 3 && n.Contains(q, StringComparison.Ordinal)) score = 400 - Math.Min(300, n.Length - q.Length);
            else
            {
                var nWords = Words(item.Name);
                var inter = nWords.Intersect(qWords, StringComparer.Ordinal).Count();
                var union = nWords.Union(qWords, StringComparer.Ordinal).Count();
                score = union == 0 ? 0 : (int)(300.0 * inter / union);
            }
            if (score > 0)
            {
                var nameWords = Words(item.Name);
                if (ExtraWords.Any(w => nameWords.Contains(w, StringComparer.Ordinal) && !qWords.Contains(w, StringComparer.Ordinal))) score -= 150;
            }
            if (score > bestScore) { best = item; bestScore = score; }
        }
        return bestScore >= 100 ? best : null;
    }
}

/// <summary>Speicherformat der lokal gespeicherten Online-Spiele (%LOCALAPPDATA%\UltimateX\games-user.json).</summary>
public sealed record UserGameFile(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("games")] IReadOnlyList<GameInfo> Games);

public static class UserGameStore
{
    public const int MaxGames = 2000;

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Serialize(IEnumerable<GameInfo> games)
        => JsonSerializer.Serialize(new UserGameFile(1, games.Take(MaxGames).ToList()), Options);

    /// <summary>Liest das Speicherformat; ungültiges JSON → <see cref="JsonException"/>. Einträge werden bereinigt (keine null-Listen).</summary>
    public static IReadOnlyList<GameInfo> Deserialize(string json)
    {
        var file = JsonSerializer.Deserialize<UserGameFile>(json, Options);
        if (file?.Games is null) return [];
        var list = new List<GameInfo>();
        foreach (var g in file.Games)
        {
            if (g is null || string.IsNullOrWhiteSpace(g.Name)) continue;
            list.Add(Sanitize(g));
            if (list.Count >= MaxGames) break;
        }
        return list;
    }

    private static GameRequirement Fix(GameRequirement? r)
        => r is null
            ? new GameRequirement([], [], 0, 0, 0, false, 0)
            : new GameRequirement(r.Cpu ?? [], r.Gpu ?? [], Math.Max(0, r.RamGb), Math.Max(0, r.VramGb), Math.Max(0, r.StorageGb), r.Ssd, Math.Max(0, r.DirectX));

    private static GameInfo Sanitize(GameInfo g)
        => new(SteamRequirements.CleanGameName(g.Name), g.Aliases ?? [], Math.Max(0, g.SteamAppId), g.Esports, g.RayTracing, g.Upscalers ?? [],
            Fix(g.Minimum), Fix(g.Recommended), Online: true, Source: g.Source);
}
