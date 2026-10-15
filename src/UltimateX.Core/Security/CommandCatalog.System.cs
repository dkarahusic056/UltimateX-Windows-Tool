using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Power;

namespace UltimateX.Core.Security;

/// <summary>
/// v1.3 Zeitgesteuerte Energieaktionen: Herunterfahren/Neustart über shutdown.exe mit /t (0 … 604800 Sekunden = 7 Tage).
/// Bewusst OHNE /c-Kommentar (kein Benutzertext in Befehlszeilen). Abbrechen über den vorhandenen Eintrag AbortShutdown (/a).
/// Hinweis: Bei /t &gt; 0 setzt Windows /f (Programme werden zwangsweise geschlossen) automatisch voraus – die UI warnt davor.
/// </summary>
public static partial class CommandCatalog
{
    private static CommandSpec? BuildSystemCommand(AllowedCommand id, string? parameter) => id switch
    {
        AllowedCommand.ShutdownAt => new(id, Path.Combine(AppPaths.System32, "shutdown.exe"), ["/s", "/t", ScheduleSeconds(parameter)], false, false, "Herunterfahren (geplant)"),
        AllowedCommand.RestartAt => new(id, Path.Combine(AppPaths.System32, "shutdown.exe"), ["/r", "/t", ScheduleSeconds(parameter)], false, false, "Neustart (geplant)"),
        _ => null,
    };

    /// <summary>Sekunden 0–604800, nur ASCII-Ziffern (kein Vorzeichen, keine Leerzeichen, keine Tausendertrennzeichen).</summary>
    internal static string ScheduleSeconds(string? parameter)
    {
        if (parameter is null || parameter.Length is 0 or > 6 || !parameter.All(char.IsAsciiDigit)
            || !long.TryParse(parameter, NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            || !PowerScheduleMath.IsValidSeconds(s))
            throw new ArgumentException("Zeitpunkt muss zwischen 0 und 604800 Sekunden (7 Tage) liegen.", nameof(parameter));
        return s.ToString(CultureInfo.InvariantCulture);
    }
}
