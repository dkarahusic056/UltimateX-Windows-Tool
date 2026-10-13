using System.Windows;
using System.Windows.Media;

namespace UltimateX.App.Controls;

/// <summary>
/// Kreisförmige Prozentanzeige (Hardware Overview). Ohne Wert (null) wird nur die Spur gezeichnet –
/// so bleibt sichtbar, dass ein Wert nicht verfügbar ist, statt eine 0 vorzutäuschen.
/// </summary>
public sealed class Ring : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double?), typeof(Ring), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Ring), new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(
        nameof(RingBrush), typeof(Brush), typeof(Ring), new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ShowTicksProperty = DependencyProperty.Register(
        nameof(ShowTicks), typeof(bool), typeof(Ring), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>HUD-Skala (Teilstriche) außen um den Ring.</summary>
    public bool ShowTicks { get => (bool)GetValue(ShowTicksProperty); set => SetValue(ShowTicksProperty, value); }

    /// <summary>Prozent 0–100 oder null (nicht verfügbar).</summary>
    public double? Value { get => (double?)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        double tickSpace = ShowTicks && size > 60 ? 8 : 0;
        double r = size / 2 - Thickness / 2 - tickSpace;
        if (r <= 2) return;

        dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), c, r, r);

        if (tickSpace > 0)
        {
            // 60 Teilstriche, jeder fünfte länger – gefüllt bis zum aktuellen Wert in der Ringfarbe.
            var filled = Value is { } fv ? (int)Math.Round(Math.Clamp(fv, 0, 100) / 100.0 * 60) : 0;
            var dim = new Pen(TrackBrush, 1);
            var lit = new Pen(RingBrush, 1);
            double outer = size / 2 - 1;
            for (var i = 0; i < 60; i++)
            {
                var deg = -90 + i * 6;
                var inner = outer - (i % 5 == 0 ? 5 : 3);
                dc.DrawLine(i < filled ? lit : dim, At(c, inner, deg), At(c, outer, deg));
            }
        }

        if (Value is not { } v || v <= 0) return;
        var fraction = Math.Clamp(v / 100.0, 0, 1);
        var pen = new Pen(RingBrush, Thickness) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat };
        // weicher Glow unter dem Bogen (günstig: ein zusätzlicher, transparenter Stift statt Effekt)
        var glowBrush = RingBrush.CloneCurrentValue();
        glowBrush.Opacity = 0.22;
        var glow = new Pen(glowBrush, Thickness + 6);
        if (fraction >= 0.999)
        {
            dc.DrawEllipse(null, glow, c, r, r);
            dc.DrawEllipse(null, pen, c, r, r);
            return;
        }

        const double start = -90; // 12 Uhr
        double end = start + 360 * fraction;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(At(c, r, start), false, false);
            ctx.ArcTo(At(c, r, end), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true, false);
        }
        geo.Freeze();
        dc.DrawGeometry(null, glow, geo);
        dc.DrawGeometry(null, pen, geo);
    }

    private static Point At(Point c, double r, double deg)
    {
        var rad = deg * Math.PI / 180;
        return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad));
    }
}
