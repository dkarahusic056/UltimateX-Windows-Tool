using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using UltimateX.App.Infrastructure;
using UltimateX.App.ViewModels;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Logging;
using UltimateX.Core.Settings;
using UltimateX.Services.Security;
using UltimateX.Services.SystemTools;

namespace UltimateX.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Globale Fehlerbehandlung: Fehler werden protokolliert und angezeigt, die App läuft weiter (§90).
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog.Error("Unhandled", "Unbeobachtete Task-Ausnahme", args.Exception);
            args.SetObserved();
        };

        AppLog.ApplyRetention();
        AppLog.Info("App", $"Start (Version {typeof(App).Assembly.GetName().Version}, {(Elevation.IsElevated ? "erhöht" : "Standardbenutzer")})");

        // Einstellungen und Oberflächensprache zuerst, damit auch frühe Meldungen übersetzt sind (§84).
        AppServices.Settings = SettingsStore.Load();
        Loc.Initialize(AppServices.Settings.Language);
        Thread.CurrentThread.CurrentCulture = Loc.Culture;
        Thread.CurrentThread.CurrentUICulture = Loc.Culture;
        CultureInfo.DefaultThreadCurrentCulture = Loc.Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Loc.Culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(Loc.Culture.IetfLanguageTag)));
        UiTranslator.Install();

        // Application Integrity (§62): Manipulierte (signiert, aber verändert) Dateien → Start abbrechen.
        var integrity = SecurityService.GetAppIntegrity();
        if (integrity.State == DataState.Problem)
        {
            MessageBox.Show(
                Loc.T("Die Programmdateien von ULTIMATE X wurden verändert (ungültige Signatur):\n" + integrity.Detail +
                "\n\nAus Sicherheitsgründen wird die Anwendung nicht gestartet. Bitte neu installieren."),
                Loc.T("ULTIMATE X – Integritätsprüfung"), MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(2);
            return;
        }

        ThemeManager.Apply(AppServices.Settings.Theme);

        // Security Session endet sofort bei PC-Sperre, Abmeldung oder Benutzerwechsel (§59).
        SystemEvents.SessionSwitch += (_, args) =>
        {
            if (args.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff
                or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
                AppServices.Auth.EndSession();
        };

        // Benachrichtigungen (§83): AppUserModelID muss vor dem ersten Fenster gesetzt sein.
        NotificationService.Initialize();
        AppServices.Notifications.Enabled = AppServices.Settings.NotificationsEnabled;
        CommandHistory.Start();
        _ = Task.Run(() => AppServices.History.Prune());

        MonitorHub.Start();

        var main = new MainViewModel();
        AppServices.Notifications.FallbackRequested += (title, text) => Dispatcher.InvokeAsync(() => main.ShowToast($"{title}: {text}"));
        var window = new MainWindow(main);
        MainWindow = window;

        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (startMinimized) window.WindowState = WindowState.Minimized;
        window.Show();

        if (!AppServices.Settings.FirstStartCompleted)
        {
            // First Start (§99): kurze Erklärung; die Erkennung läuft bereits im Dashboard.
            Dialogs.Info("Willkommen bei ULTIMATE X",
                "ULTIMATE X erkennt jetzt Ihr System, Ihre Hardware, Netzwerkadapter, installierte Software, Autostart-Einträge sowie den Sicherheits- und Windows-Status.\n\n" +
                "• Die App läuft ohne Administratorrechte. Für Reparaturen fragt Windows gezielt per UAC.\n" +
                "• Sensible Daten (MAC, IP, Seriennummern) werden maskiert.\n" +
                "• Keine Telemetrie. Netzwerkzugriffe gibt es nur für die Update-Prüfung (Windows Update, winget – abschaltbar in den Einstellungen) " +
                "sowie wenn Sie selbst einen Speedtest starten oder die öffentliche IP abfragen.");
            AppServices.Settings.FirstStartCompleted = true;
            AppServices.SaveSettings();
        }

        // Update-Prüfung im Hintergrund (Windows Update, winget, ULTIMATE X) – verzögert, damit der Start schnell bleibt.
        if (AppServices.Settings.AutoCheckAppUpdates)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(45));
                try { await main.Page<UpdatesViewModel>().EnsureLoadedAsync(); }
                catch (Exception ex) { AppLog.Warn("Updates", "Hintergrundprüfung fehlgeschlagen", ex.Message); }
            });
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Error("Unhandled", "Unerwarteter Fehler in der Oberfläche", e.Exception);
        MessageBox.Show(Loc.T("Ein unerwarteter Fehler ist aufgetreten und wurde protokolliert:\n\n") + e.Exception.Message,
            "ULTIMATE X", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MonitorHub.Stop();
        AppServices.Monitor.Dispose();
        AppLog.Info("App", "Beendet");
        base.OnExit(e);
    }
}
