using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Updates;

public sealed record WindowsUpdateItem(
    string UpdateId,
    string Title,
    bool IsDriver,
    string? KbArticle,
    long? DownloadBytes,
    DateTime? Published,
    bool RebootMayBeRequired,
    string? DriverClass,
    string? DriverManufacturer,
    string? DriverModel,
    string? DriverProvider,
    DateTime? DriverDate,
    string? DriverHardwareId);

public sealed record UpdateInstallResult(bool Success, bool RebootRequired, string Message, string Output);

/// <summary>
/// Driver Updater (§31, §32) und Windows-Updates über die offizielle Windows Update Agent API.
///  - Quelle ist ausschließlich Windows Update (bzw. der vom Administrator konfigurierte WSUS).
///  - Treiberpakete aus Windows Update sind von Microsoft (WHQL) signiert und auf die Hardware-ID des PCs abgestimmt.
///  - Suchen funktioniert ohne Adminrechte; installiert wird je Update einzeln über den Admin-Helper (UAC),
///    nie ohne ausdrückliche Zustimmung des Benutzers.
///  - Es werden keine Drittanbieter-Treiberportale verwendet.
/// </summary>
public sealed class WindowsUpdateService
{
    public Task<(IReadOnlyList<WindowsUpdateItem> Items, string? Error)> SearchAsync(bool drivers, CancellationToken ct = default)
        => Task.Run(() => Search(drivers), ct);

    private static (IReadOnlyList<WindowsUpdateItem> Items, string? Error) Search(bool drivers)
    {
        var list = new List<WindowsUpdateItem>();
        try
        {
            var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (sessionType is null) return (list, "Windows Update Agent ist nicht verfügbar.");
            dynamic session = Activator.CreateInstance(sessionType)!;
            session.ClientApplicationID = "ULTIMATE X Windows Control";
            dynamic searcher = session.CreateUpdateSearcher();
            searcher.Online = true;
            var criteria = drivers
                ? "IsInstalled=0 and Type='Driver' and IsHidden=0"
                : "IsInstalled=0 and Type='Software' and IsHidden=0";
            dynamic result = searcher.Search(criteria);
            int count = result.Updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic u = result.Updates.Item(i);
                string id = u.Identity.UpdateID;
                string title = u.Title;
                string? kb = null;
                try { if ((int)u.KBArticleIDs.Count > 0) kb = "KB" + (string)u.KBArticleIDs.Item(0); } catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                long? size = null;
                try { size = Convert.ToInt64(u.MaxDownloadSize, CultureInfo.InvariantCulture); } catch (Exception ex) when (ex is InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                DateTime? published = null;
                try { published = (DateTime)u.LastDeploymentChangeTime; } catch (Exception ex) when (ex is InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                bool reboot = false;
                try { reboot = (int)u.InstallationBehavior.RebootBehavior != 0; } catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }

                string? cls = null, man = null, model = null, provider = null, hwid = null;
                DateTime? drvDate = null;
                if (drivers)
                {
                    // IWindowsDriverUpdate-Eigenschaften (nur bei Treiber-Updates vorhanden)
                    try
                    {
                        cls = u.DriverClass;
                        man = u.DriverManufacturer;
                        model = u.DriverModel;
                        provider = u.DriverProvider;
                        hwid = u.DriverHardwareID;
                        drvDate = (DateTime)u.DriverVerDate;
                    }
                    catch (Exception ex) when (ex is InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                }

                list.Add(new WindowsUpdateItem(id, title, drivers, kb, size, published, reboot, cls, man, model, provider, drvDate, hwid));
            }
            AppLog.Info("WindowsUpdate", $"Suche nach {(drivers ? "Treiber" : "Software")}-Updates", $"{list.Count} gefunden");
            return (list, null);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            AppLog.Warn("WindowsUpdate", "Suche fehlgeschlagen", $"0x{ex.HResult:X8}");
            return (list, $"Windows Update-Suche fehlgeschlagen (0x{ex.HResult:X8}). Internetverbindung und Windows-Update-Dienst prüfen.");
        }
    }

    /// <summary>Installiert genau ein Update über den Admin-Helper.</summary>
    public async Task<UpdateInstallResult> InstallAsync(WindowsUpdateItem item, IProgress<string>? progress, CancellationToken ct = default)
    {
        AppLog.Security(item.IsDriver ? "DriverInstall" : "AdminOperation", $"Installation angefordert: {item.Title}");
        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.InstallWindowsUpdate, item.UpdateId, progress, ct).ConfigureAwait(false);
        if (r.Cancelled) return new UpdateInstallResult(false, false, "Die Administratorfreigabe wurde abgelehnt.", r.Output);
        var reboot = r.Output.Contains("\"rebootRequired\":true", StringComparison.Ordinal);
        var ok = r.ExitCode == 0;
        AppLog.Security(item.IsDriver ? "DriverInstall" : "AdminOperation", $"{item.Title}: {(ok ? "installiert" : "fehlgeschlagen")}",
            ok ? LogLevel.Info : LogLevel.Warning, $"ExitCode={r.ExitCode}, Neustart={reboot}");
        return new UpdateInstallResult(ok, reboot, ok ? (reboot ? "Installiert – Neustart erforderlich." : "Installiert.") : "Installation fehlgeschlagen.", r.Output);
    }
}
