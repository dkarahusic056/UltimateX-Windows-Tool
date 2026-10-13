using System.Windows;
using System.Windows.Media;

namespace UltimateX.App.Controls;

/// <summary>
/// Leichtgewichtiges Verlaufsdiagramm (§19). Zeichnet direkt per OnRender – keine Chart-Bibliothek nötig,
/// minimale CPU-Last auch bei 1 Hz Aktualisierung.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Fester Maximalwert (z. B. 100 für Prozent). 0 = automatisch aus den Daten.</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<double>? Values
    {
        get => (IReadOnlyList<double>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var values = Values;
        double w = ActualWidth, h = ActualHeight;
        if (values is null || values.Count < 2 || w <= 0 || h <= 0) return;

        var max = Maximum > 0 ? Maximum : Math.Max(1, values.Max() * 1.15);
        double step = w / (values.Count - 1);

        var line = new StreamGeometry();
        using (var ctx = line.Open())
        {
            for (int i = 0; i < values.Count; i++)
            {
                var p = new Point(i * step, h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1);
                if (i == 0) ctx.BeginFigure(p, false, false);
                else ctx.LineTo(p, true, true);
            }
        }
        line.Freeze();

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var ctx = area.Open())
            {
                ctx.BeginFigure(new Point(0, h), true, true);
                for (int i = 0; i < values.Count; i++)
                    ctx.LineTo(new Point(i * step, h - Math.Clamp(values[i] / max, 0, 1) * (h - 2) - 1), false, true);
                ctx.LineTo(new Point(w, h), false, true);
            }
            area.Freeze();
            dc.DrawGeometry(fill, null, area);
        }

        var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, line);
    }
}
