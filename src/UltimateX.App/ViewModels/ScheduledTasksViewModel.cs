using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.NetSec;
using UltimateX.Services.Common;
using UltimateX.Services.NetSec;

namespace UltimateX.App.ViewModels;

public sealed record ScheduledTaskRow(ScheduledTaskInfo Task)
{
    public string Path => Task.Path;
    public string Name => Task.Name;
    public string Folder => Task.Folder;
    public string State => Task.State;
    public bool Enabled => Task.Enabled;
    public string Triggers => Task.Triggers;
    public string LastRun => Task.LastRun is { } d ? Format.DateTimeText(d) : "nie";
    public string NextRun => Task.NextRun is { } d ? Format.DateTimeText(d) : "–";
    public string LastResult => Task.LastResultText;
    public string Program => Task.Program ?? "–";
    public string Arguments => Task.Arguments ?? "";
    public string Publisher => Task.Publisher;
    public string Marker => Task.MarkerText;
    public DataState MarkerState => Task.MarkerState;
    public bool IsMicrosoftWindows => Task.IsMicrosoftWindows;
}

/// <summary>
/// GEPLANTE AUFGABEN (v1.3): alle Aufgaben der Aufgabenplanung mit Programm, Signatur und Markierung.
/// Aktivieren/Deaktivieren nur nach Bestätigung (UAC). Es wird nichts gelöscht.
/// </summary>
public sealed partial class ScheduledTasksViewModel : PageViewModel
{
    public override string Title => "Geplante Aufgaben";
    public override string Glyph => "";
    public override string Keywords => "geplante aufgaben aufgabenplanung task scheduler schtasks autostart signatur unsigniert temp appdata deaktivieren";
    public override string? Subtitle => "Was startet Windows automatisch nach Zeitplan – und ist es vertrauenswürdig?";

    private List<ScheduledTaskRow> _all = [];

    public ObservableCollection<ScheduledTaskRow> Tasks { get; } = [];
    public ObservableCollection<InfoItem> Summary { get; } = [];

    [ObservableProperty] private ScheduledTaskRow? _selectedTask;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _hideMicrosoft;
    [ObservableProperty] private bool _onlyNoticeable;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private string? _output;

    protected override async Task LoadAsync()
    {
        var progress = new Progress<int>(n => ProgressText = $"{n} Aufgaben gelesen …");
        var scan = await Task.Run(() => AppServices.ScheduledTasks.GetTasks(progress, CancellationToken.None));
        ProgressText = null;
        _all = scan.Tasks.Select(t => new ScheduledTaskRow(t)).ToList();
        Summary.Clear();
        Summary.Add(new InfoItem("Aufgaben", _all.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        Summary.Add(new InfoItem("Deaktiviert", _all.Count(r => !r.Enabled).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        foreach (var group in _all.GroupBy(r => r.Task.Marker).OrderBy(g => g.Key))
            Summary.Add(new InfoItem(ScheduledTaskLogic.Label(group.Key), group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture),
                group.First().Task.MarkerState));
        if (scan.SkippedFolders > 0)
            Summary.Add(new InfoItem("Nicht lesbare Ordner", $"{scan.SkippedFolders} (Adminrechte bzw. andere Benutzer)", DataState.RequiresAdmin));
        ApplyFilter();
        if (scan.Error is not null) StatusMessage = scan.Error;
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnHideMicrosoftChanged(bool value) => ApplyFilter();

    partial void OnOnlyNoticeableChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = Filter.Trim();
        var rows = _all.Where(r =>
            (!HideMicrosoft || !r.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase))
            && (!OnlyNoticeable || r.MarkerState is DataState.Warning or DataState.Problem)
            && (f.Length == 0 || r.Path.Contains(f, StringComparison.OrdinalIgnoreCase) || r.Program.Contains(f, StringComparison.OrdinalIgnoreCase)
                || r.Publisher.Contains(f, StringComparison.OrdinalIgnoreCase)));
        Tasks.Clear();
        foreach (var r in rows) Tasks.Add(r);
    }

    [RelayCommand]
    private Task DisableAsync(ScheduledTaskRow? row) => SetEnabledAsync(row ?? SelectedTask, false);

    [RelayCommand]
    private Task EnableAsync(ScheduledTaskRow? row) => SetEnabledAsync(row ?? SelectedTask, true);

    private async Task SetEnabledAsync(ScheduledTaskRow? row, bool enable)
    {
        if (row is null) { StatusMessage = "Bitte eine Aufgabe auswählen."; return; }
        if (IsBusy) return;
        if (row.Enabled == enable) { StatusMessage = enable ? "Die Aufgabe ist bereits aktiviert." : "Die Aufgabe ist bereits deaktiviert."; return; }

        var text = enable
            ? $"Aufgabe „{row.Path}“ aktivieren?\n\nProgramm: {row.Program}"
            : $"Aufgabe „{row.Path}“ deaktivieren?\n\nProgramm: {row.Program}\n\nDie Aufgabe wird nicht gelöscht und kann hier jederzeit wieder aktiviert werden.";
        if (row.IsMicrosoftWindows)
            text += "\n\nACHTUNG: Dies ist eine Windows-Aufgabe (\\Microsoft\\Windows\\). Das Deaktivieren kann Wartung, Updates oder Sicherheitsfunktionen beeinträchtigen. Nur ändern, wenn Sie genau wissen, wofür sie dient.";
        if (!Dialogs.Confirm(enable ? "Aufgabe aktivieren" : "Aufgabe deaktivieren", text, warning: row.IsMicrosoftWindows))
            return;

        IsBusy = true;
        OperationResult r;
        try
        {
            r = await ScheduledTaskService.SetEnabledAsync(row.Path, enable, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
        Output = r.TechnicalDetails;
        if (r.Success) await RefreshAsync();
        StatusMessage = r.Message;
    }
}
