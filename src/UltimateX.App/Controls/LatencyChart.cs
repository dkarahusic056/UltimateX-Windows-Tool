using System.Globalization;
using System.Windows;
using System.Windows.Media;
using UltimateX.Services.Network;

namespace UltimateX.App.Controls;

/// <summary>
/// Live-Latenzdiagramm für Ping: ein Balken je Antwort, verlorene Pakete als gedimmter roter Balken über die volle Höhe
/// (statt einer vorgetäuschten 0 ms). Skala passt sich dem Maximum an. Zeichnet per OnRender ohne Bibliothek.
/// </summary>
public sealed class LatencyChart : FrameworkElement
{
    public static readonly DependencyProperty RepliesProperty = DependencyProperty.Register(
        nameof(Replies), typeof(IReadOnlyList<PingReplyInfo>), typeof(LatencyChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(LatencyChart), new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LossBrushProperty = DependencyProperty.Register(
        nameof(LossBrush), typeof(Brush), typeof(LatencyChart), new FrameworkPropertyMetadata(Brushes.DarkRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(LatencyChart), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(LatencyChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<PingReplyInfo>? Replies { get => (IReadOnlyList<PingReplyInfo>?)GetValue(RepliesProperty); set => SetValue(RepliesProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush LossBrush { get => (Brush)GetValue(LossBrushProperty); set => SetValue(LossBrushProperty, value); }
    public Brush GridBrush { get => (Brush)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    private const int MaxBars = 60;
    private const double LabelWidth = 42;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth - LabelWidth, h = ActualHeight - 4;
        if (w <= 10 || h <= 10) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

        var all = Replies ?? [];
        var items = all.Count > MaxBars ? all.Skip(all.Count - MaxBars).ToList() : all.ToList();
        var max = items.Where(r => r.RoundtripMs is not null).Select(r => (double)r.RoundtripMs!.Value).DefaultIfEmpty(0).Max();
        var scale = NiceMax(Math.Max(max, 5));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        var gridPen = new Pen(GridBrush, 1);
        foreach (var f in new[] { 0.0, 0.5, 1.0 })
        {
            double y = 2 + h - f * h;
            dc.DrawLine(gridPen, new Point(LabelWidth, y), new Point(LabelWidth + w, y));
            var text = new FormattedText($"{scale * f:0} ms", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, 10, LabelBrush, dpi);
            dc.DrawText(text, new Point(LabelWidth - text.Width - 6, y - text.Height / 2));
        }

        if (items.Count == 0) return;
        double slot = w / MaxBars;
        double bar = Math.Max(2, slot * 0.68);
        var lossBrush = LossBrush.CloneCurrentValue();
        lossBrush.Opacity = 0.45;
        for (var i = 0; i < items.Count; i++)
        {
            var r = items[i];
            double x = LabelWidth + (MaxBars - items.Count + i) * slot + (slot - bar) / 2;
            if (r.Success && r.RoundtripMs is { } ms)
            {
                var bh = Math.Max(2, Math.Min(1, ms / scale) * h);
                dc.DrawRectangle(BarBrush, null, new Rect(x, 2 + h - bh, bar, bh));
            }
            else
            {
                dc.DrawRectangle(lossBrush, null, new Rect(x, 2, bar, h));
            }
        }
    }

    private static double NiceMax(double v)
    {
        foreach (var step in new double[] { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000 })
            if (v <= step) return step;
        return Math.Ceiling(v / 1000) * 1000;
    }
}
