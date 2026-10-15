using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using UltimateX.Core.Analysis;
using UltimateX.Core.Settings;

namespace UltimateX.App.Views;

/// <summary>
/// In-Game-Overlay (v1.3): randloses, transparentes, immer oben liegendes Fenster ohne Taskleisten-Eintrag.
/// Durchklickbar und nie aktiviert (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) –
/// Maus und Tastatur bleiben beim Spiel. Positioniert wird in Gerätepixeln über SetWindowPos (PerMonitorV2).
/// Hinweis: Spiele im exklusiven Vollbildmodus zeigen kein fremdes Fenster darüber → randloses Fenster im Spiel wählen.
/// </summary>
public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    private OverlayCorner _corner = OverlayCorner.TopLeft;
    private string? _monitorDevice;
    private readonly ObservableCollection<LineItem> _items = [];
    private double _maxWidth;

    /// <summary>Zeile mit änderbarem Wert – die Zeilen werden nur bei geänderter Auswahl neu aufgebaut (kein Flackern).</summary>
    public sealed class LineItem(OverlayElement element, string label, string value) : INotifyPropertyChanged
    {
        private string _value = value;
        public OverlayElement Element { get; } = element;
        public string Label { get; } = label;
        public string Value
        {
            get => _value;
            set { if (_value == value) return; _value = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value))); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public OverlayWindow()
    {
        InitializeComponent();
        Lines.ItemsSource = _items;
        SizeChanged += (_, e) =>
        {
            // Nur wachsen, nie schrumpfen: wechselnde Zahlenbreiten (59 → 120 FPS) sollen das Fenster nicht springen lassen.
            if (e.NewSize.Width > _maxWidth) { _maxWidth = e.NewSize.Width; OverlayPanel.MinWidth = Math.Max(0, _maxWidth); }
            Reposition();
        };
        DpiChanged += (_, _) => Reposition();
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style | WsExTransparent | WsExLayered | WsExToolWindow | WsExNoActivate));
        Reposition();
    }

    /// <summary>Übernimmt Darstellung und Position aus den Einstellungen.</summary>
    public void ApplyAppearance(OverlaySettings s)
    {
        FontSize = s.FontSize;
        Opacity = s.Opacity;
        var alpha = (byte)Math.Round(Math.Clamp(s.BackgroundOpacity, 0, 1) * 255);
        var bg = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
        bg.Freeze();
        OverlayPanel.Background = bg;
        _corner = s.Corner;
        _monitorDevice = s.MonitorDevice;
        ResetWidth();
        Reposition();
    }

    public void SetLines(IReadOnlyList<OverlayLine> lines)
    {
        var same = lines.Count == _items.Count;
        for (var i = 0; same && i < lines.Count; i++)
            same = lines[i].Element == _items[i].Element && lines[i].Label == _items[i].Label;
        if (same)
        {
            for (var i = 0; i < lines.Count; i++) _items[i].Value = lines[i].Value; // nur Werte aktualisieren
        }
        else
        {
            _items.Clear();
            foreach (var l in lines) _items.Add(new LineItem(l.Element, l.Label, l.Value));
            ResetWidth();
        }
        var vis = lines.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (OverlayPanel.Visibility != vis) OverlayPanel.Visibility = vis;
    }

    private void ResetWidth()
    {
        _maxWidth = 0;
        OverlayPanel.MinWidth = 0;
    }

    /// <summary>Setzt das Fenster in die gewählte Ecke des gewählten Monitors (Gerätepixel, 12 px Abstand × Skalierung).</summary>
    public void Reposition()
    {
        var hwnd = Handle;
        if (hwnd == IntPtr.Zero) return;
        var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(sc => string.Equals(sc.DeviceName, _monitorDevice, StringComparison.OrdinalIgnoreCase))
                     ?? System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return;
        var bounds = screen.Bounds; // ganzer Monitor (Spiele laufen randlos über der Taskleiste)
        var scale = VisualTreeHelper.GetDpi(this);
        var w = (int)Math.Ceiling(ActualWidth * scale.DpiScaleX);
        var h = (int)Math.Ceiling(ActualHeight * scale.DpiScaleY);
        var margin = (int)Math.Round(12 * scale.DpiScaleX);
        var x = _corner is OverlayCorner.TopLeft or OverlayCorner.BottomLeft ? bounds.Left + margin : bounds.Right - w - margin;
        var y = _corner is OverlayCorner.TopLeft or OverlayCorner.TopRight ? bounds.Top + margin : bounds.Bottom - h - margin;
        SetWindowPos(hwnd, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    /// <summary>Manche Spiele/Launcher drängen sich nach vorn – Topmost regelmäßig erneuern (ohne Aktivierung).</summary>
    public void ReassertTopmost()
    {
        var hwnd = Handle;
        if (hwnd != IntPtr.Zero && IsVisible)
            SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
