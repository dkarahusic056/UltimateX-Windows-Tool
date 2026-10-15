using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.NetSec;
using UltimateX.Services.NetSec;

namespace UltimateX.App.ViewModels;

public sealed record PrivacyRow(PrivacyStatus Status)
{
    public string Id => Status.Setting.Id;
    public string Title => Status.Setting.Title;
    public string Description => Status.Setting.Description;
    public string StateText => PrivacyCatalog.StateText(Status.State);
    public DataState State => Status.State switch
    {
        PrivacyState.Protected => DataState.Ok,
        PrivacyState.Partial => DataState.Warning,
        _ => DataState.NotAvailable,
    };
    public bool IsProtected => Status.State == PrivacyState.Protected;
    public bool CanUndo => Status.HasBackup || Status.State != PrivacyState.Default;
    public string Scope => Status.Setting.RequiresAdmin ? "Alle Benutzer · Adminrechte (UAC)" : "Nur dieses Konto";
    public string RegistryPath => Status.Setting.RegistryPath;
    public string Values => Status.ValuesText;
    public string BackupText => Status.HasBackup ? "Ursprungswert gesichert" : "Keine Sicherung";
}

/// <summary>
/// DATENSCHUTZ (v1.3): einzeln schaltbare Windows-Einstellungen mit Status und Rückgängig.
/// Ursprüngliche Werte werden in %LOCALAPPDATA%\UltimateX\privacy-backup.json gemerkt.
/// Es werden keine Windows-Komponenten entfernt, keine Hosts-Datei geändert und keine Dienste abgeschaltet.
/// </summary>
public sealed partial class PrivacyViewModel : PageViewModel
{
    public override string Title => "Datenschutz";
    public override string Glyph => "";
    public override string Keywords => "datenschutz privacy werbe id werbung tipps vorschläge startmenü sperrbildschirm diagnosedaten telemetrie aktivitätsverlauf websuche bing";
    public override string? Subtitle => "Windows-Datenschutzeinstellungen einzeln umschalten – jederzeit rückgängig";

    public ObservableCollection<PrivacyRow> Settings { get; } = [];

    public string BackupPath => PrivacyService.BackupPath;

    protected override async Task LoadAsync()
    {
        var list = await Task.Run(AppServices.Privacy.GetStatus);
        Settings.Clear();
        foreach (var s in list) Settings.Add(new PrivacyRow(s));
    }

    [RelayCommand]
    private async Task ProtectAsync(PrivacyRow? row)
    {
        if (row is null || IsBusy) return;
        if (row.IsProtected) { StatusMessage = "Bereits aktiv."; return; }
        var setting = row.Status.Setting;
        if (setting.RequiresAdmin && !Dialogs.Confirm(setting.Title,
                $"„{setting.Title}“ gilt für alle Benutzer dieses PCs und benötigt Adminrechte.\n\nDer aktuelle Wert wird vorher gesichert und kann hier jederzeit wiederhergestellt werden. Fortfahren?"))
            return;
        await RunAsync(() => AppServices.Privacy.ProtectAsync(setting, CancellationToken.None));
    }

    [RelayCommand]
    private async Task UndoAsync(PrivacyRow? row)
    {
        if (row is null || IsBusy) return;
        var setting = row.Status.Setting;
        var text = row.Status.HasBackup
            ? $"„{setting.Title}“ auf den gesicherten Ursprungswert zurücksetzen?"
            : $"Für „{setting.Title}“ gibt es keine Sicherung. Auf den Windows-Standard zurücksetzen?";
        if (!Dialogs.Confirm("Rückgängig", text)) return;
        await RunAsync(() => AppServices.Privacy.RestoreAsync(setting, CancellationToken.None));
    }

    private async Task RunAsync(Func<Task<OperationResult>> action)
    {
        IsBusy = true;
        OperationResult r;
        try
        {
            r = await action();
        }
        finally
        {
            IsBusy = false;
        }
        await RefreshAsync();
        StatusMessage = r.Message;
    }

    [RelayCommand]
    private void OpenPrivacySettings() => ShowResult(UriLauncher.OpenSettings("privacy"));
}
