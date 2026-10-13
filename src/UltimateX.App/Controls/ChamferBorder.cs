using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UltimateX.App.Controls;

/// <summary>
/// Kartenfläche des ULTIMATE X Designs: oben rechts und unten links abgeschrägt, dünne Graphitkante,
/// optional ein kurzer roter Akzentstrich an der Oberkante. Verhält sich ansonsten wie ein <see cref="Border"/>
/// (Padding, Kind-Element) und schneidet den Inhalt an der Kontur ab.
/// </summary>
public class ChamferBorder : Border
{
    public static readonly DependencyProperty ChamferProperty = DependencyProperty.Register(
        nameof(Chamfer), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender, OnShapeChanged));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentLengthProperty = DependencyProperty.Register(
        nameof(AccentLength), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Kantenlänge der Abschrägung in Pixeln (0 = rechteckig).</summary>
    public double Chamfer { get => (double)GetValue(ChamferProperty); set => SetValue(ChamferProperty, value); }

    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }

    /// <summary>Länge des Akzentstrichs oben links (0 = kein Akzent).</summary>
    public double AccentLength { get => (double)GetValue(AccentLengthProperty); set => SetValue(AccentLengthProperty, value); }

    private static void OnShapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((ChamferBorder)d).UpdateClip();

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateClip();
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var g = Outline(new Rect(0, 0, ActualWidth, ActualHeight), EffectiveChamfer());
        g.Freeze();
        Clip = g;
    }

    private double EffectiveChamfer() => Math.Max(0, Math.Min(Chamfer, Math.Min(ActualWidth, ActualHeight) / 3));

    internal static StreamGeometry Outline(Rect r, double c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(r.Left, r.Top), true, true);
            ctx.LineTo(new Point(r.Right - c, r.Top), true, false);
            ctx.LineTo(new Point(r.Right, r.Top + c), true, false);
            ctx.LineTo(new Point(r.Right, r.Bottom), true, false);
            ctx.LineTo(new Point(r.Left + c, r.Bottom), true, false);
            ctx.LineTo(new Point(r.Left, r.Bottom - c), true, false);
        }
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var c = EffectiveChamfer();
        var t = Math.Max(BorderThickness.Left, Math.Max(BorderThickness.Top, Math.Max(BorderThickness.Right, BorderThickness.Bottom)));
        Pen? pen = null;
        if (t > 0 && BorderBrush is not null)
        {
            pen = new Pen(BorderBrush, t);
            pen.Freeze();
        }
        var inset = t / 2;
        var rect = new Rect(inset, inset, Math.Max(0, w - t), Math.Max(0, h - t));
        dc.DrawGeometry(Background, pen, Outline(rect, Math.Max(0, c - inset / 2)));

        if (AccentLength > 0 && AccentBrush is not null)
        {
            var accent = new Pen(AccentBrush, 2);
            accent.Freeze();
            var len = Math.Min(AccentLength, Math.Max(0, w - c - 4));
            dc.DrawLine(accent, new Point(0, 1), new Point(len, 1));
        }
    }
}
