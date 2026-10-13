using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Common;
using UltimateX.Services.SystemTools;
using UltimateX.Services.Updates;

namespace UltimateX.App.ViewModels;

public sealed partial class UpdateRow(WindowsUpdateItem item) : ObservableObject
{
    public WindowsUpdateItem Item { get; } = item;
    public string Title => Item.Title;
    public string Detail => Item.IsDriver
        ? $"{Item.DriverClass ?? "Treiber"} · {Item.DriverManufacturer ?? Item.DriverProvider ?? "?"} · Treiberdatum {Item.DriverDate:dd.MM.yyyy}"
        : $"{Item.KbArticle ?? "Update"} · veröffentlicht {Item.Published:dd.MM.yyyy}";
    public string Size => Item.DownloadBytes is > 0 ? Format.Bytes(Item.DownloadBytes.Value) : "–";
    [ObservableProperty] private string _status = "Verfügbar";
    [ObservableProperty] private bool _isInstalling;
}

public sealed partial class SoftwareRow(SoftwareUpdate update) : ObservableObject
{
    public SoftwareUpdate Update { get; } = update;
    public string Name => Update.Name;
    public string Id => Update.Id;
    public string Versions => $"{Update.InstalledVersion} → {Update.AvailableVersion}";
    public string Source => Update.Source;
    [ObservableProperty] private string _status = "Verfügbar";
    [ObservableProperty] private bool _isInstalling;
}

/// <summary>Driver Updater (§31, §32), Software Update Manager (§33) und sichere App-Updates (§64).</summary>
public sealed partial class UpdatesViewModel : PageViewModel
{
    public override string Title => "Updates";
    public override string Glyph => "\uE895";
    public override string Keywords => "updates update treiber driver software winget windows update aktualisieren version neue";

    public ObservableCollection<UpdateRow> DriverUpdates { get; } = [];
    public ObservableCollection<UpdateRow> WindowsUpdates { get; } = [];
    public ObservableCollection<SoftwareRow> SoftwareUpdates { get; } = [];

    [ObservableProperty] private bool _isSearchingWindows;
    [ObservableProperty] private bool _isSearchingSoftware;
    [ObservableProperty] private string? _windowsError;
    [ObservableProperty] private string? _softwareError;
    [ObservableProperty] private string? _lastChecked;
    [ObservableProperty] private string _output = string.Empty;
    [ObservableProperty] private string? _appUpdateText;
    [ObservableProperty] private bool _appUpdateAvailable;
    [ObservableProperty] private double _appUpdateProgress;

    private AppUpdateManifest? _manifest;

    /// <summary>Anzahl gefundener Treiber-/Software-Updates (für Dashboard und Benachrichtigungen).</summary>
    public static int? DriverCount { get; private set; }
    public static int? SoftwareCount { get; private set; }
    public static IReadOnlyList<WindowsUpdateItem> LastDriverUpdates { get; private set; } = [];
    public static event Action? CountsChanged;

    internal static void SetDriverUpdates(IReadOnlyList<WindowsUpdateItem> items)
    {
        LastDriverUpdates = items;
        DriverCount = items.Count;
        CountsChanged?.Invoke();
    }

    protected override async Task LoadAsync()
    {
        await Task.WhenAll(SearchWindowsAsync(), SearchSoftwareAsync(), CheckAppUpdateAsync());
        LastChecked = $"Zuletzt geprüft: {DateTime.Now:dd.MM.yyyy HH:mm}";
        if ((DriverCount ?? 0) + (SoftwareCount ?? 0) > 0)
            AppServices.Notifications.Notify(NotificationKind.UpdatesAvailable, "Updates verfügbar",
                $"{DriverCount ?? 0} Treiber-Update(s), {SoftwareCount ?? 0} Software-Update(s) gefunden.");
    }

    private async Task SearchWindowsAsync()
    {
        IsSearchingWindows = true;
        WindowsError = null;
        try
        {
            var drivers = await AppServices.WindowsUpdate.SearchAsync(true);
            var software = await AppServices.WindowsUpdate.SearchAsync(false);
            DriverUpdates.Clear();
            foreach (var u in drivers.Items) DriverUpdates.Add(new UpdateRow(u));
            WindowsUpdates.Clear();
            foreach (var u in software.Items) WindowsUpdates.Add(new UpdateRow(u));
            WindowsError = drivers.Error ?? software.Error;
            if (drivers.Error is null) SetDriverUpdates(drivers.Items);
            else CountsChanged?.Invoke();
        }
        finally { IsSearchingWindows = false; }
    }

    private async Task SearchSoftwareAsync()
    {
        IsSearchingSoftware = true;
        SoftwareError = null;
        try
        {
            var (updates, error) = await AppServices.Winget.ListUpgradesAsync();
            SoftwareUpdates.Clear();
            foreach (var u in updates) SoftwareUpdates.Add(new SoftwareRow(u));
            SoftwareError = error;
            if (error is null) SoftwareCount = updates.Count;
            CountsChanged?.Invoke();
        }
        finally { IsSearchingSoftware = false; }
    }

    [RelayCommand] private Task SearchWindowsUpdatesAsync() => SearchWindowsAsync();
    [RelayCommand] private Task SearchSoftwareUpdatesAsync() => SearchSoftwareAsync();

    /// <summary>Treiber-/Windows-Update installieren: Authenticate → Explain → Confirm → (Restore Point) → Execute → Log.</summary>
    [RelayCommand]
    private async Task InstallUpdateAsync(UpdateRow? row)
    {
        if (row is null || row.IsInstalling) return;
        var it = row.Item;
        var explanation = new StringBuilder()
            .AppendLine($"„{it.Title}“ wird über Windows Update heruntergeladen und installiert.")
            .AppendLine()
            .AppendLine("Quelle: Microsoft Windows Update (von Microsoft signiert, für diesen PC freigegeben).");
        if (it.IsDriver)
            explanation.AppendLine($"Gerät: {it.DriverModel ?? "?"} · Hersteller: {it.DriverManufacturer ?? "?"} · Hardware-ID: {it.DriverHardwareId ?? "?"}");
        if (AppServices.Settings.AutoRestorePoint)
            explanation.AppendLine("Vorher wird ein Windows-Wiederherstellungspunkt angefordert.");
        explanation.AppendLine("Mit „Ja“ stimmen Sie ggf. den Lizenzbedingungen des Updates zu. Windows fragt nach Administratorrechten (UAC).");
        if (it.RebootMayBeRequired) explanation.AppendLine("Ein Neustart kann erforderlich sein.");

        await AppServices.Guard.RunAsync(it.IsDriver ? "Treiber installieren" : "Windows-Update installieren", explanation.ToString(), async () =>
        {
            row.IsInstalling = true;
            row.Status = "Wird installiert …";
            Output = string.Empty;
            try
            {
                if (AppServices.Settings.AutoRestorePoint)
                {
                    var rp = await AppServices.RestorePoints.CreateAsync();
                    Output += rp.Message + "\n";
                }
                var progress = new Progress<string>(s => Output += s);
                var result = await AppServices.WindowsUpdate.InstallAsync(it, progress);
                row.Status = result.Message;
                if (result.Success && result.RebootRequired)
                    AppServices.Notifications.Notify(NotificationKind.RepairCompleted, "Neustart erforderlich", $"{it.Title} wurde installiert.");
                return result.Success;
            }
            finally { row.IsInstalling = false; }
        });
    }

    [RelayCommand]
    private async Task UpgradeSoftwareAsync(SoftwareRow? row)
    {
        if (row is null || row.IsInstalling) return;
        if (!Dialogs.Confirm("Software aktualisieren",
                $"„{row.Name}“ ({row.Versions}) wird über winget aus der Quelle „{row.Source}“ aktualisiert.\n\n" +
                "Mit „Ja“ akzeptieren Sie die Paket- und Quellvereinbarungen von winget. Installer, die Adminrechte benötigen, fragen per UAC nach.\n\nFortfahren?"))
            return;
        await UpgradeAsync(row);
    }

    [RelayCommand]
    private async Task UpgradeAllSoftwareAsync()
    {
        var rows = SoftwareUpdates.Where(r => !r.IsInstalling && r.Status == "Verfügbar").ToList();
        if (rows.Count == 0) return;
        if (!Dialogs.Confirm("Alle Software-Updates", $"{rows.Count} Programme nacheinander über winget aktualisieren?\n\n" +
                "Mit „Ja“ akzeptieren Sie die Paket- und Quellvereinbarungen von winget."))
            return;
        foreach (var r in rows) await UpgradeAsync(r);
    }

    private async Task UpgradeAsync(SoftwareRow row)
    {
        row.IsInstalling = true;
        row.Status = "Wird aktualisiert …";
        Output = string.Empty;
        try
        {
            var r = await AppServices.Winget.UpgradeAsync(row.Id, new Progress<string>(s => Output += s));
            row.Status = r.ExitCode == 0 ? "Aktualisiert" : $"Fehlgeschlagen (Code {r.ExitCode})";
        }
        catch (ArgumentException)
        {
            row.Status = "Ungültige Paket-ID";
        }
        finally { row.IsInstalling = false; }
    }

    // ---------------- App-Update (§64) ----------------

    private async Task CheckAppUpdateAsync()
    {
        var check = await AppServices.AppUpdate.CheckAsync(AppServices.Settings.UpdateManifestUrl);
        _manifest = check.Manifest;
        AppUpdateAvailable = check.UpdateAvailable;
        AppUpdateText = $"Installiert: {check.Current.ToString(3)} · {check.Message}";
    }

    [RelayCommand] private Task CheckAppAsync() => CheckAppUpdateAsync();

    [RelayCommand]
    private async Task InstallAppUpdateAsync()
    {
        if (_manifest is null) return;
        await AppServices.Guard.RunAsync("ULTIMATE X aktualisieren",
            $"Version {_manifest.Version} wird per HTTPS geladen, per SHA-256 und Authenticode-Signatur geprüft und anschließend installiert.\n" +
            (string.IsNullOrWhiteSpace(_manifest.Notes) ? "" : $"\nNeu: {_manifest.Notes}\n") +
            "\nULTIMATE X wird für die Installation beendet.", async () =>
            {
                var progress = new Progress<double>(p => AppUpdateProgress = p);
                var dl = await AppServices.AppUpdate.DownloadAndVerifyAsync(_manifest, progress);
                AppUpdateText = dl.Message;
                if (!dl.Success) return false;
                var start = AppServices.AppUpdate.StartInstaller();
                AppUpdateText = start.Message;
                if (start.Success) System.Windows.Application.Current.Shutdown();
                return start.Success;
            });
    }

    [RelayCommand] private void OpenWindowsUpdate() => ShowResult(AppServices.Tools.Launch(SystemTool.WindowsUpdate));
}
