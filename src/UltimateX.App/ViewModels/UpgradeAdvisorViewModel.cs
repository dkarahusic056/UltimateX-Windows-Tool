using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;

namespace UltimateX.App.ViewModels;

public sealed record UpgradeRow(string Component, string Priority, string Reason, DataState State);

/// <summary>UPGRADE ADVISOR (v1.2 §18): nur begründete Aufrüstempfehlungen; kostenlose Maßnahmen zuerst.</summary>
public sealed partial class UpgradeAdvisorViewModel(INavigator nav) : PageViewModel
{
    public override string Title => "Upgrade Advisor";
    public override string Glyph => "";
    public override string Keywords => "upgrade aufrüsten aufrüstung empfehlung gpu cpu ram ssd kühlung advisor bottleneck";
    public override string? Subtitle => "Lohnt sich eine Aufrüstung – und welche?";

    public ObservableCollection<UpgradeRow> Upgrades { get; } = [];
    public ObservableCollection<string> FreeFixes { get; } = [];
    public ObservableCollection<Finding> Analysis { get; } = [];
    public ObservableCollection<ScoreRow> Scores { get; } = [];

    [ObservableProperty] private string _headline = "–";
    [ObservableProperty] private DataState _headlineState = DataState.NotAvailable;
    [ObservableProperty] private string? _bottleneck;

    protected override async Task LoadAsync()
    {
        var facts = await FactsCollector.CollectAsync();
        var advice = UpgradeAdvisor.Advise(facts.Facts);
        Headline = advice.Headline;
        HeadlineState = advice.Upgrades.Count == 0
            ? (facts.Facts.Scores.Cpu is null && facts.Facts.Scores.Gpu is null ? DataState.NotAvailable : DataState.Ok)
            : DataState.Warning;
        Upgrades.Clear();
        foreach (var u in advice.Upgrades)
            Upgrades.Add(new UpgradeRow(u.Component, u.Priority switch { 1 => "Hoch", 2 => "Mittel", _ => "Niedrig" }, u.Reason,
                u.Priority == 1 ? DataState.Problem : DataState.Warning));
        FreeFixes.Clear();
        foreach (var f in advice.FreeFixes) FreeFixes.Add(f);
        if (FreeFixes.Count == 0) FreeFixes.Add("Keine Konfigurationsprobleme gefunden.");

        var report = BottleneckAnalyzer.Analyze(facts.Facts);
        Bottleneck = report.Summary;
        Analysis.Clear();
        foreach (var f in report.Findings) Analysis.Add(f);

        var s = facts.Facts.Scores;
        Scores.Clear();
        Scores.Add(new ScoreRow("CPU", s.Cpu));
        Scores.Add(new ScoreRow("GPU", s.Gpu));
        Scores.Add(new ScoreRow("RAM", s.Ram));
        Scores.Add(new ScoreRow("SSD", s.Storage));
        Scores.Add(new ScoreRow("Cooling", s.Cooling));
    }

    [RelayCommand]
    private void OpenBenchmarks() => nav.Navigate(nav.Page<BenchmarkViewModel>());
}
