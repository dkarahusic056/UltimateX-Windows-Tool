using UltimateX.Core.Common;

namespace UltimateX.Core.Analysis;

/// <summary>Ergebnis einer Prüfung im Stil PASS / WARNING / FAIL (v1.2). N/A, wenn nicht zuverlässig ermittelbar.</summary>
public enum CheckStatus
{
    Pass,
    Warning,
    Fail,
    NotAvailable,
}

/// <summary>Einstufung des ULTIMATE X PERFORMANCE SCORE.</summary>
public enum PerformanceTier
{
    EntryLevel,
    Standard,
    Gaming,
    HighEndGaming,
    Enthusiast,
}

public static class CheckStatusExtensions
{
    public static DataState ToDataState(this CheckStatus s) => s switch
    {
        CheckStatus.Pass => DataState.Ok,
        CheckStatus.Warning => DataState.Warning,
        CheckStatus.Fail => DataState.Problem,
        _ => DataState.NotAvailable,
    };

    public static string Label(this CheckStatus s) => s switch
    {
        CheckStatus.Pass => "PASS",
        CheckStatus.Warning => "WARNING",
        CheckStatus.Fail => "FAIL",
        _ => "N/A",
    };

    /// <summary>Gesamtstatus = schlechtester Einzelstatus (N/A zählt nicht, außer alles ist N/A).</summary>
    public static CheckStatus Worst(IEnumerable<CheckStatus> statuses)
    {
        var list = statuses.Where(s => s != CheckStatus.NotAvailable).ToList();
        if (list.Count == 0) return CheckStatus.NotAvailable;
        if (list.Contains(CheckStatus.Fail)) return CheckStatus.Fail;
        return list.Contains(CheckStatus.Warning) ? CheckStatus.Warning : CheckStatus.Pass;
    }

    public static string Label(this PerformanceTier t) => t switch
    {
        PerformanceTier.EntryLevel => "ENTRY LEVEL",
        PerformanceTier.Standard => "STANDARD",
        PerformanceTier.Gaming => "GAMING",
        PerformanceTier.HighEndGaming => "HIGH-END GAMING",
        _ => "ENTHUSIAST",
    };
}

/// <summary>
/// Gemeinsame Bewertungsmathematik. Alle Punktwerte liegen zwischen 0 und 100.
/// Logarithmische Skalen bilden Leistung so ab, dass eine Verdopplung immer gleich viele Punkte bringt –
/// ein Mittelklasse-PC landet damit in der Mitte statt bei „5 von 100“.
/// </summary>
public static class ScoreMath
{
    public static int Clamp(double v) => (int)Math.Round(Math.Clamp(double.IsNaN(v) ? 0 : v, 0, 100));

    /// <summary>
    /// Logarithmische Skala: <paramref name="reference"/> ergibt 100 Punkte, <paramref name="reference"/>/<paramref name="range"/> ergibt 0 Punkte.
    /// </summary>
    public static int LogScale(double value, double reference, double range = 20)
    {
        if (value <= 0 || reference <= 0 || range <= 1) return 0;
        var low = reference / range;
        return Clamp(100 * Math.Log(value / low) / Math.Log(range));
    }

    /// <summary>Wie <see cref="LogScale"/>, aber für Werte, bei denen kleiner besser ist (z. B. Latenz).</summary>
    public static int LogScaleInverse(double value, double reference, double range = 20)
        => value <= 0 ? 0 : LogScale(1 / value, 1 / reference, range);

    /// <summary>Lineare Skala: bei <paramref name="best"/> 100 Punkte, bei <paramref name="worst"/> 0 Punkte (auch umgekehrt nutzbar).</summary>
    public static int Linear(double value, double worst, double best)
    {
        if (Math.Abs(best - worst) < 1e-9) return 0;
        return Clamp(100 * (value - worst) / (best - worst));
    }

    /// <summary>Gewichteter Mittelwert; Einträge ohne Wert (null) werden ignoriert. Null, wenn kein Wert vorliegt.</summary>
    public static int? WeightedAverage(IEnumerable<(int? Score, double Weight)> parts)
    {
        double sum = 0, weights = 0;
        foreach (var (score, weight) in parts)
        {
            if (score is null || weight <= 0) continue;
            sum += score.Value * weight;
            weights += weight;
        }
        return weights <= 0 ? null : Clamp(sum / weights);
    }

    public static PerformanceTier TierOf(int total) => total switch
    {
        < 35 => PerformanceTier.EntryLevel,
        < 55 => PerformanceTier.Standard,
        < 72 => PerformanceTier.Gaming,
        < 87 => PerformanceTier.HighEndGaming,
        _ => PerformanceTier.Enthusiast,
    };

    /// <summary>Statusampel für einen Punktwert.</summary>
    public static CheckStatus StatusOf(int? score, int warnBelow = 60, int failBelow = 35)
        => score is null ? CheckStatus.NotAvailable : score < failBelow ? CheckStatus.Fail : score < warnBelow ? CheckStatus.Warning : CheckStatus.Pass;
}

/// <summary>Ein Befund mit Ampel, z. B. für Gaming Check, BIOS-Center oder Bottleneck-Analyse.</summary>
public sealed record Finding(string Area, CheckStatus Status, string Value, string? Advice = null)
{
    public DataState State => Status.ToDataState();

    public string StatusText => Status.Label();
}
