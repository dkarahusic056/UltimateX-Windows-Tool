using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using UltimateX.Core.Localization;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Übersetzt die Oberfläche zur Laufzeit (§84): Jeder geladene TextBlock, jede Schaltfläche, Überschrift und
/// jeder Tooltip wird beobachtet; ändert sich der (gebundene) Text, wird er über <see cref="Loc"/> übersetzt.
/// Eingabefelder (TextBox) werden nie verändert. Ohne englische Sprache ist der Übersetzer inaktiv.
/// Die Beobachtung erfolgt über Bindungen statt DependencyPropertyDescriptor → keine Speicherlecks bei
/// virtualisierten Listen.
/// </summary>
public static class UiTranslator
{
    private static readonly DependencyProperty WatchersProperty =
        DependencyProperty.RegisterAttached("UxWatchers", typeof(List<Watcher>), typeof(UiTranslator));

    public static void Install()
    {
        if (!Loc.IsEnglish) return;
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded), true);
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.ToolTipOpeningEvent, new ToolTipEventHandler(OnToolTipOpening), true);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        switch (sender)
        {
            case TextBox:
            case PasswordBox:
                return;
            case TextBlock tb:
                Watch(tb, TextBlock.TextProperty);
                foreach (var run in tb.Inlines.OfType<Run>()) Watch(run, Run.TextProperty);
                break;
            case Window w:
                Watch(w, Window.TitleProperty);
                break;
            case HeaderedContentControl hc:
                if (hc.Header is string) Watch(hc, HeaderedContentControl.HeaderProperty);
                if (hc.Content is string) Watch(hc, ContentControl.ContentProperty);
                break;
            case HeaderedItemsControl hi:
                if (hi.Header is string) Watch(hi, HeaderedItemsControl.HeaderProperty);
                break;
            case ContentControl cc:
                if (cc.Content is string) Watch(cc, ContentControl.ContentProperty);
                break;
            case DataGrid dg:
                foreach (var col in dg.Columns)
                    if (col.Header is string h) col.Header = Loc.T(h);
                break;
        }
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.ToolTip is string s)
        {
            var t = Loc.T(s);
            if (t != s) fe.SetCurrentValue(FrameworkElement.ToolTipProperty, t);
        }
    }

    private static void Watch(DependencyObject target, DependencyProperty property)
    {
        var list = (List<Watcher>?)target.GetValue(WatchersProperty);
        if (list is null)
        {
            list = [];
            target.SetValue(WatchersProperty, list);
        }
        if (!list.Any(w => w.Property == property)) list.Add(new Watcher(target, property));
        Apply(target, property);
    }

    private static void Apply(DependencyObject target, DependencyProperty property)
    {
        if (target.GetValue(property) is not string s || s.Length == 0) return;
        var t = Loc.T(s);
        if (!string.Equals(t, s, StringComparison.Ordinal)) target.SetCurrentValue(property, t);
    }

    /// <summary>Hilfsobjekt, das per OneWay-Bindung Änderungen einer Eigenschaft meldet.</summary>
    private sealed class Watcher : DependencyObject
    {
        private static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register("Value", typeof(object), typeof(Watcher), new PropertyMetadata(null, OnValueChanged));

        private readonly WeakReference<DependencyObject> _target;

        public DependencyProperty Property { get; }

        public Watcher(DependencyObject target, DependencyProperty property)
        {
            Property = property;
            _target = new WeakReference<DependencyObject>(target);
            BindingOperations.SetBinding(this, ValueProperty, new Binding { Source = target, Path = new PropertyPath(property), Mode = BindingMode.OneWay });
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var w = (Watcher)d;
            if (w._target.TryGetTarget(out var target)) Apply(target, w.Property);
        }
    }
}
