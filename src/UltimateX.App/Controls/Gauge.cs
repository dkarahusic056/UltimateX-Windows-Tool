using System.Windows;
using System.Windows.Media;

namespace UltimateX.App.Controls;

/// <summary>Tachometer-Anzeige für den Speedtest (§23). 240°-Bogen, logarithmische Skala bis 1 Gbit/s+.</summary>
public sealed class Gauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Gauge), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Maximalwert der Skala (in derselben Einheit wie Value, z. B. Mbit/s).</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Gauge), new FrameworkPropertyMetadata(1000.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(Gauge), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(Gauge), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush ValueBrush { get => (Brush)GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }

    private const double StartAngle = 150; // Grad, im Uhrzeigersinn von 3 Uhr
    private const double Sweep = 240;

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        const double thickness = 14;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - thickness;

        DrawArc(dc, center, radius, StartAngle, Sweep, TrackBrush, thickness);

        // Logarithmische Skala: kleine Werte bleiben sichtbar, Gigabit passt trotzdem.
        double fraction = Value <= 0 ? 0 : Math.Clamp(Math.Log10(1 + Value) / Math.Log10(1 + Maximum), 0, 1);
        if (fraction > 0.001) DrawArc(dc, center, radius, StartAngle, Sweep * fraction, ValueBrush, thickness);
    }

    private static void DrawArc(DrawingContext dc, Point c, double r, double start, double sweep, Brush brush, double thickness)
    {
        static Point At(Point c, double r, double deg)
        {
            var rad = deg * Math.PI / 180;
            return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(At(c, r, start), false, false);
            ctx.ArcTo(At(c, r, start + sweep), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawGeometry(null, pen, geo);
    }
}
