using UltimateX.Core.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Security;

namespace UltimateX.Services.Health;

public sealed record ScoreFactor(string Name, int Deduction, string Reason);

public sealed record HealthScoreResult(int Score, IReadOnlyList<ScoreFactor> Factors)
{
    public string Rating => Score switch
    {
        >= 90 => "Sehr gut",
        >= 75 => "Gut",
        >= 50 => "Verbesserungswürdig",
        _ => "Kritisch",
    };

    public DataState State => Score >= 75 ? DataState.Ok : Score >= 50 ? DataState.Warning : DataState.Problem;
}

/// <summary>
/// PC Health Score (§50): vollständig nachvollziehbar. Start bei 100, jeder Abzug wird mit Grund aufgelistet.
/// Es fließen nur gemessene Werte ein; nicht verfügbare Daten führen zu KEINEM Abzug und keiner Gutschrift.
/// Der Score ist eine Orientierung, keine Garantie (§107).
/// </summary>
public static class HealthScore
{
    public static HealthScoreResult Calculate(
        HardwareSnapshot? hw,
        IReadOnlyList<SecurityItem>? security,
        RebootStatus? reboot,
        IReadOnlyList<ServiceStatus>? services,
        CrashSummary? crashes)
    {
        var f = new List<ScoreFactor>();

        if (hw is not null)
        {
            foreach (var v in hw.Volumes)
            {
                if (v.UsedPercent is >= 95) f.Add(new($"Speicherplatz {v.Letter}", 15, $"{v.UsedPercent:0} % belegt"));
                else if (v.UsedPercent is >= 85) f.Add(new($"Speicherplatz {v.Letter}", 5, $"{v.UsedPercent:0} % belegt"));
            }
            foreach (var d in hw.Disks)
            {
                if (d.HealthState == DataState.Problem) f.Add(new($"Datenträger {d.Name}", 30, "Windows meldet den Datenträger als fehlerhaft"));
                else if (d.HealthState == DataState.Warning) f.Add(new($"Datenträger {d.Name}", 15, "Windows meldet eine Warnung für den Datenträger"));
            }
        }

        if (security is not null)
        {
            foreach (var s in security.Where(s => s.Area != "Application Integrity"))
            {
                if (s.State == DataState.Problem) f.Add(new(s.Area, 10, s.Status));
                else if (s.State == DataState.Warning) f.Add(new(s.Area, 3, s.Status));
            }
        }

        if (reboot is { RebootRequired: true }) f.Add(new("Neustart ausstehend", 3, string.Join(", ", reboot.Reasons)));

        if (services is not null)
        {
            foreach (var s in services.Where(s => s.Health == DataState.Problem))
                f.Add(new($"Dienst {s.DisplayName}", 5, $"Starttyp: {s.StartMode}"));
        }

        if (crashes is not null)
        {
            if (crashes.BugChecks > 0) f.Add(new("Bluescreens (30 Tage)", Math.Min(20, crashes.BugChecks * 5), $"{crashes.BugChecks} Bluescreen(s)"));
            if (crashes.UnexpectedShutdowns > 2) f.Add(new("Unerwartete Neustarts (30 Tage)", 5, $"{crashes.UnexpectedShutdowns} Ereignisse"));
        }

        var score = Math.Clamp(100 - f.Sum(x => x.Deduction), 0, 100);
        return new HealthScoreResult(score, f);
    }
}
