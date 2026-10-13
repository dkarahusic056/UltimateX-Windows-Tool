using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using UltimateX.Services.Reports.Pdf;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Zeichnet die geräteunabhängige Berichts-Zeichenliste in WPF – für die Vorschau (Bitmap) und den Druck (Vektor,
/// über den Windows-Druckdialog inkl. „Microsoft Print to PDF“). Arial entspricht metrisch der PDF-Schrift Helvetica.
/// </summary>
public static class ReportRenderer
{
    /// <summary>Punkt (1/72") → WPF-Einheit (1/96").</summary>
    public const double PointToDip = 96.0 / 72.0;

    private static readonly Typeface Regular = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static readonly Dictionary<RColor, SolidColorBrush> BrushCache = [];

    private static SolidColorBrush Brush(RColor c)
    {
        lock (BrushCache)
        {
            if (BrushCache.TryGetValue(c, out var b)) return b;
            b = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B));
            b.Freeze();
            BrushCache[c] = b;
            return b;
        }
    }

    public static void Draw(DrawingContext dc, ReportPage page, double scale, double pixelsPerDip)
    {
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ReportPage.Width * scale, ReportPage.Height * scale));
        foreach (var op in page.Ops)
        {
            switch (op)
            {
                case RectOp r:
                    dc.DrawRectangle(r.Fill is { } f ? Brush(f) : null, r.Stroke is { } s ? new Pen(Brush(s), r.LineWidth * scale) : null,
                        new Rect(r.X * scale, r.Y * scale, r.W * scale, r.H * scale));
                    break;
                case LineOp l:
                    dc.DrawLine(new Pen(Brush(l.Color), l.LineWidth * scale), new Point(l.X1 * scale, l.Y1 * scale), new Point(l.X2 * scale, l.Y2 * scale));
                    break;
                case PathOp p when p.Points.Count >= 2:
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(new Point(p.Points[0].X * scale, p.Points[0].Y * scale), p.Fill is not null, p.Closed);
                        for (var i = 1; i < p.Points.Count; i++) ctx.LineTo(new Point(p.Points[i].X * scale, p.Points[i].Y * scale), true, true);
                    }
                    geo.Freeze();
                    dc.DrawGeometry(p.Fill is { } pf ? Brush(pf) : null,
                        p.Stroke is { } ps ? new Pen(Brush(ps), p.LineWidth * scale) { LineJoin = PenLineJoin.Round } : null, geo);
                    break;
                case TextOp t:
                    var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, t.Bold ? Bold : Regular,
                        Math.Max(1, t.Size * scale), Brush(t.Color), pixelsPerDip);
                    dc.DrawText(ft, new Point(t.X * scale, t.Y * scale - ft.Baseline));
                    break;
            }
        }
    }

    /// <summary>Vorschaubild einer Seite (Breite in Pixeln).</summary>
    public static BitmapSource RenderBitmap(ReportPage page, int widthPx)
    {
        var scale = widthPx / ReportPage.Width;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, page, scale, 1.0);
        var bmp = new RenderTargetBitmap(widthPx, (int)Math.Ceiling(ReportPage.Height * scale), 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Paginator für den Windows-Druckdialog (Vektorausgabe, A4).</summary>
    public sealed class Paginator(ReportDocument doc) : DocumentPaginator
    {
        public override DocumentPage GetPage(int pageNumber)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) Draw(dc, doc.Pages[pageNumber], PointToDip, 1.0);
            var size = new Size(ReportPage.Width * PointToDip, ReportPage.Height * PointToDip);
            return new DocumentPage(visual, size, new Rect(size), new Rect(size));
        }

        public override bool IsPageCountValid => true;
        public override int PageCount => doc.Pages.Count;
        public override Size PageSize { get; set; } = new(ReportPage.Width * PointToDip, ReportPage.Height * PointToDip);
        public override IDocumentPaginatorSource Source => null!;
    }
}
