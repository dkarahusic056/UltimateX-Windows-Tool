using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Common;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed record SnapshotChoice(SnapshotFile File)
{
    public override string ToString() => $"{Format.DateTimeText(File.Created)} · {File.Label}";
}

public sealed record ChangeRow(string Section, string Kind, string Key, string Before, string After, DataState State);

/// <summary>SYSTEM SNAPSHOT (v1.2 §38): Systemstand speichern und BEFORE vs. AFTER vergleichen.</summary>
public sealed partial class SnapshotViewModel : PageViewModel
{
    public override string Title => "System Snapshot";
    public override string Glyph => "";
    public override string Keywords => "snapshot vergleich vorher nachher before after änderungen systemstand treiber programme dienste";
    public override string? Subtitle => "Systemstand vor Änderungen sichern und vergleichen";

    public ObservableCollection<SnapshotChoice> Snapshots { get; } = [];
    public ObservableCollection<ChangeRow> Changes { get; } = [];
    public ObservableCollection<InfoItem> Summary { get; } = [];

    [ObservableProperty] private SnapshotChoice? _before;
    [ObservableProperty] private SnapshotChoice? _after;
    [ObservableProperty] private string _label = "Vor Änderungen";
    [ObservableProperty] private string? _progressText;

    protected override async Task LoadAsync()
    {
        var list = await Task.Run(SnapshotService.List);
        Snapshots.Clear();
        foreach (var s in list) Snapshots.Add(new SnapshotChoice(s));
        After ??= Snapshots.ElementAtOrDefault(0);
        Before ??= Snapshots.ElementAtOrDefault(1);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        IsBusy = true;
        try
        {
            var label = string.IsNullOrWhiteSpace(Label) ? "Snapshot" : Label.Trim();
            var progress = new Progress<string>(s => ProgressText = "Erfasse: " + s);
            var path = await Task.Run(() => AppServices.Snapshots.Save(AppServices.Snapshots.Capture(label, AppServices.AppVersion, progress)));
            StatusMessage = "Snapshot gespeichert: " + path;
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
        }
        Before = null;
        After = null;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        if (Before is null || After is null || Before == After) { StatusMessage = "Bitte zwei verschiedene Snapshots wählen (BEFORE und AFTER)."; return; }
        var (b, a) = await Task.Run(() => (SnapshotService.Load(Before.File.Path), SnapshotService.Load(After.File.Path)));
        if (b is null || a is null) { StatusMessage = "Snapshot konnte nicht gelesen werden."; return; }
        var (older, newer) = b.Created <= a.Created ? (b, a) : (a, b);
        var cmp = SnapshotComparer.Compare(older, newer);
        Changes.Clear();
        foreach (var c in cmp.Changes.Take(2000))
            Changes.Add(new ChangeRow(c.Section, c.Kind switch { ChangeKind.Added => "Neu", ChangeKind.Removed => "Entfernt", _ => "Geändert" },
                c.Key, c.Before ?? "–", c.After ?? "–", c.Kind == ChangeKind.Changed ? DataState.Warning : c.Kind == ChangeKind.Added ? DataState.Ok : DataState.Problem));
        Summary.Clear();
        Summary.Add(new InfoItem("BEFORE", $"{Format.DateTimeText(older.Created)} · {older.Label}"));
        Summary.Add(new InfoItem("AFTER", $"{Format.DateTimeText(newer.Created)} · {newer.Label}"));
        Summary.Add(new InfoItem("Änderungen", $"{cmp.Added} neu · {cmp.Removed} entfernt · {cmp.Modified} geändert", cmp.Changes.Count == 0 ? DataState.Ok : DataState.Warning));
        foreach (var (section, count) in cmp.PerSection) Summary.Add(new InfoItem(section, $"{count} Änderung(en)", DataState.Warning));
        StatusMessage = cmp.Changes.Count == 0 ? "Keine Unterschiede." : null;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        System.IO.Directory.CreateDirectory(SnapshotService.Directory);
        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { ArgumentList = { SnapshotService.Directory }, UseShellExecute = false });
    }
}
