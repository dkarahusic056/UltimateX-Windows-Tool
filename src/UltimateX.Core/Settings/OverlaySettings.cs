using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Core.Settings;

public enum OverlayCorner { TopLeft, TopRight, BottomLeft, BottomRight }

/// <summary>Wählbare Overlay-Zeilen (Reihenfolge = Anzeigereihenfolge).</summary>
public enum OverlayElement
{
    Fps,
    Frametime,
    OnePercentLow,
    CpuLoad,
    CpuTemp,
    CpuClock,
    GpuLoad,
    GpuTemp,
    GpuClock,
    GpuPower,
    Ram,
    Vram,
    Ping,
    Clock,
}

/// <summary>Ping-Ziel des Overlays: Standardgateway (lokal, kein Internetzugriff) oder 1.1.1.1 (Cloudflare, wie beim Speedtest).</summary>
public enum OverlayPingTarget { Gateway, Cloudflare }

/// <summary>Gespeicherte Overlay-Darstellung unter einem Namen (z. B. „Shooter“, „Minimal“).</summary>
public sealed class OverlayProfile
{
    public string Name { get; set; } = "Standard";
    public List<OverlayElement> Elements { get; set; } = [];
    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
    public double FontSize { get; set; } = 14;
    public double Opacity { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.55;
}

/// <summary>
/// Einstellungen des In-Game-Overlays (v1.3) – gespeichert in %LOCALAPPDATA%\UltimateX\overlay.json.
/// Alle Werte werden beim Laden über <see cref="Normalize"/> begrenzt; eine beschädigte Datei führt zu Standardwerten.
/// </summary>
public sealed class OverlaySettings
{
    public static IReadOnlyList<OverlayElement> DefaultElements { get; } =
        [OverlayElement.Fps, OverlayElement.Frametime, OverlayElement.OnePercentLow, OverlayElement.CpuLoad, OverlayElement.CpuTemp,
         OverlayElement.GpuLoad, OverlayElement.GpuTemp, OverlayElement.Ram];

    /// <summary>Overlay beim Programmstart sofort einblenden.</summary>
    public bool ShowOnStartup { get; set; }

    public string Hotkey { get; set; } = HotkeyGesture.Default.ToString();

    /// <summary>Gerätename des Monitors (z. B. \\.\DISPLAY2); null = Primärmonitor.</summary>
    public string? MonitorDevice { get; set; }

    public OverlayCorner Corner { get; set; } = OverlayCorner.TopLeft;
    public double FontSize { get; set; } = 14;
    /// <summary>Deckkraft des gesamten Overlays (0,3–1).</summary>
    public double Opacity { get; set; } = 1.0;
    /// <summary>Deckkraft des Hintergrunds hinter den Werten (0–1).</summary>
    public double BackgroundOpacity { get; set; } = 0.55;
    public List<OverlayElement> Elements { get; set; } = [.. DefaultElements];
    public OverlayPingTarget PingTarget { get; set; } = OverlayPingTarget.Gateway;

    public List<OverlayProfile> Profiles { get; set; } = [];
    public string? ActiveProfile { get; set; }

    public static string DefaultPath => Path.Combine(AppPaths.UserDataRoot, "overlay.json");

    public OverlaySettings Normalize()
    {
        FontSize = Clamp(FontSize, 9, 40, 14);
        Opacity = Clamp(Opacity, 0.3, 1, 1);
        BackgroundOpacity = Clamp(BackgroundOpacity, 0, 1, 0.55);
        Elements = (Elements ?? []).Where(e => Enum.IsDefined(e)).Distinct().ToList();
        if (!HotkeyGesture.TryParse(Hotkey, out var g)) g = HotkeyGesture.Default;
        Hotkey = g.ToString();
        if (!Enum.IsDefined(Corner)) Corner = OverlayCorner.TopLeft;
        if (!Enum.IsDefined(PingTarget)) PingTarget = OverlayPingTarget.Gateway;
        if (MonitorDevice is { Length: > 64 }) MonitorDevice = null;
        Profiles = (Profiles ?? [])
            .Where(p => p is not null && !string.IsNullOrWhiteSpace(p.Name))
            .GroupBy(p => p.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(grp => grp.First())
            .Take(20)
            .ToList();
        foreach (var p in Profiles)
        {
            p.Name = p.Name.Trim().Length > 40 ? p.Name.Trim()[..40] : p.Name.Trim();
            p.Elements = (p.Elements ?? []).Where(e => Enum.IsDefined(e)).Distinct().ToList();
            p.FontSize = Clamp(p.FontSize, 9, 40, 14);
            p.Opacity = Clamp(p.Opacity, 0.3, 1, 1);
            p.BackgroundOpacity = Clamp(p.BackgroundOpacity, 0, 1, 0.55);
            if (!Enum.IsDefined(p.Corner)) p.Corner = OverlayCorner.TopLeft;
        }
        if (ActiveProfile is not null && !Profiles.Any(p => p.Name.Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase)))
            ActiveProfile = null;
        return this;
    }

    /// <summary>Speichert die aktuelle Darstellung als Profil (ersetzt ein gleichnamiges).</summary>
    public OverlayProfile SaveProfile(string name)
    {
        name = name.Trim();
        if (name.Length == 0) throw new ArgumentException("Profilname fehlt.", nameof(name));
        if (name.Length > 40) name = name[..40];
        Profiles.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var profile = new OverlayProfile
        {
            Name = name, Elements = [.. Elements], Corner = Corner, FontSize = FontSize, Opacity = Opacity, BackgroundOpacity = BackgroundOpacity,
        };
        Profiles.Add(profile);
        ActiveProfile = name;
        return profile;
    }

    /// <summary>Übernimmt die Darstellung eines Profils. false, wenn es nicht existiert.</summary>
    public bool ApplyProfile(string name)
    {
        var p = Profiles.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (p is null) return false;
        Elements = [.. p.Elements];
        Corner = p.Corner;
        FontSize = p.FontSize;
        Opacity = p.Opacity;
        BackgroundOpacity = p.BackgroundOpacity;
        ActiveProfile = p.Name;
        return true;
    }

    private static double Clamp(double v, double min, double max, double fallback)
        => double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Clamp(v, min, max);

    public static OverlaySettings Load(string? path = null) => JsonSettingsFile.Load<OverlaySettings>(path ?? DefaultPath).Normalize();

    public void Save(string? path = null) => JsonSettingsFile.Save(path ?? DefaultPath, Normalize());
}

/// <summary>
/// Einstellungen der Hintergrundüberwachung im Infobereich (v1.3) – %LOCALAPPDATA%\UltimateX\background-monitor.json.
/// </summary>
public sealed class BackgroundMonitorSettings
{
    /// <summary>Symbol im Infobereich anzeigen und im Hintergrund prüfen.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Beim Schließen des Hauptfensters in den Infobereich minimieren statt zu beenden.</summary>
    public bool MinimizeToTrayOnClose { get; set; }
    public AlertSettings Rules { get; set; } = new();
    /// <summary>Zeitpunkt der letzten Bluescreen-Prüfung (UTC) – nur neuere Ereignisse werden gemeldet.</summary>
    public DateTime? LastBlueScreenCheckUtc { get; set; }

    public static string DefaultPath => Path.Combine(AppPaths.UserDataRoot, "background-monitor.json");

    public BackgroundMonitorSettings Normalize()
    {
        Rules = (Rules ?? new AlertSettings()).Normalize();
        if (LastBlueScreenCheckUtc is { } t && t > DateTime.UtcNow.AddDays(1)) LastBlueScreenCheckUtc = null;
        return this;
    }

    public static BackgroundMonitorSettings Load(string? path = null) => JsonSettingsFile.Load<BackgroundMonitorSettings>(path ?? DefaultPath).Normalize();

    public void Save(string? path = null) => JsonSettingsFile.Save(path ?? DefaultPath, Normalize());
}

/// <summary>Kleine JSON-Einstellungsdateien: lesen mit Rückfall auf Standardwerte, atomar schreiben (temp + ersetzen).</summary>
public static class JsonSettingsFile
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        MaxDepth = 16,
    };

    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path)) return new T();
            var info = new FileInfo(path);
            if (info.Length > 256 * 1024) throw new InvalidDataException("Datei zu groß");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? new T();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            AppLog.Warn("Settings", $"{Path.GetFileName(path)} unlesbar – Standardwerte", ex.Message);
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Settings", $"{Path.GetFileName(path)} konnte nicht gespeichert werden", ex.Message);
        }
    }
}
