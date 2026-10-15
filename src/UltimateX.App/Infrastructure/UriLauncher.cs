using System.ComponentModel;
using System.Diagnostics;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Öffnet Adressen im Standardbrowser bzw. Windows-Einstellungsseiten (v1.2 §15, §16, §22). Erlaubt sind ausschließlich
/// HTTPS-Adressen und ms-settings:-Seiten; die Adresse wird vollständig im Code erzeugt (Suchtext URL-kodiert).
/// Es wird nichts heruntergeladen oder ausgewertet.
/// </summary>
public static class UriLauncher
{
    public static OperationResult Open(Uri uri)
    {
        if (!(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "ms-settings"))
            return OperationResult.Fail("Diese Adresse wird aus Sicherheitsgründen nicht geöffnet.");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AppLog.Info("Browser", uri.Scheme == "ms-settings" ? "Einstellungen geöffnet" : "Adresse im Standardbrowser geöffnet", uri.Host);
            return OperationResult.Ok("Geöffnet.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return OperationResult.Fail("Konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    public static OperationResult OpenSettings(string page) => Open(new Uri("ms-settings:" + page));

    /// <summary>Öffnet das mitgelieferte Benutzerhandbuch (PDF aus dem Installationsordner, fester Dateiname) im Standard-PDF-Programm.</summary>
    public static OperationResult OpenManual()
    {
        var name = UltimateX.Core.Localization.Loc.IsEnglish ? "ULTIMATE X Manual (EN).pdf" : "ULTIMATE X Handbuch.pdf";
        var path = Path.Combine(AppContext.BaseDirectory, "Docs", name);
        if (!File.Exists(path)) return OperationResult.Fail("Das Handbuch wurde nicht gefunden.");
        try
        {
            using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AppLog.Info("Hilfe", "Handbuch geöffnet", name);
            return OperationResult.Ok("Handbuch geöffnet.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return OperationResult.Fail("Konnte nicht geöffnet werden: " + ex.Message);
        }
    }
}
