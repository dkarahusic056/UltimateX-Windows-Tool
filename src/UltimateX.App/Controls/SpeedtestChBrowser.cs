using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.App.Controls;

/// <summary>
/// Eingebettetes speedtest.ch (v1.3) auf Basis von Microsoft Edge WebView2 – kein externer Browser.
///
/// Schutzmaßnahmen:
///  - Navigation nur zu https://speedtest.ch bzw. *.speedtest.ch (<see cref="EmbeddedBrowserPolicy"/>), alles andere wird abgebrochen
///    und als Hinweis angezeigt; keine Pop-ups (NewWindowRequested → Handled), keine Downloads (DownloadStarting → Cancel).
///  - Kennwort-/Formular-Autofill, Statusleiste, Entwicklertools, Kontextmenü, Host-Objekte und Web-Messages aus;
///    Berechtigungsanfragen (Standort, Kamera, Mikrofon, Benachrichtigungen …) werden abgelehnt.
///  - Eigener Benutzerdatenordner %LOCALAPPDATA%\UltimateX\WebView2 (getrennt vom Edge-Profil des Benutzers).
///  - Die Webseite wird NICHT ausgelesen; Ergebnisse überträgt der Benutzer selbst.
/// Die Seite wird erst geladen, wenn <see cref="IsActive"/> true ist (Bereich gewählt) – sonst kein Internetzugriff.
/// </summary>
public sealed class SpeedtestChBrowser : ContentControl
{
    public const string RuntimeDownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive), typeof(bool), typeof(SpeedtestChBrowser), new PropertyMetadata(false, (d, _) => ((SpeedtestChBrowser)d).TryInitialize()));

    public bool IsActive { get => (bool)GetValue(IsActiveProperty); set => SetValue(IsActiveProperty, value); }

    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _noticeBar = new() { Padding = new Thickness(12, 6, 12, 6), Visibility = Visibility.Collapsed };
    private readonly ContentControl _host = new() { Focusable = false };
    private WebView2? _web;
    private bool _initializing;

    public SpeedtestChBrowser()
    {
        Focusable = false;
        _noticeBar.SetResourceReference(Border.BackgroundProperty, "Brush.AccentSoft");
        _noticeBar.Child = _notice;

        var home = new Button { Content = Loc.T("Startseite"), Margin = new Thickness(0, 0, 8, 0) };
        home.Click += (_, _) => Navigate(EmbeddedBrowserPolicy.SpeedtestChHome);
        var reload = new Button { Content = Loc.T("Neu laden") };
        reload.Click += (_, _) => _web?.CoreWebView2?.Reload();
        var info = new TextBlock
        {
            Text = Loc.T("speedtest.ch – eingebettet, nur Seiten von speedtest.ch erlaubt"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        info.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        toolbar.Children.Add(home);
        toolbar.Children.Add(reload);
        toolbar.Children.Add(info);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_noticeBar, 1);
        Grid.SetRow(_host, 2);
        _noticeBar.Margin = new Thickness(0, 0, 0, 8);
        grid.Children.Add(toolbar);
        grid.Children.Add(_noticeBar);
        grid.Children.Add(_host);
        Content = grid;

        Loaded += (_, _) => TryInitialize();
        Unloaded += (_, _) => Teardown();
    }

    private void ShowNotice(string? text)
    {
        _notice.Text = text is null ? "" : Loc.T(text);
        _noticeBar.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void TryInitialize()
    {
        if (!IsActive || !IsLoaded || _web is not null || _initializing) return;
        _initializing = true;
        try
        {
            string? version = null;
            try { version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (WebView2RuntimeNotFoundException) { }
            if (string.IsNullOrEmpty(version))
            {
                ShowRuntimeMissing();
                return;
            }

            var dataFolder = Path.Combine(AppPaths.UserDataRoot, "WebView2");
            Directory.CreateDirectory(dataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, dataFolder, new CoreWebView2EnvironmentOptions());
            if (!IsLoaded) return; // Seite inzwischen verlassen

            var web = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
            _host.Content = web;
            _web = web;
            await web.EnsureCoreWebView2Async(env);
            if (!ReferenceEquals(_web, web)) return;

            var core = web.CoreWebView2;
            var s = core.Settings;
            s.IsPasswordAutosaveEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsStatusBarEnabled = false;
            s.AreDevToolsEnabled = false;
            s.AreDefaultContextMenusEnabled = false;
            s.AreHostObjectsAllowed = false;
            s.IsWebMessageEnabled = false;
            s.AreDefaultScriptDialogsEnabled = true;

            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true; // kein Pop-up, kein neues Fenster
                ShowNotice(EmbeddedBrowserPolicy.IsAllowedSpeedtestCh(e.Uri)
                    ? "Ein neues Fenster wurde blockiert. Der Test läuft in diesem Bereich."
                    : "Ein Pop-up bzw. ein fremder Link wurde blockiert (nur speedtest.ch ist erlaubt).");
            };
            core.DownloadStarting += (_, e) =>
            {
                e.Cancel = true;
                e.Handled = true;
                ShowNotice("Ein Download wurde blockiert. Im eingebetteten Fenster sind keine Downloads möglich.");
            };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.Navigate(EmbeddedBrowserPolicy.SpeedtestChHome);
            AppLog.Info("Speedtest", "speedtest.ch eingebettet geöffnet", "WebView2 " + version);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException)
        {
            ShowRuntimeMissing();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            AppLog.Warn("Speedtest", "WebView2 konnte nicht gestartet werden", ex.Message);
            ShowNotice("Das eingebettete Browserfenster konnte nicht gestartet werden: " + ex.Message);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (EmbeddedBrowserPolicy.IsAllowedSpeedtestCh(e.Uri))
        {
            ShowNotice(null);
            return;
        }
        e.Cancel = true;
        var host = Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) ? u.Host : "unbekannt";
        ShowNotice($"Link zu „{host}“ blockiert – im eingebetteten Fenster sind nur Seiten von speedtest.ch erlaubt.");
        AppLog.Info("Speedtest", "Navigation außerhalb von speedtest.ch blockiert", host);
    }

    private void Navigate(string url)
    {
        if (_web?.CoreWebView2 is { } core && EmbeddedBrowserPolicy.IsAllowedSpeedtestCh(url)) core.Navigate(url);
        else TryInitialize();
    }

    private void ShowRuntimeMissing()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0), MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Die Microsoft Edge WebView2-Runtime ist nicht installiert. Sie wird für das eingebettete speedtest.ch benötigt und ist bei Windows 11 normalerweise vorhanden."),
            TextWrapping = TextWrapping.Wrap,
        });
        var button = new Button { Content = Loc.T("WebView2-Runtime bei Microsoft herunterladen"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        button.Click += (_, _) => UriLauncher.Open(new Uri(RuntimeDownloadUrl));
        panel.Children.Add(button);
        _host.Content = panel;
    }

    private void Teardown()
    {
        if (_web is null) return;
        try
        {
            if (_web.CoreWebView2 is { } core) core.NavigationStarting -= OnNavigationStarting;
            _web.Dispose();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ObjectDisposedException)
        {
            AppLog.Warn("Speedtest", "WebView2 konnte nicht freigegeben werden", ex.Message);
        }
        _web = null;
        _host.Content = null;
        ShowNotice(null);
    }
}
