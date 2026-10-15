using System.Globalization;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Power;

/// <summary>Zeitgesteuerte Energieaktion (v1.3).</summary>
public enum ScheduledPowerAction
{
    Shutdown,
    Restart,
    Lock,
    SignOut,
    Sleep,
}

/// <summary>Einheit für „in X …“.</summary>
public enum PowerDelayUnit
{
    Minutes,
    Hours,
}

/// <summary>Stufe der Vorwarnung vor einer geplanten Energieaktion.</summary>
public enum PowerWarningStage
{
    None,
    FiveMinutes,
    OneMinute,
}

/// <summary>
/// Eine geplante Energieaktion. Wird in %LOCALAPPDATA%\UltimateX\power-schedule.json gespeichert, damit die Anzeige
/// nach einem Neustart der App stimmt. <see cref="SystemScheduled"/> = über shutdown.exe /t geplant (läuft auch ohne App weiter).
/// </summary>
public sealed record PowerScheduleEntry(Guid Id, ScheduledPowerAction Action, DateTime DueUtc, DateTime CreatedUtc, bool SystemScheduled)
{
    /// <summary>Herunterfahren/Neustart laufen über Windows (shutdown.exe), alles andere über den App-internen Timer.</summary>
    public static bool UsesShutdownExe(ScheduledPowerAction action) => action is ScheduledPowerAction.Shutdown or ScheduledPowerAction.Restart;
}

/// <summary>
/// Reine Zeitlogik für geplante Energieaktionen (testbar, ohne Systemzugriff):
/// Uhrzeit → Fälligkeit in UTC (Mitternacht, Sommer-/Winterzeit), Sekunden für shutdown.exe /t, Verschieben, Vorwarnungen.
/// </summary>
public static partial class PowerScheduleMath
{
    /// <summary>Obergrenze für /t: 7 Tage.</summary>
    public const long MaxSeconds = 604_800;

    /// <summary>Höchstwerte für „in X Minuten/Stunden“ (entspricht 7 Tagen).</summary>
    public const int MaxMinutes = 10_080;
    public const int MaxHours = 168;

    [GeneratedRegex(@"^([01]?[0-9]|2[0-3]):([0-5][0-9])$", RegexOptions.CultureInvariant)]
    private static partial Regex ClockRegex();

    public static bool IsValidSeconds(long seconds) => seconds is >= 0 and <= MaxSeconds;

    /// <summary>Streng: nur „H:mm“ bzw. „HH:mm“ (00:00–23:59), keine Leerzeichen, keine Sekunden.</summary>
    public static bool TryParseClock(string? text, out int hour, out int minute)
    {
        hour = minute = 0;
        if (text is null) return false;
        var m = ClockRegex().Match(text);
        if (!m.Success) return false;
        hour = int.Parse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        minute = int.Parse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>„in X Minuten/Stunden“ → Sekunden (1 Minute … 7 Tage). false bei ungültiger Eingabe.</summary>
    public static bool TryDelaySeconds(int amount, PowerDelayUnit unit, out long seconds)
    {
        seconds = 0;
        if (amount < 1) return false;
        if (unit == PowerDelayUnit.Minutes && amount > MaxMinutes) return false;
        if (unit == PowerDelayUnit.Hours && amount > MaxHours) return false;
        seconds = unit == PowerDelayUnit.Hours ? amount * 3600L : amount * 60L;
        return IsValidSeconds(seconds);
    }

    /// <summary>
    /// „um HH:mm heute/morgen“ → Fälligkeit in UTC.
    ///  - Liegt die Uhrzeit wegen der Zeitumstellung in einer Lücke (z. B. 02:30 bei Beginn der Sommerzeit), wird der erste
    ///    gültige Zeitpunkt danach gewählt (03:00).
    ///  - Ist sie doppelt vorhanden (Ende der Sommerzeit), gilt das erste Auftreten.
    ///  - „heute“ in der Vergangenheit → Fehler (stattdessen „morgen“ wählen); nie stillschweigend verschieben.
    /// </summary>
    public static bool TryResolveClock(DateTime nowUtc, TimeZoneInfo zone, int hour, int minute, bool tomorrow,
        out DateTime dueUtc, out string? error)
    {
        dueUtc = default;
        error = null;
        if (hour is < 0 or > 23 || minute is < 0 or > 59) { error = "Ungültige Uhrzeit."; return false; }
        nowUtc = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var date = localNow.Date.AddDays(tomorrow ? 1 : 0);
        var local = DateTime.SpecifyKind(date.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            // Erste gültige Minute nach der Lücke (z. B. 02:30 → 03:00); Umstellungen dauern höchstens wenige Stunden.
            var probe = local;
            for (var i = 0; i < 24 * 60 && zone.IsInvalidTime(probe); i++) probe = probe.AddMinutes(1);
            local = probe;
        }

        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var maxOffset = offsets.Max();
            dueUtc = DateTime.SpecifyKind(local - maxOffset, DateTimeKind.Utc); // erstes Auftreten (noch Sommerzeit)
        }
        else
        {
            dueUtc = TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        if (dueUtc <= nowUtc)
        {
            error = tomorrow ? "Die Uhrzeit liegt in der Vergangenheit." : "Die Uhrzeit ist heute bereits vorbei – bitte „morgen“ wählen.";
            return false;
        }
        if ((dueUtc - nowUtc).TotalSeconds > MaxSeconds)
        {
            error = "Höchstens 7 Tage im Voraus.";
            return false;
        }
        return true;
    }

    /// <summary>Sekunden bis zur Fälligkeit, aufgerundet (für shutdown.exe /t). Bereits fällig → 0. Über 7 Tage → false.</summary>
    public static bool TrySecondsUntil(DateTime dueUtc, DateTime nowUtc, out long seconds)
    {
        var diff = (dueUtc - nowUtc).TotalSeconds;
        seconds = diff <= 0 ? 0 : (long)Math.Ceiling(diff);
        return IsValidSeconds(seconds);
    }

    /// <summary>Verschieben (+15 min / +1 h …) ab der bisherigen Fälligkeit; liegt diese schon zurück, ab jetzt.</summary>
    public static bool TryPostpone(DateTime dueUtc, TimeSpan delta, DateTime nowUtc, out DateTime newDueUtc)
    {
        var baseTime = dueUtc > nowUtc ? dueUtc : nowUtc;
        newDueUtc = baseTime + delta;
        return delta > TimeSpan.Zero && (newDueUtc - nowUtc).TotalSeconds <= MaxSeconds;
    }

    /// <summary>
    /// Welche Vorwarnung ist jetzt fällig? Jede Stufe wird nur einmal gemeldet; wurde die 5-Minuten-Warnung verpasst
    /// (z. B. Planung nur 3 Minuten im Voraus), kommt nur noch die 1-Minuten-Warnung.
    /// </summary>
    public static PowerWarningStage WarningDue(TimeSpan remaining, bool warnedFive, bool warnedOne)
    {
        if (remaining <= TimeSpan.Zero) return PowerWarningStage.None;
        if (remaining <= TimeSpan.FromMinutes(1)) return warnedOne ? PowerWarningStage.None : PowerWarningStage.OneMinute;
        if (remaining <= TimeSpan.FromMinutes(5)) return warnedFive ? PowerWarningStage.None : PowerWarningStage.FiveMinutes;
        return PowerWarningStage.None;
    }

    /// <summary>Anfangszustand der Warnflags beim Planen: Stufen, deren Zeitpunkt schon vorbei ist, gelten als erledigt.</summary>
    public static (bool WarnedFive, bool WarnedOne) InitialWarnings(TimeSpan remaining)
        => (remaining <= TimeSpan.FromMinutes(5), remaining <= TimeSpan.FromMinutes(1));

    /// <summary>Restzeit als „hh:mm:ss“ (Stunden über 24 werden fortgezählt, z. B. „49:05:00“).</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var total = (long)Math.Floor(remaining.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return string.Create(CultureInfo.InvariantCulture, $"{h:00}:{m:00}:{s:00}");
    }

    /// <summary>
    /// Gespeicherte Planung beim Start der App bewerten: shutdown.exe-Planungen nur anzeigen, solange sie in der Zukunft liegen;
    /// App-interne Aktionen, die verpasst wurden (App war geschlossen), werden NICHT nachgeholt.
    /// </summary>
    public static bool IsStillActive(PowerScheduleEntry entry, DateTime nowUtc) => entry.DueUtc > nowUtc;

    public static string ActionLabel(ScheduledPowerAction action) => action switch
    {
        ScheduledPowerAction.Shutdown => "Herunterfahren",
        ScheduledPowerAction.Restart => "Neustart",
        ScheduledPowerAction.Lock => "Sperren",
        ScheduledPowerAction.SignOut => "Abmelden",
        ScheduledPowerAction.Sleep => "Energiesparmodus",
        _ => action.ToString(),
    };
}
