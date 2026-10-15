using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Settings;
using UltimateX.Services.Gaming;

namespace UltimateX.App.ViewModels;

/// <summary>Ein wählbares Overlay-Element (Checkbox).</summary>
public sealed partial class OverlayElementOption(OverlayElement element, string label, bool isChecked) : ObservableObject
{
    public OverlayElement Element { get; } = element;
    public string Label { get; } = label;
    [ObservableProperty] private bool _isChecked = isChecked;
}

public sealed record SessionFileRow(string Path, string Name, string Info);

public sealed record OverlayAlertRow(string Time, string Title, string Message, bool Critical);

/// <summary>
/// Einstellungsseite „Overlay“ (v1.3): In-Game-Overlay (Vorschau, Elemente, Position, Größe, Transparenz, Tastenkürzel, Profile),
/// FPS-Messung (ETW) mit Rechte-Hinweis, Spiel-Sitzungsaufzeichnung mit Diagramm und Hintergrundüberwachung im Infobereich.
/// </summary>
public sealed partial class OverlayViewModel : PageViewModel
{
    public override string Title => "Overlay";
    public override string Glyph => "";
    public override string Keywords => "overlay fps frametime 1% low ingame spiel gaming hud anzeige hotkey tastenkürzel infobereich tray überwachung warnung temperatur benachrichtigung aufzeichnung sitzung";
    public override string? Subtitle => "In-Game-Overlay, FPS-Messung und Überwachung im Hintergrund";

    private bool _previewActive;
    private bool _loading;

    // ---------------- Overlay
    public ObservableCollection<OverlayElementOption> Elements { get; } = [];
    public ObservableCollection<OverlayLine> PreviewLines { get; } = [];
    public ObservableCollection<string> Profiles { get; } = [];
    public IReadOnlyList<Choice<OverlayCorner>> Corners { get; } =
    [
        new(OverlayCorner.TopLeft, "Oben links"), new(OverlayCorner.TopRight, "Oben rechts"),
        new(OverlayCorner.BottomLeft, "Unten links"), new(OverlayCorner.BottomRight, "Unten rechts"),
    ];
    public IReadOnlyList<Choice<OverlayPingTarget>> PingTargets { get; } =
    [
        new(OverlayPingTarget.Gateway, "Router (Standardgateway, lokal)"), new(OverlayPingTarget.Cloudflare, "Internet (1.1.1.1, Cloudflare)"),
    ];
    public ObservableCollection<Choice<string?>> Monitors { get; } = [];

    [ObservableProperty] private bool _overlayVisible;
    [ObservableProperty] private bool _showOnStartup;
    [ObservableProperty] private Choice<OverlayCorner>? _selectedCorner;
    [ObservableProperty] private Choice<string?>? _selectedMonitor;
    [ObservableProperty] private Choice<OverlayPingTarget>? _selectedPingTarget;
    [ObservableProperty] private double _fontSize = 14;
    [ObservableProperty] private double _opacityPercent = 100;
    [ObservableProperty] private double _backgroundPercent = 55;
    [ObservableProperty] private string _hotkey = HotkeyGesture.Default.ToString();
    [ObservableProperty] private string? _hotkeyError;
    [ObservableProperty] private string? _selectedProfile;
    [ObservableProperty] private string _newProfileName = "";

    // ---------------- FPS
    [ObservableProperty] private string _fpsStatus = "FPS-Messung inaktiv";
    [ObservableProperty] private bool _fpsNoPermission;
    [ObservableProperty] private string? _foregroundProcess;

    // ---------------- Aufzeichnung
    public ObservableCollection<SessionFileRow> Sessions { get; } = [];
    [ObservableProperty] private bool _isRecording;
    [ObservableProperty] private string? _recordingFile;
    [ObservableProperty] private SessionFileRow? _selectedSession;
    [ObservableProperty] private IReadOnlyList<double>? _sessionFps;
    [ObservableProperty] private IReadOnlyList<double>? _sessionCpuTemp;
    [ObservableProperty] private IReadOnlyList<double>? _sessionGpuTemp;
    [ObservableProperty] private string? _sessionSummary;

    // ---------------- Hintergrundüberwachung
    public ObservableCollection<OverlayAlertRow> Alerts { get; } = [];
    [ObservableProperty] private bool _trayEnabled;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _cpuTempEnabled;
    [ObservableProperty] private int _cpuTempLimit;
    [ObservableProperty] private bool _gpuTempEnabled;
    [ObservableProperty] private int _gpuTempLimit;
    [ObservableProperty] private int _sustainSeconds;
    [ObservableProperty] private bool _diskEnabled;
    [ObservableProperty] private int _diskWarnPercent;
    [ObservableProperty] private int _diskCriticalPercent;
    [ObservableProperty] private bool _smartEnabled;
    [ObservableProperty] private bool _blueScreenEnabled;
    [ObservableProperty] private bool _defenderEnabled;
    [ObservableProperty] private bool _backupEnabled;
    [ObservableProperty] private int _checkInterval;
    [ObservableProperty] private int _repeatLock;
    [ObservableProperty] private bool _monitorPaused;
    [ObservableProperty] private string? _lastCheckText;

    public bool BackupRuleAvailable => TrayMonitor.BackupOverdueProvider is not null;

    public string SessionDirectory => OverlayController.SessionDirectory;

    protected override Task LoadAsync()
    {
        _loading = true;
        try
        {
            LoadOverlaySettings(OverlayController.Settings);
            LoadMonitorSettings(TrayMonitor.Settings);
            RefreshSessions();
            RefreshAlerts();
            SyncState();
        }
        finally
        {
            _loading = false;
        }
        UpdatePreview();
        return Task.CompletedTask;
    }

    public override void OnNavigatedTo()
    {
        if (!_previewActive)
        {
            _previewActive = true;
            OverlayController.RequestPreview(true);
            OverlayController.Updated += OnUpdated;
            OverlayController.StateChanged += SyncState;
            TrayMonitor.AlertRaised += OnAlert;
            TrayMonitor.StateChanged += SyncState;
        }
    }

    public override void OnNavigatedFrom()
    {
        if (!_previewActive) return;
        _previewActive = false;
        OverlayController.RequestPreview(false);
        OverlayController.Updated -= OnUpdated;
        OverlayController.StateChanged -= SyncState;
        TrayMonitor.AlertRaised -= OnAlert;
        TrayMonitor.StateChanged -= SyncState;
    }

    private void LoadOverlaySettings(OverlaySettings s)
    {
        foreach (var e in Elements) e.PropertyChanged -= OnElementChanged;
        Elements.Clear();
        // Gewählte Elemente zuerst in ihrer Reihenfolge, danach die übrigen.
        foreach (var e in s.Elements.Concat(Enum.GetValues<OverlayElement>().Except(s.Elements)))
        {
            var opt = new OverlayElementOption(e, OverlayComposer.LabelOf(e) + ElementHint(e), s.Elements.Contains(e));
            opt.PropertyChanged += OnElementChanged;
            Elements.Add(opt);
        }
        Monitors.Clear();
        Monitors.Add(new Choice<string?>(null, "Primärmonitor"));
        foreach (var (device, label) in OverlayController.Monitors()) Monitors.Add(new Choice<string?>(device, label));
        SelectedMonitor = Monitors.FirstOrDefault(m => string.Equals(m.Value, s.MonitorDevice, StringComparison.OrdinalIgnoreCase)) ?? Monitors[0];
        SelectedCorner = Corners.First(c => c.Value == s.Corner);
        SelectedPingTarget = PingTargets.First(p => p.Value == s.PingTarget);
        FontSize = s.FontSize;
        OpacityPercent = Math.Round(s.Opacity * 100);
        BackgroundPercent = Math.Round(s.BackgroundOpacity * 100);
        Hotkey = s.Hotkey;
        ShowOnStartup = s.ShowOnStartup;
        Profiles.Clear();
        foreach (var p in s.Profiles) Profiles.Add(p.Name);
        SelectedProfile = s.ActiveProfile;
    }

    private static string ElementHint(OverlayElement e) => e switch
    {
        OverlayElement.CpuClock or OverlayElement.GpuClock or OverlayElement.GpuPower => " (Sensorquelle nötig)",
        OverlayElement.Ping => " (Ping alle 3 s)",
        _ => "",
    };

    private void OnElementChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => UpdatePreview();

    partial void OnFontSizeChanged(double value) => UpdatePreview();

    private OverlaySettings BuildOverlaySettings()
    {
        var current = OverlayController.Settings;
        return new OverlaySettings
        {
            ShowOnStartup = ShowOnStartup,
            Hotkey = Hotkey,
            MonitorDevice = SelectedMonitor?.Value,
            Corner = SelectedCorner?.Value ?? OverlayCorner.TopLeft,
            FontSize = FontSize,
            Opacity = OpacityPercent / 100.0,
            BackgroundOpacity = BackgroundPercent / 100.0,
            Elements = Elements.Where(e => e.IsChecked).Select(e => e.Element).ToList(),
            PingTarget = SelectedPingTarget?.Value ?? OverlayPingTarget.Gateway,
            Profiles = current.Profiles.Select(p => new OverlayProfile
            {
                Name = p.Name, Elements = [.. p.Elements], Corner = p.Corner, FontSize = p.FontSize, Opacity = p.Opacity, BackgroundOpacity = p.BackgroundOpacity,
            }).ToList(),
            ActiveProfile = SelectedProfile,
        };
    }

    // ------------------------------------------------------------------ Befehle Overlay

    [RelayCommand]
    private void ToggleOverlay()
    {
        OverlayController.Toggle();
        SyncState();
    }

    [RelayCommand]
    private void ApplyOverlay()
    {
        if (!HotkeyGesture.TryParse(Hotkey, out var g))
        {
            HotkeyError = "Ungültig. Beispiel: Strg+Umschalt+O (mindestens Strg, Alt oder Win plus eine Taste A–Z, 0–9, F1–F24).";
            return;
        }
        HotkeyError = null;
        Hotkey = g.ToString();
        var r = OverlayController.Apply(BuildOverlaySettings());
        if (!r.Success) HotkeyError = OverlayController.HotkeyError;
        ShowResult(r);
        UpdatePreview();
    }

    [RelayCommand]
    private void SaveProfile()
    {
        var name = NewProfileName.Trim();
        if (name.Length == 0)
        {
            StatusMessage = "Bitte einen Profilnamen eingeben.";
            return;
        }
        var s = BuildOverlaySettings();
        s.SaveProfile(name);
        OverlayController.Apply(s);
        _loading = true;
        try { LoadOverlaySettings(OverlayController.Settings); }
        finally { _loading = false; }
        NewProfileName = "";
        StatusMessage = $"Profil „{name}“ gespeichert.";
    }

    [RelayCommand]
    private void LoadProfile()
    {
        if (SelectedProfile is not { } name) return;
        var s = BuildOverlaySettings();
        if (!s.ApplyProfile(name)) return;
        OverlayController.Apply(s);
        _loading = true;
        try { LoadOverlaySettings(OverlayController.Settings); }
        finally { _loading = false; }
        UpdatePreview();
        StatusMessage = $"Profil „{name}“ geladen.";
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile is not { } name) return;
        if (!Dialogs.Confirm("Profil löschen", $"Overlay-Profil „{name}“ löschen?")) return;
        var s = BuildOverlaySettings();
        s.Profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        s.ActiveProfile = null;
        OverlayController.Apply(s);
        Profiles.Remove(name);
        SelectedProfile = null;
    }

    [RelayCommand]
    private void ShowFpsHelp()
    {
        Dialogs.Info("FPS-Messung freischalten",
            "ULTIMATE X misst FPS ohne Eingriff in Spiele über die Windows-Ereignisablaufverfolgung (ETW). Windows erlaubt das nur " +
            "Administratoren oder Mitgliedern der Gruppe „Leistungsprotokollbenutzer“. ULTIMATE X ändert Ihr Konto nicht selbst.\n\n" +
            "So fügen Sie Ihr Konto der Gruppe hinzu (einmalig, Administrator nötig):\n" +
            "1. Windows-Taste + R drücken, „lusrmgr.msc“ eingeben (nur Windows Pro/Enterprise) → Gruppen → „Leistungsprotokollbenutzer“ → Hinzufügen → Ihren Benutzernamen eintragen.\n" +
            "   Alternativ in einer Eingabeaufforderung als Administrator:\n" +
            "   net localgroup \"Leistungsprotokollbenutzer\" IhrBenutzername /add\n" +
            "   (auf englischem Windows heißt die Gruppe „Performance Log Users“).\n" +
            "2. Abmelden und wieder anmelden – erst dann gilt die neue Gruppenmitgliedschaft.\n" +
            "3. ULTIMATE X neu starten.\n\n" +
            "Hinweis: Mitglieder dieser Gruppe dürfen Leistungsprotokolle und Ereignisablaufverfolgungen starten. " +
            "Rückgängig machen: dieselben Schritte mit „Entfernen“ bzw. /delete.");
    }

    // ------------------------------------------------------------------ Aufzeichnung

    [RelayCommand]
    private void ToggleRecording()
    {
        if (OverlayController.IsRecording)
        {
            OverlayController.StopRecording();
            StatusMessage = "Aufzeichnung beendet.";
            RefreshSessions();
            SelectedSession = Sessions.FirstOrDefault();
        }
        else
        {
            ShowResult(OverlayController.StartRecording());
        }
        SyncState();
    }

    [RelayCommand]
    private void RefreshSessions()
    {
        Sessions.Clear();
        try
        {
            var dir = OverlayController.SessionDirectory;
            if (!Directory.Exists(dir)) return;
            foreach (var f in new DirectoryInfo(dir).GetFiles("session-*.csv").OrderByDescending(f => f.LastWriteTime).Take(100))
                Sessions.Add(new SessionFileRow(f.FullName, f.Name, $"{f.LastWriteTime:dd.MM.yyyy HH:mm} · {f.Length / 1024.0:0} KB"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Sitzungsordner nicht lesbar: " + ex.Message;
        }
    }

    partial void OnSelectedSessionChanged(SessionFileRow? value) => _ = LoadSessionAsync(value);

    private async Task LoadSessionAsync(SessionFileRow? row)
    {
        SessionFps = SessionCpuTemp = SessionGpuTemp = null;
        SessionSummary = null;
        if (row is null) return;
        try
        {
            var samples = await Task.Run(() =>
            {
                using var fs = new FileStream(row.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                var lines = new List<string>();
                while (reader.ReadLine() is { } line) lines.Add(line);
                return GameSessionCsv.Parse(lines);
            });
            if (samples.Count == 0)
            {
                SessionSummary = "Keine Messwerte in dieser Aufzeichnung.";
                return;
            }
            var step = Math.Max(1, samples.Count / 600); // höchstens ~600 Punkte zeichnen
            var thinned = samples.Where((_, i) => i % step == 0).ToList();
            SessionFps = thinned.Select(s => s.Fps ?? 0).ToArray();
            SessionCpuTemp = thinned.Any(s => s.CpuTempC is not null) ? thinned.Select(s => s.CpuTempC ?? 0).ToArray() : null;
            SessionGpuTemp = thinned.Any(s => s.GpuTempC is not null) ? thinned.Select(s => s.GpuTempC ?? 0).ToArray() : null;
            var sum = GameSessionCsv.Summarize(samples);
            static string F(double? v, string unit) => v is { } x ? $"{x:0} {unit}" : "N/A";
            SessionSummary = $"Dauer {sum.Duration:hh\\:mm\\:ss} · Ø {F(sum.AverageFps, "FPS")} · Min {F(sum.MinFps, "FPS")} · Ø 1 % Low {F(sum.AverageOnePercentLow, "FPS")} · " +
                             $"max. CPU {F(sum.MaxCpuTempC, "°C")} · max. GPU {F(sum.MaxGpuTempC, "°C")}" +
                             (sum.MainProcess is null ? "" : $" · Spiel: {sum.MainProcess}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SessionSummary = "Aufzeichnung nicht lesbar: " + ex.Message;
        }
    }

    // ------------------------------------------------------------------ Hintergrundüberwachung

    private void LoadMonitorSettings(BackgroundMonitorSettings s)
    {
        TrayEnabled = s.Enabled;
        MinimizeToTray = s.MinimizeToTrayOnClose;
        var r = s.Rules;
        CpuTempEnabled = r.CpuTempEnabled;
        CpuTempLimit = r.CpuTempLimitC;
        GpuTempEnabled = r.GpuTempEnabled;
        GpuTempLimit = r.GpuTempLimitC;
        SustainSeconds = r.TempSustainSeconds;
        DiskEnabled = r.DiskEnabled;
        DiskWarnPercent = r.DiskWarnPercent;
        DiskCriticalPercent = r.DiskCriticalPercent;
        SmartEnabled = r.SmartEnabled;
        BlueScreenEnabled = r.BlueScreenEnabled;
        DefenderEnabled = r.DefenderEnabled;
        BackupEnabled = r.BackupEnabled;
        CheckInterval = r.CheckIntervalMinutes;
        RepeatLock = r.RepeatLockMinutes;
    }

    [RelayCommand]
    private void ApplyMonitor()
    {
        var s = new BackgroundMonitorSettings
        {
            Enabled = TrayEnabled,
            MinimizeToTrayOnClose = MinimizeToTray,
            LastBlueScreenCheckUtc = TrayMonitor.Settings.LastBlueScreenCheckUtc,
            Rules = new AlertSettings
            {
                CpuTempEnabled = CpuTempEnabled, CpuTempLimitC = CpuTempLimit,
                GpuTempEnabled = GpuTempEnabled, GpuTempLimitC = GpuTempLimit,
                TempSustainSeconds = SustainSeconds,
                DiskEnabled = DiskEnabled, DiskWarnPercent = DiskWarnPercent, DiskCriticalPercent = DiskCriticalPercent,
                SmartEnabled = SmartEnabled, BlueScreenEnabled = BlueScreenEnabled, DefenderEnabled = DefenderEnabled, BackupEnabled = BackupEnabled,
                CheckIntervalMinutes = CheckInterval, RepeatLockMinutes = RepeatLock,
            },
        };
        TrayMonitor.Apply(s);
        LoadMonitorSettings(TrayMonitor.Settings); // begrenzte Werte anzeigen
        StatusMessage = "Einstellungen der Hintergrundüberwachung gespeichert.";
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsBusy = true;
        try { await TrayMonitor.RunChecksAsync(); }
        finally { IsBusy = false; }
        RefreshAlerts();
        StatusMessage = TrayMonitor.IsPaused ? "Überwachung ist pausiert – keine Prüfung." : "Prüfung abgeschlossen.";
    }

    partial void OnMonitorPausedChanged(bool value)
    {
        if (!_loading && value != TrayMonitor.IsPaused) TrayMonitor.SetPaused(value);
    }

    private void OnAlert(AlertEvent a) => RefreshAlerts();

    private void RefreshAlerts()
    {
        Alerts.Clear();
        foreach (var a in TrayMonitor.RecentAlerts.Take(20))
            Alerts.Add(new OverlayAlertRow(a.Time.ToString("dd.MM. HH:mm"), a.Title, a.Message, a.Severity == AlertSeverity.Critical));
    }

    // ------------------------------------------------------------------ Laufende Aktualisierung

    private void SyncState()
    {
        OverlayVisible = OverlayController.IsVisible;
        IsRecording = OverlayController.IsRecording;
        RecordingFile = OverlayController.IsRecording ? OverlayController.RecordingFile : null;
        var wasLoading = _loading;
        _loading = true;
        MonitorPaused = TrayMonitor.IsPaused;
        _loading = wasLoading;
        LastCheckText = TrayMonitor.LastCheck is { } t ? $"Letzte Prüfung: {t:dd.MM.yyyy HH:mm}" : "Noch keine Prüfung in dieser Sitzung";
    }

    private void OnUpdated()
    {
        UpdatePreview();
        var fps = OverlayController.LatestFps;
        FpsNoPermission = fps?.State == FpsMonitorState.NoPermission;
        ForegroundProcess = fps?.ProcessName;
        var hasFrames = fps is not null && fps.Frames.HasData;
        var process = fps?.ProcessName ?? "Vordergrundprogramm";
        FpsStatus = fps?.State switch
        {
            FpsMonitorState.Running when hasFrames => $"FPS-Messung aktiv · {process}",
            FpsMonitorState.Running => "FPS-Messung aktiv · das Vordergrundprogramm gibt keine Direct3D-Frames aus (Desktop, OpenGL oder pausiert)",
            FpsMonitorState.NoPermission => "FPS: N/A – keine Berechtigung für die Ereignisablaufverfolgung (ETW)",
            FpsMonitorState.Failed => "FPS: N/A – Messung konnte nicht gestartet werden: " + (AppServices.Fps.LastError ?? "unbekannter Fehler"),
            _ => "FPS-Messung inaktiv",
        };
    }

    private void UpdatePreview()
    {
        if (_loading) return;
        var elements = Elements.Where(e => e.IsChecked).Select(e => e.Element).ToList();
        var lines = OverlayComposer.Compose(elements, OverlayController.LatestValues);
        PreviewLines.Clear();
        foreach (var l in lines) PreviewLines.Add(l);
    }
}
