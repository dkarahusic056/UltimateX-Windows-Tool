using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Desktop;

namespace UltimateX.App.ViewModels;

public sealed partial class CategoryRow(string name, string extensions, int count) : ObservableObject
{
    [ObservableProperty] private string _name = name;
    [ObservableProperty] private string _extensions = extensions;
    [ObservableProperty] private int _count = count;
}

/// <summary>
/// Desktop Organizer (§14). Ordnet nur Symbolpositionen – Dateien werden nie verschoben oder gelöscht.
/// Vor jeder Änderung wird das Layout gesichert; "Letztes Layout wiederherstellen" macht alles rückgängig.
/// </summary>
public sealed partial class DesktopOrganizerViewModel : PageViewModel
{
    public override string Title => "Desktop Organizer";
    public override string Glyph => "\uE8A9";
    public override string Keywords => "desktop organizer aufräumen ordnen symbole icons sortieren layout";

    public ObservableCollection<CategoryRow> Categories { get; } = [];

    [ObservableProperty] private bool _sortDescending;
    [ObservableProperty] private bool _startRight;
    [ObservableProperty] private int _groupGap = 1;
    [ObservableProperty] private int _iconCount;
    [ObservableProperty] private int _otherCount;
    [ObservableProperty] private int _backupCount;

    public bool SortAz { get => !SortDescending; set { if (value) SortDescending = false; } }
    public bool SortZa { get => SortDescending; set { if (value) SortDescending = true; } }
    public bool SideLeft { get => !StartRight; set { if (value) StartRight = false; } }
    public bool SideRight { get => StartRight; set { if (value) StartRight = true; } }
    public bool Gap0 { get => GroupGap == 0; set { if (value) GroupGap = 0; } }
    public bool Gap1 { get => GroupGap == 1; set { if (value) GroupGap = 1; } }
    public bool Gap2 { get => GroupGap == 2; set { if (value) GroupGap = 2; } }

    partial void OnSortDescendingChanged(bool value) { OnPropertyChanged(nameof(SortAz)); OnPropertyChanged(nameof(SortZa)); }
    partial void OnStartRightChanged(bool value) { OnPropertyChanged(nameof(SideLeft)); OnPropertyChanged(nameof(SideRight)); }
    partial void OnGroupGapChanged(int value) { OnPropertyChanged(nameof(Gap0)); OnPropertyChanged(nameof(Gap1)); OnPropertyChanged(nameof(Gap2)); }

    protected override Task LoadAsync()
    {
        var rules = DesktopOrganizerService.LoadRules();
        SortDescending = rules.SortDescending;
        StartRight = rules.StartSide == DesktopSide.Right;
        GroupGap = rules.GroupGap;
        Categories.Clear();
        foreach (var c in rules.Categories) Categories.Add(new CategoryRow(c.Name, c.Extensions, 0));
        Preview();
        return Task.CompletedTask;
    }

    private OrganizerRules CurrentRules() => new()
    {
        SortDescending = SortDescending,
        StartSide = StartRight ? DesktopSide.Right : DesktopSide.Left,
        GroupGap = GroupGap,
        Categories = Categories.Select(c => new OrganizerCategory(c.Name.Trim(), c.Extensions.Trim())).ToList(),
    };

    /// <summary>Liest den Desktop (ohne Änderung) und zählt Symbole je Kategorie.</summary>
    [RelayCommand]
    private void Preview()
    {
        var (items, error) = AppServices.Organizer.ReadLayout(CurrentRules());
        if (error is not null) { StatusMessage = error; return; }
        IconCount = items.Count;
        foreach (var c in Categories) c.Count = items.Count(i => i.Category == c.Name.Trim());
        OtherCount = items.Count(i => i.Category is "Andere" or "System");
        BackupCount = DesktopOrganizerService.ListBackups().Count;
    }

    [RelayCommand]
    private void SaveRules()
    {
        DesktopOrganizerService.SaveRules(CurrentRules());
        StatusMessage = "Regeln gespeichert.";
        Preview();
    }

    [RelayCommand]
    private void ResetRules()
    {
        Categories.Clear();
        foreach (var c in OrganizerRules.DefaultCategories()) Categories.Add(new CategoryRow(c.Name, c.Extensions, 0));
        Preview();
    }

    [RelayCommand]
    private void Organize()
    {
        var rules = CurrentRules();
        if (!Dialogs.Confirm("Desktop organisieren",
                $"{IconCount} Desktopsymbole werden nach Typ gruppiert und {(SortDescending ? "Z–A" : "A–Z")} sortiert " +
                $"({(StartRight ? "rechts" : "links")} beginnend).\n\nEs werden nur Positionen geändert – keine Datei wird verschoben oder gelöscht. " +
                "Das aktuelle Layout wird vorher gesichert und kann jederzeit wiederhergestellt werden.\n\nFortfahren?"))
            return;
        DesktopOrganizerService.SaveRules(rules);
        ShowResult(AppServices.Organizer.Organize(rules));
        Preview();
    }

    [RelayCommand]
    private void RestoreLayout()
    {
        if (!Dialogs.Confirm("Layout wiederherstellen", "Das zuletzt gesicherte Desktop-Layout wiederherstellen?")) return;
        ShowResult(AppServices.Organizer.RestoreLastLayout());
        Preview();
    }

    [RelayCommand]
    private void AddCategory() => Categories.Add(new CategoryRow("Neue Kategorie", ".ext", 0));

    [RelayCommand]
    private void RemoveCategory(CategoryRow? row)
    {
        if (row is not null) Categories.Remove(row);
    }
}
