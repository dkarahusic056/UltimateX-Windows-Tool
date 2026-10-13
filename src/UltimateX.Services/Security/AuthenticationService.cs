using UltimateX.Core.Logging;
using Windows.Security.Credentials.UI;

namespace UltimateX.Services.Security;

public enum AuthOutcome
{
    Verified,
    /// <summary>Windows Hello ist nicht eingerichtet/verfügbar – Aufrufer fällt auf explizite Bestätigung (+ UAC) zurück.</summary>
    NotAvailable,
    Cancelled,
    Failed,
}

/// <summary>
/// Sensitive Action Authentication (§56–§59).
/// - Nutzt ausschließlich Windows Hello (PIN, Fingerabdruck, Gesicht) über die offizielle WinRT-API.
/// - Die App fragt NIEMALS selbst Passwörter oder PINs ab und speichert nichts davon.
/// - Optionale Security Session (0/1/2/5 Minuten) – wird beim Sperren des PCs, Benutzerwechsel
///   oder per LOCK-Button sofort beendet.
/// </summary>
public sealed class AuthenticationService
{
    private DateTime _sessionValidUntil = DateTime.MinValue;

    public int SessionMinutes { get; set; }

    public event Action? SessionEnded;

    public bool HasActiveSession => SessionMinutes > 0 && DateTime.UtcNow < _sessionValidUntil;

    /// <summary>Authentifiziert den Benutzer für eine sensible Aktion (Schritt 1 von Authenticate → Explain → Confirm → Execute → Log).</summary>
    public async Task<AuthOutcome> AuthenticateAsync(string reason)
    {
        if (HasActiveSession) return AuthOutcome.Verified;

        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                AppLog.Security("Authentication", $"Windows Hello nicht verfügbar ({availability}) – Fallback auf Bestätigung", LogLevel.Warning);
                return AuthOutcome.NotAvailable;
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(reason);
            switch (result)
            {
                case UserConsentVerificationResult.Verified:
                    if (SessionMinutes > 0) _sessionValidUntil = DateTime.UtcNow.AddMinutes(SessionMinutes);
                    AppLog.Security("Authentication", "Windows Hello bestätigt");
                    return AuthOutcome.Verified;
                case UserConsentVerificationResult.Canceled:
                    AppLog.Security("AuthFailure", "Authentifizierung abgebrochen", LogLevel.Warning);
                    return AuthOutcome.Cancelled;
                case UserConsentVerificationResult.DeviceNotPresent:
                case UserConsentVerificationResult.NotConfiguredForUser:
                case UserConsentVerificationResult.DisabledByPolicy:
                    return AuthOutcome.NotAvailable;
                default:
                    AppLog.Security("AuthFailure", $"Authentifizierung fehlgeschlagen ({result})", LogLevel.Warning);
                    return AuthOutcome.Failed;
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            AppLog.Security("Authentication", "Windows Hello konnte nicht aufgerufen werden", LogLevel.Warning, ex.Message);
            return AuthOutcome.NotAvailable;
        }
    }

    /// <summary>LOCK-Button (§61) und Sperr-/Abmeldeereignisse (von der App über SystemEvents.SessionSwitch verdrahtet).</summary>
    public void EndSession()
    {
        var had = HasActiveSession;
        _sessionValidUntil = DateTime.MinValue;
        if (had) AppLog.Security("Session", "Security Session beendet");
        SessionEnded?.Invoke();
    }
}
