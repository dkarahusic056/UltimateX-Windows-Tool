using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UltimateX.Core.Analysis;

/// <summary>Produkt aus NVIDIAs offizieller Treibersuche (psid = Produktserie, pfid = Produkt).</summary>
public sealed record NvidiaProduct(string Name, int Psid, int Pfid);

/// <summary>Neuester Treiber laut NVIDIA-Treibersuche (Game Ready, WHQL, DCH, Windows 10/11 64-Bit).</summary>
public sealed record NvidiaDriverInfo(string Version, string? Name, string? ReleaseDate, string? SizeText, string DownloadUrl, string? DetailsUrl);

/// <summary>
/// v1.3 NVIDIA-Grafiktreiber: Erkennung, Versionsvergleich und Auswertung der offiziellen NVIDIA-Treibersuche
/// (www.nvidia.com – Produktliste, gfwsl.geforce.com – Treiberabfrage). Download nur von *.download.nvidia.com über HTTPS;
/// ausgeführt wird die Datei erst nach Authenticode-Prüfung („NVIDIA Corporation“) – siehe NvidiaDriverService.
/// </summary>
public static partial class NvidiaDriver
{
    public const string ProductListUrl = "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3";

    /// <summary>Offizielle Download-Hosts (us.download.nvidia.com, international.download.nvidia.com …).</summary>
    public static IReadOnlyList<string> DownloadHosts { get; } = ["download.nvidia.com"];

    /// <summary>NVIDIA-Geräte haben die PCI-Hersteller-ID 10DE.</summary>
    public static bool IsNvidia(string? pnpDeviceId, string? name = null)
        => (pnpDeviceId?.Contains("VEN_10DE", StringComparison.OrdinalIgnoreCase) ?? false)
           || (pnpDeviceId is null && (name?.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ?? false));

    /// <summary>
    /// Windows-Treiberversion → NVIDIA-Version: die letzten fünf Ziffern der beiden letzten Blöcke,
    /// z. B. 32.0.16.1692 → 616.92, 31.0.15.5222 → 552.22. Ungültig → null.
    /// </summary>
    public static string? FromWindowsDriverVersion(string? windowsVersion)
    {
        if (string.IsNullOrWhiteSpace(windowsVersion)) return null;
        var parts = windowsVersion.Trim().Split('.');
        if (parts.Length < 4 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit))) return null;
        var digits = parts[^2] + parts[^1].PadLeft(4, '0');
        if (digits.Length < 5) return null;
        var last5 = digits[^5..];
        return $"{int.Parse(last5[..3], CultureInfo.InvariantCulture)}.{last5[3..]}";
    }

    /// <summary>Vergleicht NVIDIA-Versionen „617.42“ numerisch (größer = neuer). Nicht lesbar → 0.</summary>
    public static int Compare(string? a, string? b)
    {
        static decimal? P(string? v) => decimal.TryParse(v, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d) ? d : null;
        var (x, y) = (P(a), P(b));
        return x is null || y is null ? 0 : x.Value.CompareTo(y.Value);
    }

    /// <summary>Ist <paramref name="latest"/> neuer als <paramref name="installed"/>? Ohne installierte Version: ja.</summary>
    public static bool IsNewer(string latest, string? installed) => installed is null || Compare(latest, installed) > 0;

    /// <summary>Liest die Produktliste (XML: LookupValue mit ParentID = psid, Value = pfid, Name).</summary>
    public static IReadOnlyList<NvidiaProduct> ParseProductList(string xml)
    {
        var list = new List<NvidiaProduct>();
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return list; }
        foreach (var v in doc.Descendants("LookupValue"))
        {
            var name = v.Element("Name")?.Value?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (int.TryParse((string?)v.Attribute("ParentID"), NumberStyles.None, CultureInfo.InvariantCulture, out var psid)
                && int.TryParse(v.Element("Value")?.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var pfid))
                list.Add(new NvidiaProduct(name, psid, pfid));
        }
        return list;
    }

    private static string Normalize(string s)
    {
        s = s.Replace("(TM)", "", StringComparison.OrdinalIgnoreCase).Replace("(R)", "", StringComparison.OrdinalIgnoreCase);
        s = MultiSpace().Replace(s, " ").Trim().ToLowerInvariant();
        if (s.StartsWith("nvidia ", StringComparison.Ordinal)) s = s[7..];
        return s;
    }

    /// <summary>
    /// Sucht das Produkt zum Windows-Gerätenamen. Nur exakte Treffer (nach Entfernen von „NVIDIA“, (TM), Mehrfach-Leerzeichen) –
    /// „RTX 4080“ wird nie als „RTX 4080 SUPER“ erkannt. Laptop-GPUs heißen in Windows und bei NVIDIA „… Laptop GPU“.
    /// </summary>
    public static NvidiaProduct? FindProduct(IReadOnlyList<NvidiaProduct> products, string? gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return null;
        var n = Normalize(gpuName);
        return products.FirstOrDefault(p => Normalize(p.Name) == n);
    }

    /// <summary>Adresse der Treiberabfrage (osID 57 = Windows 10/11 64-Bit, WHQL, DCH, Englisch/international).</summary>
    public static string LookupUrl(NvidiaProduct p)
        => string.Create(CultureInfo.InvariantCulture,
            $"https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php?func=DriverManualLookup&psid={p.Psid}&pfid={p.Pfid}&osID=57&languageCode=1033&isWHQL=1&dch=1&sort1=0&numberOfResults=1");

    /// <summary>Wertet die JSON-Antwort aus (IDS[0].downloadInfo). Ungültig oder kein offizieller HTTPS-Download → null.</summary>
    public static NvidiaDriverInfo? ParseLookup(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("IDS", out var ids) || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0) return null;
            if (!ids[0].TryGetProperty("downloadInfo", out var d)) return null;
            string? S(string name) => d.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
            var version = S("Version");
            var url = S("DownloadURL");
            if (version is null || !VersionRegex().IsMatch(version) || !IsOfficialDownload(url)) return null;
            var name = S("Name") is { } nm ? Uri.UnescapeDataString(nm) : null;
            return new NvidiaDriverInfo(version, name, FormatDate(S("ReleaseDateTime")), S("DownloadURLFileSize"), url!, S("DetailsURL"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>„Tue Oct 06, 2026“ → „06.10.2026“; unbekanntes Format bleibt unverändert.</summary>
    public static string? FormatDate(string? nvidiaDate)
        => DateTime.TryParseExact(nvidiaDate?.Trim(), "ddd MMM dd, yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : nvidiaDate;

    /// <summary>Nur HTTPS, nur *.download.nvidia.com, nur .exe.</summary>
    public static bool IsOfficialDownload(string? url)
        => SoftwareCatalog.ValidateDownloadUrl(url, DownloadHosts).Ok
           && Uri.TryCreate(url, UriKind.Absolute, out var u) && u.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\d{3}\.\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpace();
}
