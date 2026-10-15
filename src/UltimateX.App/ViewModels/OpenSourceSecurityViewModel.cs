using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Security;

namespace UltimateX.App.ViewModels;

public sealed record ScanHistoryRow(string Time, string Kind, string Target, string Files, string Found, string Summary, DataState State);

/// <summary>
/// OPEN SOURCE SECURITY (v1.2 §27): ClamAV als zusätzlicher Scanner. Microsoft Defender bleibt der Echtzeitschutz
/// und wird nie deaktiviert. Installation erst nach bestandener Online-Prüfung des Projekts.
/// </summary>
public sealed partial class OpenSourceSecurityViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "Open Source Security";
    public override string Glyph => "";
    public override string Keywords => "clamav open source antivirus virenscanner scan quarantäne usb downloads signaturen defender";
    public override string? Subtitle => "ClamAV als Zusatz-Scanner – Microsoft Defender bleibt aktiv";

    public ObservableCollection<Finding> ProjectChecks { get; } = [];
    public ObservableCollection<ScanHistoryRow> History { get; } = [];
    public ObservableCollection<string> Found { get; } = [];
    public ObservableCollection<string> Quarantine { get; } = [];

    [ObservableProperty] private string _installState = "–";
    [ObservableProperty] private DataState _installStateColor = DataState.NotAvailable;
    [ObservableProperty] private string _signatureState = "–";
    [ObservableProperty] private bool _projectApproved;
    [ObservableProperty] private bool _useQuarantine;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string? _output;
    [ObservableProperty] private string? _customPath;

    protected override Task LoadAsync()
    {
        UpdateState();
        LoadHistory();
        return Task.CompletedTask;
    }

    private void UpdateState()
    {
        InstallState = ClamAvService.IsInstalled ? $"Installiert ({CommandCatalog.ClamAvDirectory})" : "Nicht installiert";
        InstallStateColor = ClamAvService.IsInstalled ? DataState.Ok : DataState.NotAvailable;
        SignatureState = ClamAvService.SignaturesUpdated is { } t ? $"Signaturen vom {Format.DateTimeText(t)}" : "Noch keine Signaturen";
        Quarantine.Clear();
        foreach (var q in ClamAvService.QuarantineFiles()) Quarantine.Add(q);
    }

    private void LoadHistory()
    {
        History.Clear();
        foreach (var h in ClamAvService.LoadHistory())
            History.Add(new ScanHistoryRow(Format.DateTimeText(h.Time), h.Kind.ToString(), h.Target, h.ScannedFiles?.ToString(Core.Localization.Loc.Culture) ?? "–",
                h.Infected.Count.ToString(Core.Localization.Loc.Culture), h.Summary,
                h.Infected.Count > 0 ? DataState.Problem : h.Completed ? DataState.Ok : DataState.Warning));
    }

    [RelayCommand]
    private async Task CheckProjectAsync()
    {
        await Work(async ct =>
        {
            var r = await AppServices.ClamAv.CheckProjectAsync(ct);
            ProjectChecks.Clear();
            foreach (var f in r.Findings) ProjectChecks.Add(f);
            ProjectApproved = r.ActivelyMaintained == true && r.Archived != true;
            StatusMessage = r.Error is not null ? "Online-Prüfung nicht möglich: " + r.Error
                : ProjectApproved ? $"Projekt aktiv gepflegt (Version {r.LatestVersion}) – Installation möglich."
                : "Das Projekt erfüllt die Kriterien derzeit nicht – keine Installation.";
        });
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (!ProjectApproved) { StatusMessage = "Bitte zuerst „Projekt online prüfen“ ausführen."; return; }
        if (!Dialogs.Confirm("ClamAV installieren",
                "ClamAV wird über den Windows-Paket-Manager (Cisco.ClamAV) aus der offiziellen Quelle installiert – nach Prüfung von Herstellerdomain und SHA-256.\n\n" +
                "ClamAV ist ein zusätzlicher Scanner ohne Echtzeitschutz. Microsoft Defender bleibt aktiv und wird nicht verändert.\n\nInstallieren?")) return;
        await Work(async ct =>
        {
            var o = await AppServices.SoftwareSetup.InstallAsync(SoftwareCatalog.ClamAv, new Progress<string>(Append), ct);
            StatusMessage = o.Message;
            UpdateState();
        });
    }

    [RelayCommand]
    private async Task UpdateSignaturesAsync()
    {
        await Work(async ct =>
        {
            var (ok, output) = await AppServices.ClamAv.UpdateSignaturesAsync(new Progress<string>(Append), ct);
            StatusMessage = ok ? "Signaturen aktualisiert." : "Signatur-Update fehlgeschlagen – Details in der Ausgabe.";
            if (!ok && Output is null) Output = output;
            UpdateState();
        });
    }

    [RelayCommand]
    private Task QuickScanAsync() => Scan(ClamScanKind.Quick, null);

    [RelayCommand]
    private Task FullScanAsync()
        => Dialogs.Confirm("Full Scan", "Ein vollständiger Scan aller lokalen Laufwerke kann je nach Datenmenge mehrere Stunden dauern. Starten?")
            ? Scan(ClamScanKind.Full, null) : Task.CompletedTask;

    [RelayCommand]
    private Task UsbScanAsync() => Scan(ClamScanKind.Usb, null);

    [RelayCommand]
    private Task DownloadsScanAsync() => Scan(ClamScanKind.Downloads, null);

    [RelayCommand]
    private Task CustomScanAsync()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Core.Localization.Loc.T("Ordner für den Scan auswählen") };
        if (dlg.ShowDialog() != true) return Task.CompletedTask;
        CustomPath = dlg.FolderName;
        return Scan(ClamScanKind.Custom, dlg.FolderName);
    }

    private async Task Scan(ClamScanKind kind, string? path)
    {
        await Work(async ct =>
        {
            var r = await AppServices.ClamAv.ScanAsync(kind, path, UseQuarantine, new Progress<string>(Append), ct);
            Found.Clear();
            foreach (var f in r.Infected) Found.Add(f);
            StatusMessage = r.Summary;
            LoadHistory();
            UpdateState();
        });
    }

    [RelayCommand]
    private void OpenQuarantine()
    {
        System.IO.Directory.CreateDirectory(CommandCatalog.ClamAvQuarantineDirectory);
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { ArgumentList = { CommandCatalog.ClamAvQuarantineDirectory }, UseShellExecute = false });
    }

    [RelayCommand]
    private void OpenDefender() => ShowResult(AppServices.Tools.Launch(Services.SystemTools.SystemTool.WindowsSecurity));

    private async Task Work(Func<CancellationToken, Task> work)
    {
        if (IsWorking) return;
        IsWorking = true;
        Output = null;
        StatusMessage = null;
        _cts = new CancellationTokenSource();
        try { await work(_cts.Token); }
        catch (OperationCanceledException) { StatusMessage = "Abgebrochen."; }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsWorking = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    private void Append(string text)
    {
        var o = (Output ?? "") + text;
        Output = o.Length > 20_000 ? o[^20_000..] : o;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
