using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UltimateX.App.Controls;

/// <summary>
/// Mehrlinien-Diagramm 0–100 % (System Performance). Bis zu drei Reihen, horizontale Hilfslinien
/// bei 0/25/50/75/100. Direkt per OnRender gezeichnet → keine Chart-Bibliothek, geringe Last.
/// </summary>
public sealed class PerformanceChart : FrameworkElement
{
    private static DependencyProperty Series(string name) => DependencyProperty.Register(
        name, typeof(IReadOnlyList<double>), typeof(PerformanceChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty BrushProp(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(PerformanceChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty Series1Property = Series(nameof(Series1));
    public static readonly DependencyProperty Series2Property = Series(nameof(Series2));
    public static readonly DependencyProperty Series3Property = Series(nameof(Series3));
    public static readonly DependencyProperty Brush1Property = BrushProp(nameof(Brush1));
    public static readonly DependencyProperty Brush2Property = BrushProp(nameof(Brush2));
    public static readonly DependencyProperty Brush3Property = BrushProp(nameof(Brush3));
    public static readonly DependencyProperty GridBrushProperty = BrushProp(nameof(GridBrush));
    public static readonly DependencyProperty LabelBrushProperty = BrushProp(nameof(LabelBrush));

    public IReadOnlyList<double>? Series1 { get => (IReadOnlyList<double>?)GetValue(Series1Property); set => SetValue(Series1Property, value); }
    public IReadOnlyList<double>? Series2 { get => (IReadOnlyList<double>?)GetValue(Series2Property); set => SetValue(Series2Property, value); }
    public IReadOnlyList<double>? Series3 { get => (IReadOnlyList<double>?)GetValue(Series3Property); set => SetValue(Series3Property, value); }
    public Brush Brush1 { get => (Brush)GetValue(Brush1Property); set => SetValue(Brush1Property, value); }
    public Brush Brush2 { get => (Brush)GetValue(Brush2Property); set => SetValue(Brush2Property, value); }
    public Brush Brush3 { get => (Brush)GetValue(Brush3Property); set => SetValue(Brush3Property, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private const double LabelWidth = 30;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth - LabelWidth, h = ActualHeight - 4;
        if (w <= 10 || h <= 10) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var gridPen = new Pen(GridBrush, 1);
        foreach (var level in new[] { 0, 25, 50, 75, 100 })
        {
            double y = 2 + h - level / 100.0 * h;
            dc.DrawLine(gridPen, new Point(LabelWidth, y), new Point(LabelWidth + w, y));
            var text = new FormattedText(level.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, typeface, 10, LabelBrush, dpi);
            dc.DrawText(text, new Point(LabelWidth - text.Width - 6, y - text.Height / 2));
        }

        Fill(dc, Series1, Brush1, w, h);
        Draw(dc, Series1, Brush1, w, h);
        Draw(dc, Series2, Brush2, w, h);
        Draw(dc, Series3, Brush3, w, h);
    }

    /// <summary>Halbtransparente Verlaufsfläche unter der ersten Reihe (HUD-Optik, ein Geometrieaufruf).</summary>
    private static void Fill(DrawingContext dc, IReadOnlyList<double>? values, Brush brush, double w, double h)
    {
        if (values is null || values.Count < 2 || brush is not SolidColorBrush solid) return;
        double step = w / (values.Count - 1);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(new Point(LabelWidth, 2 + h), true, true);
            for (int i = 0; i < values.Count; i++)
                ctx.LineTo(new Point(LabelWidth + i * step, 2 + h - Math.Clamp(values[i], 0, 100) / 100.0 * h), false, false);
            ctx.LineTo(new Point(LabelWidth + (values.Count - 1) * step, 2 + h), false, false);
        }
        geo.Freeze();
        var top = solid.Color;
        var fill = new LinearGradientBrush(Color.FromArgb(70, top.R, top.G, top.B), Color.FromArgb(0, top.R, top.G, top.B), 90);
        fill.Freeze();
        dc.DrawGeometry(fill, null, geo);
    }

    private static void Draw(DrawingContext dc, IReadOnlyList<double>? values, Brush brush, double w, double h)
    {
        if (values is null || values.Count < 2) return;
        double step = w / (values.Count - 1);
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                var p = new Point(LabelWidth + i * step, 2 + h - Math.Clamp(values[i], 0, 100) / 100.0 * h);
                if (i == 0) ctx.BeginFigure(p, false, false);
                else ctx.LineTo(p, true, true);
            }
        }
        geo.Freeze();
        dc.DrawGeometry(null, new Pen(brush, 1.8) { LineJoin = PenLineJoin.Round }, geo);
    }
}
