using System.Globalization;

namespace UltimateX.Core.Analysis;

/// <summary>Von Hand übernommenes Ergebnis eines speedtest.ch-Tests (v1.3). Jitter optional (null = nicht angegeben).</summary>
public sealed record ManualSpeedtest(double DownloadMbps, double UploadMbps, double PingMs, double? JitterMs);

/// <summary>
/// Prüfung der Eingabefelder für speedtest.ch-Ergebnisse. Es wird nichts von der Webseite ausgelesen –
/// die Werte tippt der Benutzer ab. Komma und Punkt werden als Dezimaltrennzeichen akzeptiert.
/// </summary>
public static class ManualSpeedtestEntry
{
    /// <summary>Quellenkennung im Speedtest-Verlauf (Spalte „server“), unterscheidet manuelle Einträge vom eigenen Cloudflare-Test.</summary>
    public const string SourceName = "speedtest.ch (manuell)";

    public static bool IsManualSource(string? server) => string.Equals(server, SourceName, StringComparison.Ordinal);

    public static bool TryParse(string? download, string? upload, string? ping, string? jitter, out ManualSpeedtest? result, out string? error)
    {
        result = null;
        error = null;
        if (!TryNumber(download, 0.01, 100_000, out var down)) { error = "Download: bitte eine Zahl zwischen 0,01 und 100000 Mbit/s eingeben."; return false; }
        if (!TryNumber(upload, 0.01, 100_000, out var up)) { error = "Upload: bitte eine Zahl zwischen 0,01 und 100000 Mbit/s eingeben."; return false; }
        if (!TryNumber(ping, 0, 10_000, out var p)) { error = "Ping: bitte eine Zahl zwischen 0 und 10000 ms eingeben."; return false; }
        double? j = null;
        if (!string.IsNullOrWhiteSpace(jitter))
        {
            if (!TryNumber(jitter, 0, 10_000, out var jv)) { error = "Jitter: bitte eine Zahl zwischen 0 und 10000 ms eingeben oder leer lassen."; return false; }
            j = jv;
        }
        result = new ManualSpeedtest(down, up, p, j);
        return true;
    }

    private static bool TryNumber(string? text, double min, double max, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (t.Length > 12) return false;
        t = t.Replace(',', '.');
        if (!double.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)) return false;
        return !double.IsNaN(value) && value >= min && value <= max;
    }
}
