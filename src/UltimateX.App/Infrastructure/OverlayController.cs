using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using UltimateX.App.Views;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Settings;
using UltimateX.Services.Gaming;
using UltimateX.Services.Monitoring;
using UltimateX.Services.Sensors;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Steuert das In-Game-Overlay (v1.3): Einstellungen (overlay.json), Overlay-Fenster, globales Tastenkürzel
/// (RegisterHotKey auf einem Message-only-Fenster), Messwerte (MonitorHub + SensorManager + FPS über ETW),
/// Ping und die optionale Sitzungsaufzeichnung (CSV). Alles läuft auf dem UI-Thread; nur Ping und Dateizugriffe im Hintergrund.
///
/// Lebenszyklus: <see cref="Initialize"/> nach MonitorHub.Start() in App.OnStartup, <see cref="Shutdown"/> in App.OnExit.
/// </summary>
public static class OverlayController
{
    private const int HotkeyId = 0x5558; // „UX“
    private const int WmHotkey = 0x0312;
    private const uint ModNoRepeat = 0x4000;

    private static OverlayWindow? _window;
    private static HwndSource? _hotkeyHost;
    private static bool _hotkeyRegistered;
    private static bool _initialized;
    private static int _tick;
    private static double? _pingMs;
    private static int _pingRunning;
    private static StreamWriter? _recorder;
    private static int _previewRequests;

    public static OverlaySettings Settings { get; private set; } = new();

    public static bool IsVisible => _window?.IsVisible == true;

    public static bool IsRecording => _recorder is not null;

    public static string? RecordingFile { get; private set; }

    /// <summary>Letzte zusammengesetzte Werte (für Vorschau/Einstellungsseite).</summary>
    public static OverlayValues LatestValues { get; private set; } = new();

    public static FpsReading? LatestFps { get; private set; }

    /// <summary>Text, falls das Tastenkürzel nicht registriert werden konnte (z. B. von einem anderen Programm belegt).</summary>
    public static string? HotkeyError { get; private set; }

    /// <summary>Sekündlich nach dem Aktualisieren der Werte (UI-Thread).</summary>
    public static event Action? Updated;

    /// <summary>Overlay ein-/ausgeblendet, Aufzeichnung gestartet/beendet.</summary>
    public static event Action? StateChanged;

    public static string SessionDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ULTIMATE X", "Sessions");

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Settings = OverlaySettings.Load();
        CreateHotkeyHost();
        RegisterHotkey();
        MonitorHub.SampleArrived += OnSample;
        if (Settings.ShowOnStartup) Show();
    }

    public static void Shutdown()
    {
        if (!_initialized) return;
        _initialized = false;
        MonitorHub.SampleArrived -= OnSample;
        StopRecording();
        UnregisterHotkey();
        _hotkeyHost?.Dispose();
        _hotkeyHost = null;
        _window?.Close();
        _window = null;
        AppServices.Fps.Stop();
    }

    public static void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    public static void Show()
    {
        if (_window is null)
        {
            _window = new OverlayWindow();
            _window.ApplyAppearance(Settings);
        }
        _window.SetLines(OverlayComposer.Compose(Settings.Elements, LatestValues));
        _window.Show();
        _window.Reposition();
        AppLog.Info("Overlay", "Overlay eingeblendet");
        UpdateFpsSession();
        StateChanged?.Invoke();
    }

    public static void Hide()
    {
        _window?.Hide();
        UpdateFpsSession();
        StateChanged?.Invoke();
    }

    /// <summary>Speichert neue Einstellungen, wendet sie an und registriert das Tastenkürzel neu.</summary>
    public static OperationResult Apply(OverlaySettings settings)
    {
        Settings = settings.Normalize();
        Settings.Save();
        _window?.ApplyAppearance(Settings);
        _window?.SetLines(OverlayComposer.Compose(Settings.Elements, LatestValues));
        var ok = RegisterHotkey();
        UpdateFpsSession();
        StateChanged?.Invoke();
        return ok ? OperationResult.Ok("Overlay-Einstellungen gespeichert.")
                  : OperationResult.Fail("Einstellungen gespeichert, aber das Tastenkürzel ist nicht verfügbar: " + HotkeyError);
    }

    /// <summary>Die Einstellungsseite (Vorschau) braucht FPS-Werte, auch wenn das Overlay ausgeblendet ist.</summary>
    public static void RequestPreview(bool active)
    {
        _previewRequests = Math.Max(0, _previewRequests + (active ? 1 : -1));
        UpdateFpsSession();
    }

    // ------------------------------------------------------------------ Messwerte

    private static bool WantsFps(IEnumerable<OverlayElement> e)
        => e.Any(x => x is OverlayElement.Fps or OverlayElement.Frametime or OverlayElement.OnePercentLow);

    private static void UpdateFpsSession()
    {
        var need = (IsVisible && WantsFps(Settings.Elements)) || IsRecording || _previewRequests > 0;
        if (need && !AppServices.Fps.IsRunning) AppServices.Fps.Start();
        else if (!need && AppServices.Fps.IsRunning) AppServices.Fps.Stop();
    }

    private static void OnSample(MonitorSample s)
    {
        _tick = _tick == int.MaxValue ? 1 : _tick + 1;
        try
        {
            var sm = AppServices.Monitor.SensorManager;
            double? Best(UnifiedSensorId id) => sm.Best(id)?.Current;
            var fps = AppServices.Fps.State is FpsMonitorState.Stopped ? null : AppServices.Fps.Read();
            LatestFps = fps;
            var noPermission = fps?.State is FpsMonitorState.NoPermission or FpsMonitorState.Failed;

            LatestValues = new OverlayValues(
                Fps: fps?.Frames.Fps,
                FrametimeMs: fps?.Frames.FrametimeMs,
                OnePercentLowFps: fps?.Frames.OnePercentLowFps,
                CpuLoad: s.CpuPercent ?? Best(UnifiedSensorId.CpuLoad),
                CpuTempC: Best(UnifiedSensorId.CpuPackageTemp) ?? Best(UnifiedSensorId.CpuCoreTemp) ?? s.CpuTempC,
                CpuClockMHz: Best(UnifiedSensorId.CpuClock),
                GpuLoad: s.GpuPercent ?? Best(UnifiedSensorId.GpuLoad),
                GpuTempC: s.GpuTempC ?? Best(UnifiedSensorId.GpuCoreTemp),
                GpuClockMHz: Best(UnifiedSensorId.GpuClock),
                GpuPowerW: Best(UnifiedSensorId.GpuPower),
                RamPercent: s.RamPercent,
                RamUsedBytes: s.RamUsedBytes,
                VramUsedBytes: s.VramUsedBytes,
                PingMs: Settings.Elements.Contains(OverlayElement.Ping) ? _pingMs : null,
                Clock: DateTime.Now,
                FpsUnavailable: noPermission);

            if (_window is not null && _window.IsVisible)
            {
                _window.SetLines(OverlayComposer.Compose(Settings.Elements, LatestValues));
                if (_tick % 5 == 0) _window.ReassertTopmost();
            }
            if ((IsVisible || _previewRequests > 0) && Settings.Elements.Contains(OverlayElement.Ping) && _tick % 3 == 0)
                _ = PingAsync();

            Record(fps);
            Updated?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or ArgumentException)
        {
            AppLog.Warn("Overlay", "Aktualisierung fehlgeschlagen", ex.Message);
        }
    }

    private static async Task PingAsync()
    {
        if (Interlocked.Exchange(ref _pingRunning, 1) == 1) return;
        try
        {
            var target = Settings.PingTarget == OverlayPingTarget.Cloudflare ? IPAddress.Parse("1.1.1.1") : DefaultGateway();
            if (target is null) { _pingMs = null; return; }
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(target, 1000);
            _pingMs = reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch (Exception ex) when (ex is PingException or InvalidOperationException or SocketException)
        {
            _pingMs = null;
        }
        finally
        {
            Interlocked.Exchange(ref _pingRunning, 0);
        }
    }

    private static IPAddress? DefaultGateway()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ Sitzungsaufzeichnung

    public static OperationResult StartRecording()
    {
        if (_recorder is not null) return OperationResult.Ok("Aufzeichnung läuft bereits.");
        try
        {
            Directory.CreateDirectory(SessionDirectory);
            var file = Path.Combine(SessionDirectory, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            _recorder = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new System.Text.UTF8Encoding(true)) { AutoFlush = true };
            _recorder.WriteLine(GameSessionCsv.Header);
            RecordingFile = file;
            AppLog.Info("Overlay", "Sitzungsaufzeichnung gestartet", Path.GetFileName(file));
            UpdateFpsSession();
            StateChanged?.Invoke();
            return OperationResult.Ok("Aufzeichnung gestartet: " + Path.GetFileName(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _recorder = null;
            return OperationResult.Fail("Aufzeichnung konnte nicht gestartet werden: " + ex.Message);
        }
    }

    public static void StopRecording()
    {
        if (_recorder is null) return;
        try { _recorder.Dispose(); }
        catch (IOException) { }
        _recorder = null;
        AppLog.Info("Overlay", "Sitzungsaufzeichnung beendet", RecordingFile is null ? null : Path.GetFileName(RecordingFile));
        UpdateFpsSession();
        StateChanged?.Invoke();
    }

    private static void Record(FpsReading? fps)
    {
        if (_recorder is null) return;
        var v = LatestValues;
        try
        {
            _recorder.WriteLine(GameSessionCsv.FormatRow(new GameSessionSample(DateTime.Now, fps?.ProcessName, v.Fps, v.FrametimeMs, v.OnePercentLowFps,
                v.CpuLoad, v.CpuTempC, v.GpuLoad, v.GpuTempC)));
        }
        catch (IOException ex)
        {
            AppLog.Warn("Overlay", "Aufzeichnung abgebrochen", ex.Message);
            StopRecording();
        }
    }

    // ------------------------------------------------------------------ Globales Tastenkürzel

    private static void CreateHotkeyHost()
    {
        // Message-only-Fenster (HWND_MESSAGE): unsichtbar, nur für WM_HOTKEY.
        var p = new HwndSourceParameters("UltimateX-Overlay-Hotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 0, Height = 0 };
        _hotkeyHost = new HwndSource(p);
        _hotkeyHost.AddHook(WndProc);
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Toggle();
        }
        return IntPtr.Zero;
    }

    private static bool RegisterHotkey()
    {
        if (_hotkeyHost is null) return false;
        UnregisterHotkey();
        if (!HotkeyGesture.TryParse(Settings.Hotkey, out var g))
        {
            HotkeyError = "ungültige Tastenkombination";
            return false;
        }
        if (RegisterHotKey(_hotkeyHost.Handle, HotkeyId, (uint)g.Modifiers | ModNoRepeat, (uint)g.VirtualKey))
        {
            _hotkeyRegistered = true;
            HotkeyError = null;
            return true;
        }
        HotkeyError = $"{g} ist bereits von einem anderen Programm belegt";
        AppLog.Warn("Overlay", "Tastenkürzel nicht verfügbar", g.ToString());
        return false;
    }

    private static void UnregisterHotkey()
    {
        if (!_hotkeyRegistered || _hotkeyHost is null) return;
        UnregisterHotKey(_hotkeyHost.Handle, HotkeyId);
        _hotkeyRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Monitore für die Auswahl (Gerätename, Anzeigetext).</summary>
    public static IReadOnlyList<(string Device, string Label)> Monitors()
        => System.Windows.Forms.Screen.AllScreens
            .Select((s, i) => (s.DeviceName, $"Monitor {i + 1}{(s.Primary ? " (Primär)" : "")} – {s.Bounds.Width}×{s.Bounds.Height}"))
            .ToList();
}
