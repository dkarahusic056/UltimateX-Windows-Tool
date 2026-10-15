using System.Globalization;
using System.Text;
using UltimateX.Core.Localization;
using UltimateX.Core.Settings;

namespace UltimateX.Core.Analysis;

/// <summary>Aktuelle Messwerte für das Overlay. Null = keine Quelle → die Zeile wird ausgeblendet.</summary>
public sealed record OverlayValues(
    double? Fps = null,
    double? FrametimeMs = null,
    double? OnePercentLowFps = null,
    double? CpuLoad = null,
    double? CpuTempC = null,
    double? CpuClockMHz = null,
    double? GpuLoad = null,
    double? GpuTempC = null,
    double? GpuClockMHz = null,
    double? GpuPowerW = null,
    double? RamPercent = null,
    ulong? RamUsedBytes = null,
    ulong? VramUsedBytes = null,
    double? PingMs = null,
    DateTime? Clock = null,
    bool FpsUnavailable = false);

public sealed record OverlayLine(OverlayElement Element, string Label, string Value);

/// <summary>
/// Baut die Overlay-Zeilen aus Einstellungen und Messwerten (testbar, ohne WPF).
/// Regel „keine Fantasiewerte“: Fehlt ein Wert, entfällt die Zeile. Ausnahme: Ist die FPS-Messung mangels Rechten
/// nicht möglich (<see cref="OverlayValues.FpsUnavailable"/>), zeigt die FPS-Zeile „N/A“, damit der Grund in der App sichtbar wird.
/// </summary>
public static class OverlayComposer
{
    public static string LabelOf(OverlayElement e) => e switch
    {
        OverlayElement.Fps => "FPS",
        OverlayElement.Frametime => "Frametime",
        OverlayElement.OnePercentLow => "1 % Low",
        OverlayElement.CpuLoad => "CPU",
        OverlayElement.CpuTemp => "CPU-Temp.",
        OverlayElement.CpuClock => "CPU-Takt",
        OverlayElement.GpuLoad => "GPU",
        OverlayElement.GpuTemp => "GPU-Temp.",
        OverlayElement.GpuClock => "GPU-Takt",
        OverlayElement.GpuPower => "GPU-Leistung",
        OverlayElement.Ram => "RAM",
        OverlayElement.Vram => "VRAM",
        OverlayElement.Ping => "Ping",
        _ => "Uhrzeit",
    };

    public static IReadOnlyList<OverlayLine> Compose(IEnumerable<OverlayElement> elements, OverlayValues v, CultureInfo? culture = null)
    {
        var c = culture ?? CultureInfo.CurrentCulture;
        var lines = new List<OverlayLine>();
        foreach (var e in elements.Distinct())
        {
            string? value = e switch
            {
                OverlayElement.Fps => v.Fps is { } f ? f.ToString("0", c) : v.FpsUnavailable ? "N/A" : null,
                OverlayElement.Frametime => v.FrametimeMs is { } ft ? ft.ToString("0.0", c) + " ms" : null,
                OverlayElement.OnePercentLow => v.OnePercentLowFps is { } low ? low.ToString("0", c) + " FPS" : null,
                OverlayElement.CpuLoad => v.CpuLoad is { } cl ? cl.ToString("0", c) + " %" : null,
                OverlayElement.CpuTemp => v.CpuTempC is { } ct ? ct.ToString("0", c) + " °C" : null,
                OverlayElement.CpuClock => v.CpuClockMHz is { } cc ? Clock(cc, c) : null,
                OverlayElement.GpuLoad => v.GpuLoad is { } gl ? gl.ToString("0", c) + " %" : null,
                OverlayElement.GpuTemp => v.GpuTempC is { } gt ? gt.ToString("0", c) + " °C" : null,
                OverlayElement.GpuClock => v.GpuClockMHz is { } gc ? Clock(gc, c) : null,
                OverlayElement.GpuPower => v.GpuPowerW is { } gp ? gp.ToString("0", c) + " W" : null,
                OverlayElement.Ram => Ram(v, c),
                OverlayElement.Vram => v.VramUsedBytes is { } vr ? Gb(vr, c) : null,
                OverlayElement.Ping => v.PingMs is { } p ? p.ToString("0", c) + " ms" : null,
                OverlayElement.Clock => v.Clock is { } t ? t.ToString("HH:mm", c) : null,
                _ => null,
            };
            if (value is not null) lines.Add(new OverlayLine(e, Loc.T(LabelOf(e)), value));
        }
        return lines;
    }

    private static string Clock(double mhz, CultureInfo c) => mhz >= 1000 ? (mhz / 1000).ToString("0.00", c) + " GHz" : mhz.ToString("0", c) + " MHz";

    private static string Gb(ulong bytes, CultureInfo c) => (bytes / 1073741824.0).ToString("0.0", c) + " GB";

    private static string? Ram(OverlayValues v, CultureInfo c)
    {
        if (v.RamUsedBytes is { } used && v.RamPercent is { } pct) return $"{Gb(used, c)} ({pct.ToString("0", c)} %)";
        if (v.RamUsedBytes is { } u) return Gb(u, c);
        return v.RamPercent is { } p ? p.ToString("0", c) + " %" : null;
    }
}

/// <summary>Ein sekündlicher Eintrag einer aufgezeichneten Spiel-Sitzung. Null = kein Messwert (in der CSV leer).</summary>
public sealed record GameSessionSample(
    DateTime Time,
    string? Process,
    double? Fps,
    double? FrametimeMs,
    double? OnePercentLowFps,
    double? CpuLoad,
    double? CpuTempC,
    double? GpuLoad,
    double? GpuTempC);

public sealed record GameSessionSummary(int Samples, TimeSpan Duration, double? AverageFps, double? MinFps, double? AverageOnePercentLow,
    double? MaxCpuTempC, double? MaxGpuTempC, string? MainProcess);

/// <summary>
/// CSV-Format der Sitzungsaufzeichnung (Dokumente\ULTIMATE X\Sessions). Trennzeichen „;“, Zahlen mit Punkt (invariant),
/// Zeit im ISO-Format. Prozessnamen werden gegen Formel-Injektion in Tabellenprogrammen geschützt.
/// </summary>
public static class GameSessionCsv
{
    public const string Header = "Zeit;Prozess;FPS;Frametime_ms;OnePercentLow_FPS;CPU_Last_Prozent;CPU_Temp_C;GPU_Last_Prozent;GPU_Temp_C";

    public static string FormatRow(GameSessionSample s)
    {
        static string N(double? v) => v is { } x && !double.IsNaN(x) && !double.IsInfinity(x) ? x.ToString("0.##", CultureInfo.InvariantCulture) : "";
        return string.Join(';',
            s.Time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            SafeCell(s.Process),
            N(s.Fps), N(s.FrametimeMs), N(s.OnePercentLowFps), N(s.CpuLoad), N(s.CpuTempC), N(s.GpuLoad), N(s.GpuTempC));
    }

    /// <summary>Entfernt Trenn-/Steuerzeichen und entschärft führende =, +, -, @ (CSV-Injection).</summary>
    public static string SafeCell(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (ch != ';' && ch != '"' && !char.IsControl(ch)) sb.Append(ch);
        var s = sb.ToString();
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@') s = "'" + s;
        return s.Length > 80 ? s[..80] : s;
    }

    public static IReadOnlyList<GameSessionSample> Parse(IEnumerable<string> lines)
    {
        var list = new List<GameSessionSample>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("Zeit;", StringComparison.Ordinal)) continue;
            var p = line.Split(';');
            if (p.Length < 9) continue;
            if (!DateTime.TryParseExact(p[0], "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
            static double? D(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
            list.Add(new GameSessionSample(t, p[1].Length == 0 ? null : p[1], D(p[2]), D(p[3]), D(p[4]), D(p[5]), D(p[6]), D(p[7]), D(p[8])));
            if (list.Count >= 86_400) break; // max. 24 h
        }
        return list;
    }

    public static GameSessionSummary Summarize(IReadOnlyList<GameSessionSample> samples)
    {
        if (samples.Count == 0) return new GameSessionSummary(0, TimeSpan.Zero, null, null, null, null, null, null);
        static double? Avg(IEnumerable<double?> xs) { var l = xs.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Average(); }
        static double? Max(IEnumerable<double?> xs) { var l = xs.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Max(); }
        static double? Min(IEnumerable<double?> xs) { var l = xs.Where(x => x is not null).Select(x => x!.Value).ToList(); return l.Count == 0 ? null : l.Min(); }
        var main = samples.Where(s => s.Fps is not null && s.Process is not null).GroupBy(s => s.Process!).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        return new GameSessionSummary(
            samples.Count,
            samples[^1].Time - samples[0].Time,
            Avg(samples.Select(s => s.Fps)),
            Min(samples.Select(s => s.Fps)),
            Avg(samples.Select(s => s.OnePercentLowFps)),
            Max(samples.Select(s => s.CpuTempC)),
            Max(samples.Select(s => s.GpuTempC)),
            main);
    }
}
