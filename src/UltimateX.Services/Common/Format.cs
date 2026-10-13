using System.Globalization;
using UltimateX.Core.Localization;

namespace UltimateX.Services.Common;

/// <summary>Einheitliche, deutschsprachige Formatierung von Größen, Raten und Zeiten.</summary>
public static class Format
{
    /// <summary>Zahlen-/Datumsformat der aktiven Oberflächensprache.</summary>
    private static CultureInfo De => Loc.Culture;

    public static string Bytes(ulong? bytes)
    {
        if (bytes is null) return "nicht verfügbar";
        double v = bytes.Value;
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        int i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return v.ToString(i == 0 ? "0" : v >= 100 ? "0" : "0.0", De) + " " + units[i];
    }

    public static string Bytes(long bytes) => Bytes(bytes < 0 ? null : (ulong?)bytes);

    /// <summary>Datenrate in Bit/s (Netzwerk-üblich).</summary>
    public static string BitsPerSecond(double bitsPerSecond)
    {
        if (double.IsNaN(bitsPerSecond) || bitsPerSecond < 0) return "–";
        string[] units = ["bit/s", "kbit/s", "Mbit/s", "Gbit/s"];
        int i = 0;
        while (bitsPerSecond >= 1000 && i < units.Length - 1) { bitsPerSecond /= 1000; i++; }
        return bitsPerSecond.ToString(bitsPerSecond >= 100 ? "0" : "0.0", De) + " " + units[i];
    }

    public static string Percent(double value) => value.ToString("0", De) + " %";

    public static string Number(double value, string format = "0.0") => value.ToString(format, De);

    public static string DateText(DateTime? dt) => dt?.ToString(Loc.IsEnglish ? "MM/dd/yyyy" : "dd.MM.yyyy", De) ?? "nicht verfügbar";

    public static string DateTimeText(DateTime? dt) => dt?.ToString(Loc.IsEnglish ? "MM/dd/yyyy HH:mm" : "dd.MM.yyyy HH:mm", De) ?? "nicht verfügbar";

    public static string Duration(TimeSpan span)
        => span.TotalDays >= 1
            ? $"{(int)span.TotalDays} T {span.Hours} Std {span.Minutes} Min"
            : $"{span.Hours} Std {span.Minutes} Min";

    public static string OrNa(string? value) => string.IsNullOrWhiteSpace(value) ? "nicht verfügbar" : value;
}
