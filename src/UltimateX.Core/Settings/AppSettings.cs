using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Core.Settings;

public enum ThemeMode { System, Dark, Light }

public enum LayoutMode
{
    ModernControlCenter,
    SimpleMode,
    GamingMode,
    TechnicianMode,
    VisualPcMap,
    AdminServiceMode,
}

/// <summary>Sprache der Oberfläche. Automatisch = Windows-Anzeigesprache (Deutsch, sonst Englisch).</summary>
public enum AppLanguage { Automatisch, Deutsch, English }

/// <summary>Alle Benutzereinstellungen (§84). Werte werden beim Laden validiert/geklemmt.</summary>
public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public LayoutMode Layout { get; set; } = LayoutMode.ModernControlCenter;
    public AppLanguage Language { get; set; } = AppLanguage.Automatisch;

    /// <summary>Sensible Werte (MAC, Public IP, Seriennummern) maskiert anzeigen.</summary>
    public bool SensitiveMode { get; set; } = true;

    /// <summary>Gültigkeit der Security Session in Minuten: 0 = immer erneut fragen, 1, 2, 5.</summary>
    public int SecuritySessionMinutes { get; set; }

    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool ShutdownCountdownEnabled { get; set; } = true;
    public int ShutdownCountdownSeconds { get; set; } = 30;
    public bool FirstStartCompleted { get; set; }

    /// <summary>Hinweis zum Datenverbrauch des Speedtests wurde bestätigt (§23).</summary>
    public bool SpeedtestNoticeAccepted { get; set; }

    /// <summary>Hinweis „nur eigene Netzwerke scannen“ wurde bestätigt (§103, THREAT_MODEL T-25).</summary>
    public bool NetworkScanNoticeAccepted { get; set; }

    /// <summary>Windows-Benachrichtigungen (§83).</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Vor Treiber-Updates und Windows Repair automatisch einen Wiederherstellungspunkt anlegen (§86).</summary>
    public bool AutoRestorePoint { get; set; } = true;

    /// <summary>
    /// HTTPS-Adresse des Update-Manifests der App (§64). Leer = Update-Prüfung deaktiviert.
    /// Wird vom Herausgeber gesetzt; Updates werden nur installiert, wenn SHA-256 UND Authenticode-Signatur passen.
    /// </summary>
    public string UpdateManifestUrl { get; set; } = string.Empty;

    public bool AutoCheckAppUpdates { get; set; } = true;

    internal void Sanitize()
    {
        if (!Enum.IsDefined(Theme)) Theme = ThemeMode.System;
        if (!Enum.IsDefined(Layout)) Layout = LayoutMode.ModernControlCenter;
        if (!Enum.IsDefined(Language)) Language = AppLanguage.Automatisch;
        if (SecuritySessionMinutes is not (0 or 1 or 2 or 5)) SecuritySessionMinutes = 0;
        ShutdownCountdownSeconds = Math.Clamp(ShutdownCountdownSeconds, 0, 600);
        // Manipulierte Konfiguration darf keine unsicheren Update-Quellen einschleusen (§105).
        if (!string.IsNullOrEmpty(UpdateManifestUrl) &&
            (!Uri.TryCreate(UpdateManifestUrl, UriKind.Absolute, out var u) || u.Scheme != Uri.UriSchemeHttps || UpdateManifestUrl.Length > 500))
            UpdateManifestUrl = string.Empty;
    }
}

/// <summary>
/// Lädt/speichert Einstellungen als JSON. Eine beschädigte oder manipulierte Datei
/// führt nicht zum Absturz: Sie wird gesichert und durch sichere Standardwerte ersetzt (§105, Fail Securely).
/// </summary>
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return new AppSettings();

        try
        {
            var info = new FileInfo(path);
            if (info.Length > 64 * 1024) throw new JsonException("Settings-Datei unplausibel groß.");
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options) ?? new AppSettings();
            settings.Sanitize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
        {
            try
            {
                File.Move(path, path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            }
            catch (IOException)
            {
            }
            AppLog.Warn("Settings", "Einstellungen waren beschädigt und wurden auf Standardwerte zurückgesetzt.", ex.Message);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        settings.Sanitize();
        var path = AppPaths.SettingsFile;
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true); // atomarer Austausch
    }
}
