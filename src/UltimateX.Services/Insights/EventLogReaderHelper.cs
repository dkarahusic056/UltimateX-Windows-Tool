using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Insights;

/// <summary>Ein gelesenes Ereignis (nur die benötigten Felder).</summary>
internal sealed record LogEvent(DateTime Time, long Id, string Provider, IReadOnlyList<string> Props, string? Message);

/// <summary>Lesender Zugriff auf Windows-Ereignisprotokolle (für Standardbenutzer lesbar: System, Application).</summary>
internal static class EventLogReaderHelper
{
    /// <summary>XPath-Filter: Anbieter + IDs + Zeitraum (Millisekunden) – null = ohne Zeitgrenze.</summary>
    public static string Query(string provider, IEnumerable<int> ids, TimeSpan? within)
    {
        var idCond = string.Join(" or ", ids.Select(i => "EventID=" + i.ToString(CultureInfo.InvariantCulture)));
        var time = within is { } w ? $" and TimeCreated[timediff(@SystemTime) <= {(long)w.TotalMilliseconds}]" : "";
        return $"*[System[Provider[@Name='{provider}'] and ({idCond}){time}]]";
    }

    /// <summary>Liest bis zu <paramref name="max"/> Ereignisse (neueste zuerst). Fehler → leere Liste + Logeintrag.</summary>
    public static List<LogEvent> Read(string log, string xpath, int max, bool withMessage = false, CancellationToken ct = default)
    {
        var list = new List<LogEvent>();
        try
        {
            var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var i = 0; i < max; i++)
            {
                ct.ThrowIfCancellationRequested();
                using var rec = reader.ReadEvent();
                if (rec is null) break;
                var props = rec.Properties.Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture) ?? "").ToList();
                string? msg = null;
                if (withMessage)
                {
                    try { msg = rec.FormatDescription(); }
                    catch (EventLogException) { msg = null; }
                }
                list.Add(new LogEvent(rec.TimeCreated ?? DateTime.MinValue, rec.Id, rec.ProviderName ?? "", props, msg));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Warn("Insights", $"Protokoll '{log}' nicht lesbar", ex.Message);
        }
        return list;
    }

    /// <summary>Zeitpunkt des ältesten Ereignisses im Protokoll (= wie weit es zurückreicht), null wenn nicht lesbar.</summary>
    public static DateTime? Oldest(string log)
    {
        try
        {
            var query = new EventLogQuery(log, PathType.LogName, "*") { ReverseDirection = false };
            using var reader = new EventLogReader(query);
            using var rec = reader.ReadEvent();
            return rec?.TimeCreated;
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
