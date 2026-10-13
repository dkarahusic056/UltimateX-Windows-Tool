using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace UltimateX.Services.Reports.Pdf;

/// <summary>
/// Schreibt einen <see cref="ReportDocument"/> als echtes PDF 1.4 (Vektorgrafik + Text, Flate-komprimiert).
/// Bewusst ohne Fremdbibliothek: keine zusätzliche Abhängigkeit, keine nativen Binärdateien, keine Lizenzfragen.
/// Schriften: Helvetica/Helvetica-Bold (PDF-Standardschriften, WinAnsiEncoding) – in jedem PDF-Betrachter vorhanden.
/// </summary>
public static class PdfWriter
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static byte[] Write(ReportDocument doc)
    {
        using var ms = new MemoryStream();
        var offsets = new List<long>();
        void Raw(string s)
        {
            var b = Encoding.ASCII.GetBytes(s);
            ms.Write(b, 0, b.Length);
        }
        int NewObj()
        {
            offsets.Add(ms.Position);
            var id = offsets.Count;
            Raw($"{id} 0 obj\n");
            return id;
        }

        var header = Encoding.Latin1.GetBytes("%PDF-1.4\n%\u00E2\u00E3\u00CF\u00D3\n");
        ms.Write(header, 0, header.Length);
        // Objektnummern: 1 Catalog, 2 Pages, 3 Font regular, 4 Font bold, 5 Info, danach je Seite Contents + Page.
        var pageCount = doc.Pages.Count;
        var pageIds = new int[pageCount];
        var contentIds = new int[pageCount];
        for (var i = 0; i < pageCount; i++)
        {
            contentIds[i] = 6 + i * 2;
            pageIds[i] = 7 + i * 2;
        }

        NewObj();
        Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        NewObj();
        Raw($"<< /Type /Pages /Count {pageCount} /Kids [{string.Join(" ", pageIds.Select(p => $"{p} 0 R"))}] >>\nendobj\n");
        NewObj();
        Raw("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>\nendobj\n");
        NewObj();
        Raw("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>\nendobj\n");
        NewObj();
        Raw($"<< /Title {Utf16(doc.Title)} /Author {Utf16(doc.Author)} /Producer {Utf16("ULTIMATE X Windows Control")} /CreationDate ({PdfDate(doc.Created)}) >>\nendobj\n");

        for (var i = 0; i < pageCount; i++)
        {
            var content = BuildContent(doc.Pages[i]);
            byte[] compressed;
            using (var cms = new MemoryStream())
            {
                using (var z = new ZLibStream(cms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(content, 0, content.Length);
                compressed = cms.ToArray();
            }
            NewObj();
            Raw($"<< /Length {compressed.Length} /Filter /FlateDecode >>\nstream\n");
            ms.Write(compressed, 0, compressed.Length);
            Raw("\nendstream\nendobj\n");
            NewObj();
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(ReportPage.Width)} {N(ReportPage.Height)}] " +
                $"/Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> /Contents {contentIds[i]} 0 R >>\nendobj\n");
        }

        var xref = ms.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) Raw($"{o:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R /Info 5 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static byte[] BuildContent(ReportPage page)
    {
        var sb = new StringBuilder();
        const double h = ReportPage.Height;
        foreach (var op in page.Ops)
        {
            switch (op)
            {
                case RectOp r:
                    sb.Append("q ");
                    if (r.Fill is { } f) sb.Append(Rgb(f, false));
                    if (r.Stroke is { } s) sb.Append(Rgb(s, true)).Append(N(r.LineWidth)).Append(" w ");
                    sb.Append(N(r.X)).Append(' ').Append(N(h - r.Y - r.H)).Append(' ').Append(N(r.W)).Append(' ').Append(N(r.H)).Append(" re ");
                    sb.Append(r.Fill is not null && r.Stroke is not null ? "B" : r.Fill is not null ? "f" : "S").Append(" Q\n");
                    break;
                case LineOp l:
                    sb.Append("q ").Append(Rgb(l.Color, true)).Append(N(l.LineWidth)).Append(" w ")
                      .Append(N(l.X1)).Append(' ').Append(N(h - l.Y1)).Append(" m ")
                      .Append(N(l.X2)).Append(' ').Append(N(h - l.Y2)).Append(" l S Q\n");
                    break;
                case PathOp p when p.Points.Count >= 2:
                    sb.Append("q 1 j ");
                    if (p.Fill is { } pf) sb.Append(Rgb(pf, false));
                    if (p.Stroke is { } ps) sb.Append(Rgb(ps, true)).Append(N(p.LineWidth)).Append(" w ");
                    for (var i = 0; i < p.Points.Count; i++)
                        sb.Append(N(p.Points[i].X)).Append(' ').Append(N(h - p.Points[i].Y)).Append(i == 0 ? " m " : " l ");
                    if (p.Closed) sb.Append("h ");
                    sb.Append(p.Fill is not null && p.Stroke is not null ? "B" : p.Fill is not null ? "f" : "S").Append(" Q\n");
                    break;
                case TextOp t:
                    sb.Append("BT ").Append(t.Bold ? "/F2 " : "/F1 ").Append(N(t.Size)).Append(" Tf ").Append(Rgb(t.Color, false))
                      .Append(N(t.X)).Append(' ').Append(N(h - t.Y)).Append(" Td ").Append(PdfString(t.Text)).Append(" Tj ET\n");
                    break;
            }
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string N(double v) => Math.Round(v, 2).ToString("0.##", Inv);

    private static string Rgb(RColor c, bool stroke) =>
        $"{N(c.R / 255.0)} {N(c.G / 255.0)} {N(c.B / 255.0)} {(stroke ? "RG" : "rg")} ";

    /// <summary>Text → PDF-Literal in WinAnsi (cp1252). Nicht darstellbare Zeichen werden sinnvoll ersetzt.</summary>
    internal static string PdfString(string text)
    {
        var sb = new StringBuilder("(");
        foreach (var ch in Normalize(text))
        {
            var b = ToWinAnsi(ch);
            switch (b)
            {
                case (byte)'(': sb.Append("\\("); break;
                case (byte)')': sb.Append("\\)"); break;
                case (byte)'\\': sb.Append("\\\\"); break;
                default:
                    if (b < 32 || b > 126) sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
                    else sb.Append((char)b);
                    break;
            }
        }
        return sb.Append(')').ToString();
    }

    private static string Normalize(string s) => s
        .Replace("→", "->", StringComparison.Ordinal).Replace("←", "<-", StringComparison.Ordinal)
        .Replace("↓", "v", StringComparison.Ordinal).Replace("↑", "^", StringComparison.Ordinal)
        .Replace("≥", ">=", StringComparison.Ordinal).Replace("≤", "<=", StringComparison.Ordinal)
        .Replace("▲", "+", StringComparison.Ordinal).Replace("▼", "-", StringComparison.Ordinal)
        .Replace("−", "-", StringComparison.Ordinal)
        .Replace("\t", "    ", StringComparison.Ordinal);

    private static byte ToWinAnsi(char c)
    {
        if (c < 0x80) return (byte)c;
        if (c is >= ' ' and <= 'ÿ') return (byte)c;
        return c switch
        {
            '€' => 0x80, '‚' => 0x82, 'ƒ' => 0x83, '„' => 0x84, '…' => 0x85, '†' => 0x86, '‡' => 0x87, 'ˆ' => 0x88, '‰' => 0x89,
            'Š' => 0x8A, '‹' => 0x8B, 'Œ' => 0x8C, 'Ž' => 0x8E, '‘' => 0x91, '’' => 0x92, '“' => 0x93, '”' => 0x94, '•' => 0x95,
            '–' => 0x96, '—' => 0x97, '˜' => 0x98, '™' => 0x99, 'š' => 0x9A, '›' => 0x9B, 'œ' => 0x9C, 'ž' => 0x9E, 'Ÿ' => 0x9F,
            _ => (byte)'?',
        };
    }

    private static string Utf16(string s)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (var b in Encoding.BigEndianUnicode.GetBytes(s)) sb.Append(b.ToString("X2", Inv));
        return sb.Append('>').ToString();
    }

    private static string PdfDate(DateTime t)
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(t);
        var sign = off < TimeSpan.Zero ? '-' : '+';
        off = off.Duration();
        return $"D:{t:yyyyMMddHHmmss}{sign}{off.Hours:D2}'{off.Minutes:D2}'";
    }
}
