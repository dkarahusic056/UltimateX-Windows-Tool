using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using UltimateX.Core.Analysis;
using UltimateX.Core.Localization;
using UltimateX.Core.Logging;
using UltimateX.Core.Settings;
using UltimateX.Services.Monitoring;
using UltimateX.Services.Sensors;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Überwachung im Hintergrund (v1.3): Symbol im Infobereich (System.Windows.Forms.NotifyIcon) mit Menü
/// (Öffnen, Overlay ein/aus, Überwachung pausieren, Beenden) und regelbasierte Warnungen (<see cref="AlertEngine"/>):
/// CPU-/GPU-Temperatur, Laufwerksfüllstand, SMART, neue Bluescreens, Virenschutz aus, überfällige Backups.
/// Ausschließlich lesend, keine Netzwerkzugriffe, nichts wird automatisch geändert.
///
/// Lebenszyklus: <see cref="Start"/> in App.OnStartup nach window.Show(); <see cref="Stop"/> in App.OnExit.
/// Das Schließen-Verhalten („in den Infobereich minimieren“) hängt sich selbst an Window.Closing des Hauptfensters.
/// </summary>
public static class TrayMonitor
{
    private static System.Windows.Forms.NotifyIcon? _icon;
    private static System.Windows.Forms.ToolStripMenuItem? _overlayItem, _pauseItem;
    private static Window? _main;
    private static AlertEngine _engine = new(new AlertSettings());
    private static readonly DispatcherTimer CheckTimer = new(DispatcherPriority.Background);
    private static readonly Dictionary<NotificationKind, DateTime> KindUsed = [];
    private static readonly List<AlertEvent> Recent = [];
    private static bool _exiting, _checking, _started, _trayHintShown;

    /// <summary>
    /// Liefert überfällige Backups als Meldungstexte (z. B. „Dokumente: letzte Sicherung vor 9 Tagen“). Wird vom Hauptagenten
    /// mit dem Backup-Dienst verdrahtet; null = keine Backup-Regel. Aufruf auf dem UI-Thread, muss schnell sein.
    /// </summary>
    public static Func<IReadOnlyList<string>>? BackupOverdueProvider { get; set; }

    public static BackgroundMonitorSettings Settings { get; private set; } = new();

    public static bool IsPaused { get; private set; }

    public static DateTime? LastCheck { get; private set; }

    /// <summary>Zuletzt ausgelöste Meldungen (neueste zuerst, max. 50).</summary>
    public static IReadOnlyList<AlertEvent> RecentAlerts => Recent.ToList();

    public static event Action<AlertEvent>? AlertRaised;
    public static event Action? StateChanged;

    public static void Start(Window mainWindow)
    {
        if (_started) return;
        _started = true;
        _main = mainWindow;
        Settings = BackgroundMonitorSettings.Load();
        _engine = new AlertEngine(Settings.Rules);
        Settings.LastBlueScreenCheckUtc ??= DateTime.UtcNow; // erster Start: alte Bluescreens nicht melden
        Settings.Save();

        mainWindow.Closing += OnMainClosing;
        MonitorHub.SampleArrived += OnSample;
        CheckTimer.Tick += async (_, _) => await RunChecksAsync();
        ApplyRuntime(firstStart: true);
    }

    public static void Stop()
    {
        if (!_started) return;
        _started = false;
        _exiting = true;
        CheckTimer.Stop();
        MonitorHub.SampleArrived -= OnSample;
        if (_main is not null) _main.Closing -= OnMainClosing;
        RemoveIcon();
    }

    /// <summary>Neue Einstellungen speichern und sofort anwenden.</summary>
    public static void Apply(BackgroundMonitorSettings settings)
    {
        Settings = settings.Normalize();
        Settings.Save();
        _engine.Settings = Settings.Rules;
        if (_started) ApplyRuntime(firstStart: false);
        StateChanged?.Invoke();
    }

    public static void SetPaused(bool paused)
    {
        IsPaused = paused;
        if (_pauseItem is not null) _pauseItem.Checked = paused;
        UpdateTooltip();
        AppLog.Info("Überwachung", paused ? "Hintergrundüberwachung pausiert" : "Hintergrundüberwachung fortgesetzt");
        StateChanged?.Invoke();
    }

    private static void ApplyRuntime(bool firstStart)
    {
        if (Settings.Enabled)
        {
            EnsureIcon();
            CheckTimer.Interval = firstStart ? TimeSpan.FromMinutes(2) : TimeSpan.FromMinutes(Settings.Rules.CheckIntervalMinutes);
            CheckTimer.Start();
        }
        else
        {
            CheckTimer.Stop();
            RemoveIcon();
        }
        UpdateTooltip();
    }

    // ------------------------------------------------------------------ Infobereich

    private static void EnsureIcon()
    {
        if (_icon is not null) return;
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add(Loc.T("Öffnen"), null, (_, _) => OpenMain());
        _overlayItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Overlay ein/aus"), null, (_, _) => OverlayController.Toggle());
        menu.Items.Add(_overlayItem);
        _pauseItem = new System.Windows.Forms.ToolStripMenuItem(Loc.T("Überwachung pausieren"), null, (_, _) => SetPaused(!IsPaused)) { Checked = IsPaused };
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(Loc.T("Beenden"), null, (_, _) => ExitApp());
        menu.Opening += (_, _) => { if (_overlayItem is not null) _overlayItem.Checked = OverlayController.IsVisible; };

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => OpenMain();
        UpdateTooltip();
    }

    private static void RemoveIcon()
    {
        if (_icon is null) return;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Icon?.Dispose();
        _icon.Dispose();
        _icon = null;
        _overlayItem = null;
        _pauseItem = null;
    }

    /// <summary>App-Symbol aus der WPF-Ressource (Assets/ultimatex.ico); Rückfall: Symbol der EXE.</summary>
    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            var res = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/ultimatex.ico"));
            if (res?.Stream is { } stream)
            {
                using (stream)
                    return new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            AppLog.Warn("Überwachung", "App-Symbol nicht geladen", ex.Message);
        }
        return (Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : null) ?? System.Drawing.SystemIcons.Application;
    }

    private static void UpdateTooltip()
    {
        if (_icon is null) return;
        var text = "ULTIMATE X – " + Loc.T(IsPaused ? "Überwachung pausiert" : "Überwachung aktiv");
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static void OpenMain()
    {
        if (_main is null) return;
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    private static void ExitApp()
    {
        _exiting = true;
        Application.Current?.Shutdown();
    }

    private static void OnMainClosing(object? sender, CancelEventArgs e)
    {
        if (_exiting || !Settings.Enabled || !Settings.MinimizeToTrayOnClose || _icon is null) return;
        e.Cancel = true;
        _main?.Hide();
        if (!_trayHintShown)
        {
            _trayHintShown = true;
            _icon.ShowBalloonTip(5000, "ULTIMATE X", Loc.T("ULTIMATE X läuft im Infobereich weiter. Beenden über das Symbol-Menü."), System.Windows.Forms.ToolTipIcon.Info);
        }
    }

    // ------------------------------------------------------------------ Regeln

    private static void OnSample(MonitorSample s)
    {
        if (!Settings.Enabled || IsPaused) return;
        var sm = AppServices.Monitor.SensorManager;
        double? cpu = sm.Best(UnifiedSensorId.CpuPackageTemp)?.Current ?? sm.Best(UnifiedSensorId.CpuCoreTemp)?.Current ?? s.CpuTempC;
        double? gpu = s.GpuTempC ?? sm.Best(UnifiedSensorId.GpuCoreTemp)?.Current;
        foreach (var a in _engine.ObserveTemperatures(DateTime.Now, cpu, gpu)) Raise(a);
    }

    /// <summary>Führt die langsamen Prüfungen sofort aus (auch über die Einstellungsseite).</summary>
    public static async Task RunChecksAsync()
    {
        if (_checking || !Settings.Enabled) return;
        if (CheckTimer.IsEnabled) CheckTimer.Interval = TimeSpan.FromMinutes(Settings.Rules.CheckIntervalMinutes);
        if (IsPaused) return;
        _checking = true;
        try
        {
            var rules = Settings.Rules;
            var since = Settings.LastBlueScreenCheckUtc ?? DateTime.UtcNow;
            var started = DateTime.UtcNow;

            IReadOnlyList<string>? backups = null;
            if (rules.BackupEnabled && BackupOverdueProvider is { } provider)
            {
                try { backups = provider(); }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    AppLog.Warn("Überwachung", "Backup-Status nicht verfügbar", ex.Message);
                }
            }

            var checks = AppServices.BackgroundChecks;
            var input = await Task.Run(() => new SystemCheckInput(
                Drives: rules.DiskEnabled ? checks.GetDriveFill() : null,
                SmartWarnings: rules.SmartEnabled ? checks.GetSmartWarnings() : null,
                NewBlueScreens: rules.BlueScreenEnabled ? checks.GetBlueScreensSince(since) : null,
                DefenderOff: rules.DefenderEnabled ? checks.IsAntivirusOff() : null,
                BackupOverdue: backups));

            if (input.NewBlueScreens is not null)
            {
                Settings.LastBlueScreenCheckUtc = started;
                Settings.Save();
            }
            LastCheck = DateTime.Now;
            foreach (var a in _engine.EvaluateChecks(DateTime.Now, input)) Raise(a);
            StateChanged?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Überwachung", "Hintergrundprüfung fehlgeschlagen", ex.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    private static void Raise(AlertEvent a)
    {
        AppLog.Warn("Überwachung", a.Title, a.Message);
        Recent.Insert(0, a);
        if (Recent.Count > 50) Recent.RemoveRange(50, Recent.Count - 50);
        AlertRaised?.Invoke(a);

        var notifications = AppServices.Notifications;
        if (!notifications.Enabled) return; // globale Einstellung „Benachrichtigungen“ gilt auch hier
        var kind = a.Rule switch
        {
            AlertRuleId.CpuTemperature or AlertRuleId.GpuTemperature => NotificationKind.TemperatureWarning,
            AlertRuleId.DiskFull => NotificationKind.LowDiskSpace,
            _ => NotificationKind.SecurityAlert,
        };
        // Der NotificationService zeigt je Art höchstens eine Meldung pro Stunde. Damit eine andere Regel derselben Art
        // nicht verschluckt wird, weichen wir in diesem Fall auf die Sprechblase des Infobereich-Symbols aus.
        var now = DateTime.UtcNow;
        if (KindUsed.TryGetValue(kind, out var last) && now - last < TimeSpan.FromHours(1))
        {
            _icon?.ShowBalloonTip(10000, Loc.T(a.Title), Loc.T(a.Message),
                a.Severity == AlertSeverity.Critical ? System.Windows.Forms.ToolTipIcon.Error : System.Windows.Forms.ToolTipIcon.Warning);
            return;
        }
        KindUsed[kind] = now;
        notifications.Notify(kind, a.Title, a.Message);
    }
}
