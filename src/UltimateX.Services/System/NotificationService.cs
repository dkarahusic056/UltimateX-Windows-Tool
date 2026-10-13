using System.Runtime.InteropServices;
using System.Security;
using UltimateX.Core.Logging;
using WinXml = Windows.Data.Xml.Dom;
using WinToast = Windows.UI.Notifications;

namespace UltimateX.Services.SystemTools;

public enum NotificationKind
{
    UpdatesAvailable,
    DriversAvailable,
    LowDiskSpace,
    TemperatureWarning,
    RepairCompleted,
    SecurityAlert,
}

/// <summary>
/// Windows-Toast-Benachrichtigungen (§83). Voraussetzung für Toasts einer klassischen Desktop-App ist eine
/// Startmenü-Verknüpfung mit derselben AppUserModelID – die legt der MSI-Installer an.
/// Ohne Verknüpfung (z. B. Entwicklungs-Build) wird auf die In-App-Meldung zurückgefallen.
/// Gleichartige Meldungen werden höchstens einmal pro Stunde angezeigt (kein Spam).
/// </summary>
public sealed class NotificationService
{
    public const string AppUserModelId = "UltimateX.WindowsControl";
    private readonly Dictionary<NotificationKind, DateTime> _lastShown = new();

    public bool Enabled { get; set; } = true;

    /// <summary>Fallback, wenn Windows keinen Toast anzeigen kann (UI zeigt dann eine In-App-Meldung).</summary>
    public event Action<string, string>? FallbackRequested;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    public static void Initialize()
    {
        try { SetCurrentProcessExplicitAppUserModelID(AppUserModelId); }
        catch (EntryPointNotFoundException) { }
    }

    public void Notify(NotificationKind kind, string title, string message)
    {
        if (!Enabled) return;
        title = Core.Localization.Loc.T(title);
        message = Core.Localization.Loc.T(message);
        var now = DateTime.UtcNow;
        if (_lastShown.TryGetValue(kind, out var last) && now - last < TimeSpan.FromHours(1)) return;
        _lastShown[kind] = now;

        try
        {
            var xml = new WinXml.XmlDocument();
            xml.LoadXml(
                "<toast><visual><binding template=\"ToastGeneric\">" +
                $"<text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(message)}</text>" +
                "</binding></visual></toast>");
            WinToast.ToastNotificationManager.CreateToastNotifier(AppUserModelId).Show(new WinToast.ToastNotification(xml));
            AppLog.Info("Notification", $"{kind}: {title}");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        {
            FallbackRequested?.Invoke(title, message);
        }
    }
}
