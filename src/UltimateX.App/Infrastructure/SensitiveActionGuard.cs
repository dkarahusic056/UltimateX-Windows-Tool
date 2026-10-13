using UltimateX.Core.Logging;
using UltimateX.Services.Security;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Umsetzung des Sicherheitsablaufs <b>Authenticate → Explain → Confirm → Execute → Log</b> (§58).
/// Jede sensible Aktion (§57) läuft über <see cref="RunAsync"/>.
///  1. Authenticate: Windows Hello (PIN/Fingerabdruck/Gesicht). Ist Hello nicht eingerichtet,
///     wird das ausdrücklich angezeigt und die Bestätigung unten ist die Freigabe;
///     für Admin-Aktionen folgt zusätzlich immer UAC durch Windows selbst.
///  2. Explain + 3. Confirm: Dialog mit Beschreibung der Auswirkungen.
///  4. Execute: Aktion des Aufrufers.
///  5. Log: Security-Log mit Ergebnis.
/// </summary>
public sealed class SensitiveActionGuard
{
    public async Task<bool> RunAsync(string actionName, string explanation, Func<Task<bool>> execute, bool warning = true)
    {
        var auth = await AppServices.Auth.AuthenticateAsync($"ULTIMATE X: {actionName}");
        switch (auth)
        {
            case AuthOutcome.Cancelled:
            case AuthOutcome.Failed:
                AppLog.Security("AuthFailure", $"Aktion nicht autorisiert: {actionName}", LogLevel.Warning);
                return false;
            case AuthOutcome.NotAvailable:
                explanation += "\n\nHinweis: Windows Hello ist auf diesem PC nicht eingerichtet. Ihre Bestätigung gilt als Freigabe.";
                break;
        }

        if (!Dialogs.Confirm(actionName, explanation + "\n\nMöchten Sie fortfahren?", warning))
        {
            AppLog.Security("ActionDeclined", $"Vom Benutzer abgelehnt: {actionName}");
            return false;
        }

        bool ok;
        try
        {
            ok = await execute();
        }
        catch (Exception ex)
        {
            // Unerwartete Fehler einer Aktion dürfen die App nicht beenden (§90) – aber sie werden protokolliert und angezeigt.
            AppLog.Error(actionName, "Fehler bei der Ausführung", ex);
            Dialogs.Error(actionName, "Die Aktion ist fehlgeschlagen:\n" + ex.Message);
            ok = false;
        }
        AppLog.Security("SensitiveAction", $"{actionName}: {(ok ? "ausgeführt" : "nicht erfolgreich")}", ok ? LogLevel.Info : LogLevel.Warning);
        return ok;
    }

    /// <summary>Nur Authentifizierung (z. B. "Vollständig anzeigen" für maskierte Werte, §60).</summary>
    public async Task<bool> AuthenticateOnlyAsync(string reason)
    {
        var auth = await AppServices.Auth.AuthenticateAsync($"ULTIMATE X: {reason}");
        if (auth == AuthOutcome.Verified) return true;
        if (auth == AuthOutcome.NotAvailable)
            return Dialogs.Confirm(reason, $"Windows Hello ist auf diesem PC nicht eingerichtet.\n\nMöchten Sie „{reason}“ trotzdem bestätigen?");
        AppLog.Security("AuthFailure", $"Anzeige sensibler Daten verweigert: {reason}", LogLevel.Warning);
        return false;
    }
}
