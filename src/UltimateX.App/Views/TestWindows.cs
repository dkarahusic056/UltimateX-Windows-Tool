using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using UltimateX.Core.Localization;

namespace UltimateX.App.Views;

/// <summary>
/// Bildschirm-Farbtest (v1.1 §Display): Vollbild in Rot, Grün, Blau, Weiß, Schwarz, Grau – zum Erkennen von Pixelfehlern
/// und ungleichmäßiger Ausleuchtung. Klick/Leertaste/→ = nächste Farbe, Esc = beenden. Es wird nichts aufgezeichnet.
/// </summary>
public sealed class ColorTestWindow : Window
{
    private static readonly (Color Color, string Name)[] Steps =
    [
        (Colors.Red, "Rot"), (Color.FromRgb(0, 255, 0), "Grün"), (Colors.Blue, "Blau"),
        (Colors.White, "Weiß"), (Colors.Black, "Schwarz"), (Color.FromRgb(128, 128, 128), "Grau"),
    ];

    private int _index;
    private readonly TextBlock _hint;

    public bool Completed { get; private set; }

    public ColorTestWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.None;
        Title = "ULTIMATE X – Farbtest";
        _hint = new TextBlock
        {
            FontSize = 16,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0.75,
        };
        Content = new Grid { Children = { _hint } };
        Apply();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key is Key.Space or Key.Right or Key.Enter or Key.PageDown) Next();
            else if (e.Key is Key.Left or Key.PageUp) { _index = Math.Max(0, _index - 1); Apply(); }
            e.Handled = true;
        };
        MouseLeftButtonUp += (_, _) => Next();
        MouseRightButtonUp += (_, _) => Close();
    }

    private void Next()
    {
        if (_index >= Steps.Length - 1)
        {
            Completed = true;
            Close();
            return;
        }
        _index++;
        Apply();
    }

    private void Apply()
    {
        var (color, name) = Steps[_index];
        Background = new SolidColorBrush(color);
        var bright = color.R * 0.3 + color.G * 0.59 + color.B * 0.11 > 128;
        _hint.Foreground = bright ? Brushes.Black : Brushes.White;
        _hint.Text = Loc.T($"{name} ({_index + 1}/{Steps.Length}) · Klick oder Leertaste = weiter · Esc = beenden");
    }
}

/// <summary>
/// Tastatur- und Maustest (v1.1 §Input): erfasst Eingaben AUSSCHLIESSLICH innerhalb dieses Fensters
/// (keine globalen Hooks, keine Aufzeichnung). Zeigt gedrückte Tasten, Maustasten, Mausrad, Doppelklick und Position.
/// </summary>
public sealed class InputTestWindow : Window
{
    private readonly HashSet<Key> _keys = [];
    private readonly WrapPanel _keyPanel = new();
    private readonly TextBlock _lastKey = new() { FontSize = 30, FontWeight = FontWeights.Bold };
    private readonly Dictionary<string, int> _mouse = new(StringComparer.Ordinal)
    {
        ["Links"] = 0, ["Rechts"] = 0, ["Mitte"] = 0, ["Taste 4"] = 0, ["Taste 5"] = 0, ["Rad hoch"] = 0, ["Rad runter"] = 0, ["Doppelklick"] = 0,
    };
    private readonly Dictionary<string, TextBlock> _mouseLabels = [];
    private readonly TextBlock _position = new();
    private readonly Border _pad;

    /// <summary>true = Benutzer bestätigt „funktioniert“, false = Problem, null = ohne Urteil geschlossen.</summary>
    public bool? KeyboardOk { get; private set; }
    public bool? MouseOk { get; private set; }
    public int DistinctKeys => _keys.Count;
    public IReadOnlyDictionary<string, int> MouseCounts => _mouse;

    public InputTestWindow()
    {
        Title = Loc.T("ULTIMATE X – Tastatur- und Maustest");
        Width = 980;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Brush.Background");
        SetResourceReference(ForegroundProperty, "Brush.Text");
        FontFamily = new FontFamily("Segoe UI");

        var root = new DockPanel { Margin = new Thickness(20) };

        var info = new TextBlock
        {
            Text = Loc.T("Tasten drücken und mit der Maus im roten Feld klicken/scrollen. Eingaben werden nur in diesem Fenster erfasst und nicht gespeichert. Hinweis: Die Windows-Taste und Tastenkombinationen wie Strg+Alt+Entf werden von Windows selbst verarbeitet."),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
            Opacity = 0.8,
        };
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(MakeButton("Tastatur OK, Maus OK", () => Finish(true, true)));
        buttons.Children.Add(MakeButton("Problem mit der Tastatur", () => Finish(false, MouseOk ?? true)));
        buttons.Children.Add(MakeButton("Problem mit der Maus", () => Finish(KeyboardOk ?? true, false)));
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var kb = new StackPanel();
        kb.Children.Add(Header("TASTATUR"));
        kb.Children.Add(new TextBlock { Text = Loc.T("Zuletzt gedrückt:"), Opacity = 0.7 });
        kb.Children.Add(_lastKey);
        kb.Children.Add(new TextBlock { Text = Loc.T("Erkannte Tasten:"), Opacity = 0.7, Margin = new Thickness(0, 10, 0, 6) });
        kb.Children.Add(new ScrollViewer { Content = _keyPanel, MaxHeight = 300, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        grid.Children.Add(kb);

        var ms = new DockPanel();
        Grid.SetColumn(ms, 2);
        var mh = Header("MAUS");
        DockPanel.SetDock(mh, Dock.Top);
        ms.Children.Add(mh);
        var counters = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var k in _mouse.Keys)
        {
            var t = new TextBlock { Margin = new Thickness(0, 2, 10, 2) };
            _mouseLabels[k] = t;
            counters.Children.Add(t);
        }
        DockPanel.SetDock(counters, Dock.Top);
        ms.Children.Add(counters);
        DockPanel.SetDock(_position, Dock.Bottom);
        ms.Children.Add(_position);
        _pad = new Border { BorderThickness = new Thickness(2), MinHeight = 220, Cursor = Cursors.Cross };
        _pad.SetResourceReference(Border.BorderBrushProperty, "Brush.AccentHot");
        _pad.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _pad.Child = new TextBlock { Text = Loc.T("Testfeld"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.4 };
        _pad.MouseDown += OnPadMouseDown;
        _pad.MouseWheel += (_, e) => { Count(e.Delta > 0 ? "Rad hoch" : "Rad runter"); e.Handled = true; };
        _pad.MouseMove += (_, e) => { var p = e.GetPosition(_pad); _position.Text = Loc.T($"Position im Testfeld: {p.X:0} / {p.Y:0}"); };
        ms.Children.Add(_pad);
        grid.Children.Add(ms);

        root.Children.Add(grid);
        Content = root;
        UpdateMouse();

        PreviewKeyDown += OnKey;
    }

    private static TextBlock Header(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.AccentHot");
        return t;
    }

    private Button MakeButton(string text, Action click)
    {
        var b = new Button { Content = Loc.T(text), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8, 14, 8) };
        b.Click += (_, _) => click();
        return b;
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
        if (key == Key.None) return;
        // Tab/Enter/Leertaste sollen hier geprüft werden, nicht die Schaltflächen bedienen.
        e.Handled = true;
        _lastKey.Text = key.ToString();
        if (_keys.Add(key))
        {
            var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 6) };
            chip.SetResourceReference(Border.BackgroundProperty, "Brush.AccentDeep");
            chip.Child = new TextBlock { Text = key.ToString(), Foreground = Brushes.White };
            _keyPanel.Children.Add(chip);
        }
    }

    private void OnPadMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) Count("Doppelklick");
        Count(e.ChangedButton switch
        {
            MouseButton.Left => "Links",
            MouseButton.Right => "Rechts",
            MouseButton.Middle => "Mitte",
            MouseButton.XButton1 => "Taste 4",
            _ => "Taste 5",
        });
        e.Handled = true;
    }

    private void Count(string key)
    {
        _mouse[key]++;
        UpdateMouse();
    }

    private void UpdateMouse()
    {
        foreach (var (k, t) in _mouseLabels) t.Text = $"{Loc.T(k)}: {_mouse[k]}";
    }

    private void Finish(bool keyboard, bool mouse)
    {
        KeyboardOk = keyboard;
        MouseOk = mouse;
        Close();
    }
}
