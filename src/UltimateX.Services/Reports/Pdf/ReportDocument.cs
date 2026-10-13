namespace UltimateX.Services.Reports.Pdf;

/// <summary>RGB-Farbe (0–255) für den Bericht.</summary>
public readonly record struct RColor(byte R, byte G, byte B)
{
    public static RColor Hex(string hex)
    {
        hex = hex.TrimStart('#');
        return new RColor(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16));
    }
}

/// <summary>Zeichenbefehle einer Seite (Ursprung oben links, Einheit Punkt = 1/72 Zoll).</summary>
public abstract record DrawOp;

public sealed record RectOp(double X, double Y, double W, double H, RColor? Fill, RColor? Stroke, double LineWidth = 0.75) : DrawOp;

public sealed record LineOp(double X1, double Y1, double X2, double Y2, RColor Color, double LineWidth = 0.75) : DrawOp;

/// <summary>Polygon/Polylinie. Geschlossen + Fill = Fläche; offen = Linienzug (z. B. Diagramm).</summary>
public sealed record PathOp(IReadOnlyList<(double X, double Y)> Points, bool Closed, RColor? Fill, RColor? Stroke, double LineWidth = 1) : DrawOp;

/// <summary>Text, Y = Grundlinie. Schrift: Helvetica (PDF-Standardschrift) bzw. Arial in WPF – metrisch identisch.</summary>
public sealed record TextOp(double X, double Y, string Text, double Size, bool Bold, RColor Color) : DrawOp;

public sealed class ReportPage
{
    public const double Width = 595.28;   // A4
    public const double Height = 841.89;
    public List<DrawOp> Ops { get; } = [];
}

public sealed class ReportDocument
{
    public List<ReportPage> Pages { get; } = [];
    public string Title { get; init; } = "ULTIMATE X PC Report";
    public string Author { get; init; } = "ULTIMATE X Windows Control";
    public DateTime Created { get; init; } = DateTime.Now;
}

/// <summary>
/// Textbreiten der PDF-Standardschrift Helvetica/Helvetica-Bold (Adobe-AFM, 1/1000 em). Arial hat dieselben Breiten,
/// daher stimmen Zeilenumbrüche in Vorschau/Druck (WPF) und PDF überein.
/// </summary>
public static class FontMetrics
{
    private static readonly short[] Regular = Build(
        " 278|!278|\"355|#556|$556|%889|&667|'191|(333|)333|*389|+584|,278|-333|.278|/278|:278|;278|<584|=584|>584|?556|@1015|" +
        "A667|B667|C722|D722|E667|F611|G778|H722|I278|J500|K667|L556|M833|N722|O778|P667|Q778|R722|S667|T611|U722|V667|W944|X667|Y667|Z611|" +
        "[278|\\278|]278|^469|_556|`333|a556|b556|c500|d556|e556|f278|g556|h556|i222|j222|k500|l222|m833|n556|o556|p556|q556|r333|s500|t278|" +
        "u556|v500|w722|x500|y500|z500|{334|}334|~584|Ä667|Ö778|Ü722|ä556|ö556|ü556|ß611|é556|è556|°400|·278|•350|–556|—1000|€556|" +
        "©737|±584|×584|µ556|„333|“333|”333|‘222|’222|…1000|§556|²333|³333|½834|¼834",
        556, 260);

    private static readonly short[] Bold = Build(
        " 278|!333|\"474|#556|$556|%889|&722|'238|(333|)333|*389|+584|,278|-333|.278|/278|:333|;333|<584|=584|>584|?611|@975|" +
        "A722|B722|C722|D722|E667|F611|G778|H722|I278|J556|K722|L611|M833|N722|O778|P667|Q778|R722|S667|T611|U722|V667|W944|X667|Y667|Z611|" +
        "[333|\\278|]333|^584|_556|`333|a556|b611|c556|d611|e556|f333|g611|h611|i278|j278|k556|l278|m889|n611|o611|p611|q611|r389|s556|t333|" +
        "u611|v556|w778|x556|y556|z500|{389|}389|~584|Ä722|Ö778|Ü722|ä556|ö611|ü611|ß611|é556|è556|°400|·278|•350|–556|—1000|€556|" +
        "©737|±584|×584|µ611|„500|“500|”500|‘278|’278|…1000|§556|²333|³333|½834|¼834",
        556, 280);

    private static short[] Build(string spec, short fallback, short pipe)
    {
        var table = new short[0x2100];
        Array.Fill(table, fallback);
        for (var c = '0'; c <= '9'; c++) table[c] = 556;
        foreach (var entry in spec.Split('|', StringSplitOptions.None))
        {
            if (entry.Length < 2) continue;
            var ch = entry[0];
            if (short.TryParse(entry[1..], out var w)) table[ch] = w;
        }
        table['|'] = pipe; // Trennzeichen der Tabelle, daher separat
        return table;
    }

    public static double Width(string text, double size, bool bold)
    {
        var t = bold ? Bold : Regular;
        double sum = 0;
        foreach (var ch in text) sum += ch < t.Length ? t[ch] : 556;
        return sum * size / 1000.0;
    }

    /// <summary>Bricht Text auf die angegebene Breite um (Wortgrenzen; überlange Wörter werden hart getrennt).</summary>
    public static IReadOnlyList<string> Wrap(string text, double size, bool bold, double maxWidth)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var current = "";
            foreach (var word in paragraph.Split(' '))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (Width(candidate, size, bold) <= maxWidth) { current = candidate; continue; }
                if (current.Length > 0) lines.Add(current);
                current = word;
                while (Width(current, size, bold) > maxWidth && current.Length > 1)
                {
                    var cut = current.Length - 1;
                    while (cut > 1 && Width(current[..cut], size, bold) > maxWidth) cut--;
                    lines.Add(current[..cut]);
                    current = current[cut..];
                }
            }
            lines.Add(current);
        }
        return lines;
    }

    /// <summary>Kürzt Text mit „…“ auf die Breite.</summary>
    public static string Ellipsis(string text, double size, bool bold, double maxWidth)
    {
        if (Width(text, size, bold) <= maxWidth) return text;
        var s = text;
        while (s.Length > 1 && Width(s + "…", size, bold) > maxWidth) s = s[..^1];
        return s + "…";
    }
}
