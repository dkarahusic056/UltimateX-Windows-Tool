using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Timeline;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Filter-Schalter je Ereignisart.</summary>
public sealed partial class TimelineKindFilter(TimelineKind kind, Action changed) : ObservableObject
{
    public TimelineKind Kind { get; } = kind;
    public string Label { get; } = TimelineRules.Label(kind);

    [ObservableProperty] private bool _isChecked = true;

    partial void OnIsCheckedChanged(bool value) => changed();
}

/// <summary>Ein Ereignis auf der Zeitachse.</summary>
public sealed record TimelineRow(TimelineEvent Event, string TimeText, string Glyph, DataState State, string? HintText)
{
    public string Title => Event.Title;
    public string KindLabel => Event.KindLabel;
    public bool IsHinted => HintText is not null;
}

/// <summary>Ein Tag mit seinen Ereignissen.</summary>
public sealed record TimelineDayRow(string Header, IReadOnlyList<TimelineRow> Events);

/// <summary>Zeitraum-Auswahl.</summary>
public sealed record TimelineRangeOption(int Days, string Label);

/// <summary>
/// ZEITLEISTE – WAS IST PASSIERT? (v1.3): Updates, Programm- und Treiberinstallationen, Bluescreens, unerwartete Neustarts,
/// Programmabstürze, Start/Herunterfahren und eigene Snapshots/Sicherungen der letzten 7/30/90 Tage – gruppiert nach Tag.
/// Ursachen-Hinweis: erste Abstürze innerhalb von 48 h nach einer Treiber-/Update-Installation werden markiert (kein Beweis).
/// </summary>
public sealed partial class TimelineViewModel : PageViewModel
{
    public override string Title => "Zeitleiste";
    public override string Glyph => "\uE81C";
    public override string Keywords => "zeitleiste timeline was ist passiert verlauf ereignisse updates installiert treiber absturz bluescreen neustart ursache seit wann historie";
    public override string? Subtitle => "Was ist passiert? Änderungen und Probleme auf einer Zeitachse";

    private static readonly IReadOnlyList<TimelineRangeOption> RangeOptions =
    [
        new(7, "7 Tage"),
        new(30, "30 Tage"),
        new(90, "90 Tage"),
    ];

    public IReadOnlyList<TimelineRangeOption> Ranges => RangeOptions;

    public ObservableCollection<TimelineKindFilter> KindFilters { get; } = [];
    public ObservableCollection<TimelineDayRow> Days { get; } = [];
    public ObservableCollection<string> Hints { get; } = [];
    public ObservableCollection<string> Notes { get; } = [];

    [ObservableProperty] private TimelineRangeOption? _selectedRange = RangeOptions[1];
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private TimelineRow? _selectedRow;
    [ObservableProperty] private string? _summaryText;
    [ObservableProperty] private bool _hasHints;

    private IReadOnlyList<TimelineEvent> _events = [];
    private IReadOnlyList<CauseHint> _hints = [];
    private bool _suppressFilter;

    public TimelineViewModel()
    {
        foreach (var k in Enum.GetValues<TimelineKind>()) KindFilters.Add(new TimelineKindFilter(k, ApplyFilter));
    }

    partial void OnSelectedRangeChanged(TimelineRangeOption? value)
    {
        if (value is not null) _ = RefreshAsync();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    protected override async Task LoadAsync()
    {
        var days = SelectedRange?.Days ?? 30;
        var result = await Task.Run(() => AppServices.Timeline.Load(days));
        _events = result.Events;
        _hints = result.Hints;
        Hints.Clear();
        foreach (var h in result.Hints.Take(20)) Hints.Add(h.Text + (h.OtherChangesInWindow > 0 ? $" (weitere Änderungen im Zeitfenster: {h.OtherChangesInWindow})" : ""));
        HasHints = Hints.Count > 0;
        Notes.Clear();
        foreach (var n in result.Notes) Notes.Add(n);
        SelectedRow = null;
        ApplyFilter();
    }

    [RelayCommand]
    private void Select(TimelineRow? row) => SelectedRow = row;

    [RelayCommand]
    private void SelectAllKinds() => SetAllKinds(true);

    [RelayCommand]
    private void ProblemsOnly()
    {
        _suppressFilter = true;
        foreach (var f in KindFilters) f.IsChecked = TimelineRules.IsProblem(f.Kind) || TimelineRules.IsChange(f.Kind);
        _suppressFilter = false;
        ApplyFilter();
    }

    private void SetAllKinds(bool value)
    {
        _suppressFilter = true;
        foreach (var f in KindFilters) f.IsChecked = value;
        _suppressFilter = false;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_suppressFilter) return;
        var kinds = KindFilters.Where(f => f.IsChecked).Select(f => f.Kind).ToHashSet();
        var hinted = _hints.ToDictionary<CauseHint, TimelineEvent, CauseHint>(h => h.Crash, h => h, ReferenceEqualityComparer.Instance);
        var filtered = TimelineRules.Filter(_events, kinds.Count == KindFilters.Count ? null : kinds, SearchText).ToList();
        if (kinds.Count == 0) filtered.Clear();

        Days.Clear();
        foreach (var (day, events) in TimelineRules.GroupByDay(filtered))
        {
            var rows = events.Select(e => new TimelineRow(e,
                e.DateOnly ? "––:––" : e.Time.ToString("HH:mm", Loc.Culture),
                TimelineRules.Glyph(e.Kind),
                TimelineRules.IsProblem(e.Kind) ? DataState.Problem : TimelineRules.IsChange(e.Kind) ? DataState.Warning : DataState.Ok,
                hinted.TryGetValue(e, out var h) ? $"Ursachen-Hinweis: {h.HoursAfter:0} h nach {TimelineRules.Label(h.Change.Kind)} „{h.Change.Title}“" : null)).ToList();
            Days.Add(new TimelineDayRow(day.ToString("dddd, dd.MM.yyyy", Loc.Culture) + $" · {rows.Count} Ereignis(se)", rows));
        }
        var problems = filtered.Count(e => TimelineRules.IsProblem(e.Kind));
        SummaryText = $"{filtered.Count} von {_events.Count} Ereignissen · {problems} Probleme · {_hints.Count} Ursachen-Hinweis(e) · Zeitraum {SelectedRange?.Label ?? "30 Tage"}";
    }

    /// <summary>Details des gewählten Ereignisses als Werteliste.</summary>
    public IReadOnlyList<InfoItem> SelectedDetails
    {
        get
        {
            if (SelectedRow is not { } r) return [];
            var items = new List<InfoItem>
            {
                new("Art", r.KindLabel, r.State),
                new("Zeitpunkt", r.Event.DateOnly
                    ? Format.DateText(r.Event.Time) + " (Uhrzeit unbekannt)"
                    : Format.DateTimeText(r.Event.Time) + r.Event.Time.ToString(":ss", Loc.Culture)),
                new("Titel", r.Event.Title),
                new("Details", string.IsNullOrWhiteSpace(r.Event.Detail) ? "–" : r.Event.Detail),
                new("Quelle", r.Event.Source),
            };
            if (r.HintText is { } hint) items.Add(new InfoItem("Hinweis", hint + " – möglicher Zusammenhang, kein Beweis.", DataState.Warning));
            return items;
        }
    }

    partial void OnSelectedRowChanged(TimelineRow? value) => OnPropertyChanged(nameof(SelectedDetails));
}
