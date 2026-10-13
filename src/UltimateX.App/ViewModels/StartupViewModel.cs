using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Startup;

namespace UltimateX.App.ViewModels;

public sealed partial class StartupRow(StartupEntry entry) : ObservableObject
{
    public StartupEntry Entry { get; private set; } = entry;
    public string Name => Entry.Name;
    public string Source => Entry.SourceText;
    public string Path => Entry.ExecutablePath ?? Entry.Command;
    public bool CanToggle => Entry.CanToggle;
    public bool IsSystemRelevant => Entry.IsSystemRelevant;
    public string Publisher => Entry.Signature?.Publisher ?? "–";
    public string SignatureText => Entry.Signature?.StatusText ?? "nicht geprüft";
    public string ToggleHint => StartupService.RequiresAdmin(Entry) ? "Eintrag für alle Benutzer – Änderung mit Administratorfreigabe (UAC)" : "";
    public bool NeedsAdmin => StartupService.RequiresAdmin(Entry);

    public DataState SignatureState => Entry.Signature?.Status switch
    {
        SignatureStatus.Valid => DataState.Ok,
        SignatureStatus.Tampered => DataState.Problem,
        SignatureStatus.Untrusted => DataState.Warning,
        SignatureStatus.NotSigned => DataState.Warning,
        _ => DataState.NotAvailable,
    };

    [ObservableProperty] private bool _enabled = entry.Enabled;

    /// <summary>Verhindert Rückkopplung, wenn der Schalter nach Abbruch zurückgesetzt wird.</summary>
    internal bool Suppress;

    internal void Update(StartupEntry e) => Entry = e;
}

/// <summary>Startup Manager (§35): nur aktivieren/deaktivieren, nie löschen.</summary>
public sealed partial class StartupViewModel : PageViewModel
{
    public override string Title => "Autostart";
    public override string Glyph => "\uE7E8";
    public override string Keywords => "autostart startup start programme beim start hochfahren boot";

    public ObservableCollection<StartupRow> Rows { get; } = [];

    [ObservableProperty] private string? _summary;

    protected override async Task LoadAsync()
    {
        var entries = await Task.Run(() => AppServices.Startup.GetEntries());
        foreach (var r in Rows) r.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        foreach (var e in entries)
        {
            var row = new StartupRow(e);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
        Summary = $"{entries.Count(e => e.Enabled)} von {entries.Count} Einträgen aktiv";
    }

    private async void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not StartupRow row || e.PropertyName != nameof(StartupRow.Enabled) || row.Suppress) return;
        var target = row.Enabled;

        bool ok;
        if ((row.IsSystemRelevant && !target) || row.NeedsAdmin)
        {
            // Kritische Startup-Änderung bzw. Admin-Änderung → Authenticate → Explain → Confirm → Execute → Log (§57).
            var explanation = row.IsSystemRelevant && !target
                ? $"„{row.Name}“ ist als systemrelevant gekennzeichnet (z. B. Windows-Sicherheit oder Treiberdienst).\nDas Deaktivieren kann Schutzfunktionen oder Hardware beeinträchtigen. Die Änderung ist jederzeit umkehrbar."
                : $"„{row.Name}“ gilt für alle Benutzer dieses PCs. Die Änderung erfolgt mit Administratorfreigabe (UAC) und ist jederzeit umkehrbar.";
            ok = await AppServices.Guard.RunAsync(target ? "Autostart aktivieren" : "Autostart deaktivieren", explanation,
                () => ApplyAsync(row, target), warning: row.IsSystemRelevant);
        }
        else
        {
            ok = await ApplyAsync(row, target);
        }

        if (!ok)
        {
            row.Suppress = true;
            row.Enabled = !target;
            row.Suppress = false;
        }
        Summary = $"{Rows.Count(r => r.Enabled)} von {Rows.Count} Einträgen aktiv";
    }

    private async Task<bool> ApplyAsync(StartupRow row, bool enabled)
    {
        var result = await AppServices.Startup.SetEnabledAsync(row.Entry, enabled);
        StatusMessage = result.Message;
        if (result.Success) row.Update(row.Entry with { Enabled = enabled });
        return result.Success;
    }

    /// <summary>Letztes Backup (Benutzer-Einträge) wiederherstellen (§85).</summary>
    [RelayCommand]
    private async Task RestoreLastBackupAsync()
    {
        var backups = StartupService.ListBackups();
        if (backups.Count == 0) { StatusMessage = "Kein Autostart-Backup vorhanden."; return; }
        if (!Dialogs.Confirm("Autostart-Backup wiederherstellen",
                $"Den Zustand vom Backup „{System.IO.Path.GetFileName(backups[0])}“ für Ihre Benutzer-Einträge wiederherstellen?")) return;
        ShowResult(AppServices.Startup.RestoreUserBackup(backups[0]));
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenBackups()
    {
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { AppPaths.BackupDirectory },
            UseShellExecute = false,
        });
    }
}
