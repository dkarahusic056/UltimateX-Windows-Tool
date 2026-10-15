using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Logging;
using UltimateX.Core.Settings;

namespace UltimateX.App.ViewModels;

/// <summary>Einstellungen (§11, §59, §60, §84). Änderungen werden sofort lokal gespeichert.</summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "UltimateX";
    private readonly MainViewModel _main;
    private readonly AppSettings _s = AppServices.Settings;

    public SettingsViewModel(MainViewModel main) => _main = main;

    public override string Title => "Einstellungen";
    public override string Glyph => "\uE713";
    public override string Keywords => "einstellungen settings theme dark light layout sprache security session sensitive modus autostart";

    protected override Task LoadAsync() => Task.CompletedTask;

    [RelayCommand]
    private void OpenManual() => ShowResult(UriLauncher.OpenManual());

    private void Save([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        AppServices.SaveSettings();
        OnPropertyChanged(name);
    }

    // ---- Appearance ----
    public bool ThemeSystem { get => _s.Theme == ThemeMode.System; set { if (value) SetTheme(ThemeMode.System); } }
    public bool ThemeDark { get => _s.Theme == ThemeMode.Dark; set { if (value) SetTheme(ThemeMode.Dark); } }
    public bool ThemeLight { get => _s.Theme == ThemeMode.Light; set { if (value) SetTheme(ThemeMode.Light); } }

    // Sprache (§84): wirkt nach Neustart der App, da alle Texte beim Laden übersetzt werden.
    public bool LanguageAuto { get => _s.Language == AppLanguage.Automatisch; set { if (value) SetLanguage(AppLanguage.Automatisch); } }
    public bool LanguageGerman { get => _s.Language == AppLanguage.Deutsch; set { if (value) SetLanguage(AppLanguage.Deutsch); } }
    public bool LanguageEnglish { get => _s.Language == AppLanguage.English; set { if (value) SetLanguage(AppLanguage.English); } }

    [ObservableProperty] private bool _languageRestartNeeded;

    private void SetLanguage(AppLanguage language)
    {
        if (_s.Language == language) return;
        _s.Language = language;
        AppServices.SaveSettings();
        OnPropertyChanged(nameof(LanguageAuto));
        OnPropertyChanged(nameof(LanguageGerman));
        OnPropertyChanged(nameof(LanguageEnglish));
        LanguageRestartNeeded = true;
    }

    [RelayCommand]
    private void RestartApp()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false });
        System.Windows.Application.Current.Shutdown();
    }

    private void SetTheme(ThemeMode mode)
    {
        _s.Theme = mode;
        ThemeManager.Apply(mode);
        AppServices.SaveSettings();
        OnPropertyChanged(nameof(ThemeSystem));
        OnPropertyChanged(nameof(ThemeDark));
        OnPropertyChanged(nameof(ThemeLight));
        // Seiten neu zeichnen, damit Statusfarben das neue Schema übernehmen.
        _ = _main.CurrentPage?.RefreshAsync();
    }

    public bool LayoutModern { get => _s.Layout == LayoutMode.ModernControlCenter; set { if (value) SetLayout(LayoutMode.ModernControlCenter); } }
    public bool LayoutSimple { get => _s.Layout == LayoutMode.SimpleMode; set { if (value) SetLayout(LayoutMode.SimpleMode); } }
    public bool LayoutGaming { get => _s.Layout == LayoutMode.GamingMode; set { if (value) SetLayout(LayoutMode.GamingMode); } }
    public bool LayoutTechnician { get => _s.Layout == LayoutMode.TechnicianMode; set { if (value) SetLayout(LayoutMode.TechnicianMode); } }
    public bool LayoutMap { get => _s.Layout == LayoutMode.VisualPcMap; set { if (value) SetLayout(LayoutMode.VisualPcMap); } }
    public bool LayoutAdmin { get => _s.Layout == LayoutMode.AdminServiceMode; set { if (value) SetLayout(LayoutMode.AdminServiceMode); } }

    private void SetLayout(LayoutMode mode)
    {
        _s.Layout = mode;
        AppServices.SaveSettings();
        foreach (var n in new[] { nameof(LayoutModern), nameof(LayoutSimple), nameof(LayoutGaming), nameof(LayoutTechnician), nameof(LayoutMap), nameof(LayoutAdmin) })
            OnPropertyChanged(n);
        // Im Modern-Layout auf der Einstellungsseite bleiben; andere Layouts öffnen ihre Startseite.
        _main.ApplyLayout(mode, mode == LayoutMode.ModernControlCenter ? this : null);
    }

    // ---- Benachrichtigungen, Wiederherstellung, Updates ----
    public bool NotificationsEnabled
    {
        get => _s.NotificationsEnabled;
        set { _s.NotificationsEnabled = value; AppServices.Notifications.Enabled = value; Save(); }
    }

    public bool AutoRestorePoint { get => _s.AutoRestorePoint; set { _s.AutoRestorePoint = value; Save(); } }

    public bool AutoCheckAppUpdates { get => _s.AutoCheckAppUpdates; set { _s.AutoCheckAppUpdates = value; Save(); } }

    [ObservableProperty] private string _updateUrlInput = AppServices.Settings.UpdateManifestUrl;

    [RelayCommand]
    private async Task SaveUpdateUrlAsync()
    {
        var v = UpdateUrlInput.Trim();
        if (v.Length > 0 && (!Uri.TryCreate(v, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || v.Length > 500))
        {
            StatusMessage = "Nur HTTPS-Adressen sind als Update-Quelle erlaubt.";
            return;
        }
        // Die Update-Quelle ist eine Sicherheitseinstellung (§57) → Authentifizierung.
        if (!await AppServices.Guard.AuthenticateOnlyAsync("Update-Quelle ändern")) return;
        _s.UpdateManifestUrl = v;
        AppServices.SaveSettings();
        AppLog.Security("SecuritySettingChange", v.Length == 0 ? "Update-Quelle entfernt" : "Update-Quelle geändert", details: v);
        StatusMessage = v.Length == 0 ? "Update-Prüfung deaktiviert." : "Update-Quelle gespeichert.";
    }

    // ---- Security ----
    public bool SensitiveMode
    {
        get => _s.SensitiveMode;
        set
        {
            if (!value && !_s.SensitiveMode) return;
            if (!value)
            {
                // Abschalten der Maskierung = Security Feature deaktivieren → Authentifizierung (§57).
                _ = DisableSensitiveModeAsync();
                return;
            }
            _s.SensitiveMode = true;
            Save();
        }
    }

    private async Task DisableSensitiveModeAsync()
    {
        if (await AppServices.Guard.AuthenticateOnlyAsync("Maskierung sensibler Daten deaktivieren"))
        {
            _s.SensitiveMode = false;
            AppLog.Security("SecuritySettingChange", "Sensitive Mode deaktiviert");
            AppServices.SaveSettings();
        }
        OnPropertyChanged(nameof(SensitiveMode));
    }

    public bool Session0 { get => _s.SecuritySessionMinutes == 0; set { if (value) SetSession(0); } }
    public bool Session1 { get => _s.SecuritySessionMinutes == 1; set { if (value) SetSession(1); } }
    public bool Session2 { get => _s.SecuritySessionMinutes == 2; set { if (value) SetSession(2); } }
    public bool Session5 { get => _s.SecuritySessionMinutes == 5; set { if (value) SetSession(5); } }

    private void SetSession(int minutes)
    {
        _s.SecuritySessionMinutes = minutes;
        AppServices.Auth.SessionMinutes = minutes;
        AppServices.Auth.EndSession();
        AppServices.SaveSettings();
        AppLog.Security("SecuritySettingChange", $"Security Session: {minutes} Minute(n)");
        foreach (var n in new[] { nameof(Session0), nameof(Session1), nameof(Session2), nameof(Session5) }) OnPropertyChanged(n);
    }

    // ---- Startup / Power ----
    public bool StartWithWindows
    {
        get => _s.StartWithWindows;
        set
        {
            _s.StartWithWindows = value;
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (value && Environment.ProcessPath is { } exe)
                    k.SetValue(RunValue, $"\"{exe}\"" + (_s.StartMinimized ? " --minimized" : ""));
                else
                    k.DeleteValue(RunValue, throwOnMissingValue: false);
                AppLog.Info("Settings", value ? "Autostart der App aktiviert" : "Autostart der App deaktiviert");
            }
            catch (UnauthorizedAccessException ex)
            {
                StatusMessage = "Autostart konnte nicht geändert werden: " + ex.Message;
            }
            Save();
        }
    }

    public bool StartMinimized
    {
        get => _s.StartMinimized;
        set
        {
            _s.StartMinimized = value;
            Save();
            if (_s.StartWithWindows) StartWithWindows = true; // Run-Eintrag mit/ohne --minimized aktualisieren
        }
    }

    public bool CountdownEnabled { get => _s.ShutdownCountdownEnabled; set { _s.ShutdownCountdownEnabled = value; Save(); } }

    public bool Countdown10 { get => _s.ShutdownCountdownSeconds == 10; set { if (value) SetCountdown(10); } }
    public bool Countdown30 { get => _s.ShutdownCountdownSeconds == 30; set { if (value) SetCountdown(30); } }
    public bool Countdown60 { get => _s.ShutdownCountdownSeconds == 60; set { if (value) SetCountdown(60); } }

    private void SetCountdown(int seconds)
    {
        _s.ShutdownCountdownSeconds = seconds;
        AppServices.SaveSettings();
        foreach (var n in new[] { nameof(Countdown10), nameof(Countdown30), nameof(Countdown60) }) OnPropertyChanged(n);
    }

    public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    [RelayCommand]
    private void OpenDataFolder()
    {
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { Core.Common.AppPaths.UserDataRoot },
            UseShellExecute = false,
        });
    }
}
