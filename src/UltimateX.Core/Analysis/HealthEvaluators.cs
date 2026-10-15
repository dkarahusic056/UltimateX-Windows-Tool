using UltimateX.Core.Common;

namespace UltimateX.Core.Analysis;

// ============================================================ Laufwerke (v1.2 §7)

public enum DriveVerdict { Good, Warning, Critical, NotAvailable }

/// <summary>Alle für die Bewertung relevanten Laufwerkswerte. Null = vom Laufwerk nicht gemeldet.</summary>
public sealed record DriveHealthInput(
    string Name,
    string MediaType,
    DataState WindowsHealth,
    int? TemperatureC,
    int? TemperatureMaxC,
    long? PowerOnHours,
    long? PowerCycles,
    int? WearPercent,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected);

public sealed record DriveHealthResult(DriveVerdict Verdict, IReadOnlyList<string> Reasons)
{
    public string Label => Verdict switch
    {
        DriveVerdict.Good => "GOOD",
        DriveVerdict.Warning => "WARNING",
        DriveVerdict.Critical => "CRITICAL",
        _ => "N/A",
    };

    public DataState State => Verdict switch
    {
        DriveVerdict.Good => DataState.Ok,
        DriveVerdict.Warning => DataState.Warning,
        DriveVerdict.Critical => DataState.Problem,
        _ => DataState.NotAvailable,
    };
}

public static class DriveHealthEvaluator
{
    /// <summary>Schlechtester Befund mehrerer Laufwerke. N/A nur, wenn kein Laufwerk Daten liefert (ein N/A-Laufwerk verdeckt nie ein CRITICAL).</summary>
    public static DriveVerdict Worst(IEnumerable<DriveVerdict> verdicts)
    {
        var list = verdicts.ToList();
        return list.Contains(DriveVerdict.Critical) ? DriveVerdict.Critical
            : list.Contains(DriveVerdict.Warning) ? DriveVerdict.Warning
            : list.Contains(DriveVerdict.Good) ? DriveVerdict.Good
            : DriveVerdict.NotAvailable;
    }

    public static DriveHealthResult Evaluate(DriveHealthInput d)
    {
        var reasons = new List<string>();
        var verdict = DriveVerdict.Good;
        void Raise(DriveVerdict v, string reason)
        {
            reasons.Add(reason);
            if (v > verdict) verdict = v;
        }

        var hasData = d.WindowsHealth != DataState.NotAvailable || d.TemperatureC is not null || d.WearPercent is not null
                      || d.ReadErrorsUncorrected is not null || d.WriteErrorsUncorrected is not null || d.PowerOnHours is not null;
        if (!hasData) return new DriveHealthResult(DriveVerdict.NotAvailable, ["Das Laufwerk meldet keine Zustandsdaten."]);

        if (d.WindowsHealth == DataState.Problem) Raise(DriveVerdict.Critical, "Windows meldet das Laufwerk als fehlerhaft.");
        else if (d.WindowsHealth == DataState.Warning) Raise(DriveVerdict.Warning, "Windows meldet eine Warnung für das Laufwerk.");

        var uncorrected = (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0);
        if (uncorrected > 10) Raise(DriveVerdict.Critical, $"{uncorrected} nicht korrigierbare Lese-/Schreibfehler – Daten sichern.");
        else if (uncorrected > 0) Raise(DriveVerdict.Warning, $"{uncorrected} nicht korrigierbare Lese-/Schreibfehler.");

        if (d.WearPercent is { } wear)
        {
            if (wear >= 90) Raise(DriveVerdict.Critical, $"Verschleiß {wear} % – Lebensdauer nahezu erreicht.");
            else if (wear >= 70) Raise(DriveVerdict.Warning, $"Verschleiß {wear} %.");
        }

        if (d.TemperatureC is { } t)
        {
            var isHdd = d.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase);
            var limit = d.TemperatureMaxC is > 0 ? d.TemperatureMaxC.Value : isHdd ? 55 : 70;
            if (t >= limit) Raise(DriveVerdict.Warning, $"Temperatur {t} °C (Grenze {limit} °C).");
        }

        if (reasons.Count == 0) reasons.Add("Keine Auffälligkeiten in den gemeldeten Werten.");
        return new DriveHealthResult(verdict, reasons);
    }
}

// ============================================================ Netzwerk (v1.2 §8)

public sealed record NetworkMeasurements(
    double? DownloadMbps,
    double? UploadMbps,
    double? PingMs,
    double? JitterMs,
    double? PacketLossPercent,
    double? DnsMs,
    double? GatewayPingMs,
    double? LinkSpeedMbps,
    int? WifiSignalPercent,
    bool? Ipv4Ok,
    bool? Ipv6Ok);

public sealed record NetworkHealthResult(int? Score, IReadOnlyList<Finding> Findings);

public static class NetworkHealth
{
    public static NetworkHealthResult Evaluate(NetworkMeasurements m)
    {
        var findings = new List<Finding>();
        int? Add(string area, double? value, Func<double, int> score, string unit, int warnBelow = 60)
        {
            if (value is null)
            {
                findings.Add(new Finding(area, CheckStatus.NotAvailable, "N/A"));
                return null;
            }
            var s = score(value.Value);
            findings.Add(new Finding(area, ScoreMath.StatusOf(s, warnBelow, 30), $"{value.Value:0.#} {unit}".Trim()));
            return s;
        }

        var down = Add("Download", m.DownloadMbps, v => ScoreMath.LogScale(v, 1000, 100), "Mbit/s");
        var up = Add("Upload", m.UploadMbps, v => ScoreMath.LogScale(v, 500, 100), "Mbit/s");
        var ping = Add("Ping", m.PingMs, v => ScoreMath.Linear(v, 100, 5), "ms");
        var jitter = Add("Jitter", m.JitterMs, v => ScoreMath.Linear(v, 30, 1), "ms");
        var loss = Add("Paketverlust", m.PacketLossPercent, v => ScoreMath.Linear(v, 5, 0), "%");
        var dns = Add("DNS-Antwortzeit", m.DnsMs, v => ScoreMath.Linear(v, 150, 10), "ms");
        var gw = Add("Gateway-Ping", m.GatewayPingMs, v => ScoreMath.Linear(v, 20, 1), "ms");
        Add("LAN-/Verbindungsgeschwindigkeit", m.LinkSpeedMbps, v => ScoreMath.LogScale(v, 1000, 100), "Mbit/s");
        var wifi = m.WifiSignalPercent is null ? null : Add("WLAN-Signal", m.WifiSignalPercent, v => ScoreMath.Linear(v, 30, 80), "%");
        if (m.WifiSignalPercent is null) findings.Add(new Finding("WLAN-Signal", CheckStatus.NotAvailable, "Kein WLAN aktiv"));

        findings.Add(new Finding("IPv4", m.Ipv4Ok switch { true => CheckStatus.Pass, false => CheckStatus.Fail, _ => CheckStatus.NotAvailable },
            m.Ipv4Ok switch { true => "Verbunden", false => "Keine IPv4-Verbindung", _ => "N/A" }));
        findings.Add(new Finding("IPv6", m.Ipv6Ok switch { true => CheckStatus.Pass, false => CheckStatus.Warning, _ => CheckStatus.NotAvailable },
            m.Ipv6Ok switch { true => "Verbunden", false => "Kein IPv6 (meist unkritisch)", _ => "N/A" }));

        var score = ScoreMath.WeightedAverage(
        [
            (down, 20), (up, 10), (ping, 15), (jitter, 10), (loss, 15), (dns, 10), (gw, 10), (wifi, 5),
        ]);
        if (score is not null && m.Ipv4Ok == false) score = Math.Min(score.Value, 10);
        return new NetworkHealthResult(score, findings);
    }
}

// ============================================================ Windows (v1.2 §36)

public sealed record WindowsHealthInputs(
    bool? RebootPending,
    int? DisabledCriticalServices,
    int? BugChecks30Days,
    int? UnexpectedShutdowns30Days,
    int? CriticalEventTypes7Days,
    bool? SystemRestoreEnabled,
    int? DaysSinceLastUpdate,
    bool? DefenderOk,
    int? DriveProblems);

public static class WindowsHealthScore
{
    /// <summary>100 Punkte abzüglich gewichteter Befunde. Nicht ermittelbare Werte ziehen nichts ab, werden aber gelistet.</summary>
    public static (int Score, IReadOnlyList<Finding> Findings) Calculate(WindowsHealthInputs i)
    {
        var score = 100;
        var f = new List<Finding>();
        void Item(string area, bool? bad, int deduction, string okText, string badText, CheckStatus badStatus = CheckStatus.Warning)
        {
            if (bad is null) { f.Add(new Finding(area, CheckStatus.NotAvailable, "N/A")); return; }
            if (bad.Value) { score -= deduction; f.Add(new Finding(area, badStatus, badText)); }
            else f.Add(new Finding(area, CheckStatus.Pass, okText));
        }

        Item("Neustart", i.RebootPending, 5, "Kein Neustart ausstehend", "Neustart ausstehend");
        Item("Wichtige Dienste", i.DisabledCriticalServices is null ? null : i.DisabledCriticalServices > 0, 15,
            "Alle wichtigen Dienste aktiv", $"{i.DisabledCriticalServices} wichtige(r) Dienst(e) deaktiviert", CheckStatus.Fail);
        Item("Bluescreens (30 Tage)", i.BugChecks30Days is null ? null : i.BugChecks30Days > 0, Math.Min(30, 10 * (i.BugChecks30Days ?? 0)),
            "Keine", $"{i.BugChecks30Days} Bluescreen(s)", CheckStatus.Fail);
        Item("Unerwartete Neustarts", i.UnexpectedShutdowns30Days is null ? null : i.UnexpectedShutdowns30Days > 2, 10,
            $"{i.UnexpectedShutdowns30Days ?? 0} in 30 Tagen", $"{i.UnexpectedShutdowns30Days} in 30 Tagen");
        Item("Kritische Ereignisse (7 Tage)", i.CriticalEventTypes7Days is null ? null : i.CriticalEventTypes7Days > 5, 5,
            $"{i.CriticalEventTypes7Days ?? 0} Fehlerarten", $"{i.CriticalEventTypes7Days} Fehlerarten");
        Item("Systemwiederherstellung", i.SystemRestoreEnabled is null ? null : !i.SystemRestoreEnabled.Value, 5,
            "Aktiv", "Deaktiviert – keine Wiederherstellungspunkte möglich");
        Item("Windows Update", i.DaysSinceLastUpdate is null ? null : i.DaysSinceLastUpdate > 45, 10,
            $"Letztes Update vor {i.DaysSinceLastUpdate} Tagen", $"Letztes Update vor {i.DaysSinceLastUpdate} Tagen");
        Item("Microsoft Defender", i.DefenderOk is null ? null : !i.DefenderOk.Value, 15, "Aktiv", "Nicht aktiv oder Problem", CheckStatus.Fail);
        Item("Laufwerke", i.DriveProblems is null ? null : i.DriveProblems > 0, 15, "Unauffällig", $"{i.DriveProblems} Problem(e)", CheckStatus.Fail);
        return (Math.Clamp(score, 0, 100), f);
    }
}
