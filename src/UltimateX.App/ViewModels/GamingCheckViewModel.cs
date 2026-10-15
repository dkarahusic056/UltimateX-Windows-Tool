using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;

namespace UltimateX.App.ViewModels;

/// <summary>GAMING CHECK (v1.2 §12): Hardware und Windows-Einstellungen für Spiele mit GAMING SCORE.</summary>
public sealed partial class GamingCheckViewModel(INavigator nav) : PageViewModel
{
    public override string Title => "Gaming Check";
    public override string Glyph => "";
    public override string Keywords => "gaming check spiele score game mode spielmodus hags resizable bar energieprofil fps";
    public override string? Subtitle => "Ist dieser PC optimal für Spiele eingerichtet?";

    public ObservableCollection<Finding> Findings { get; } = [];

    [ObservableProperty] private string _score = "–";
    [ObservableProperty] private DataState _scoreState = DataState.NotAvailable;
    [ObservableProperty] private string? _summary;

    protected override async Task LoadAsync()
    {
        var facts = await FactsCollector.CollectAsync();
        var result = GamingCheck.Evaluate(facts.Facts);
        Findings.Clear();
        foreach (var f in result.Findings) Findings.Add(f);
        Score = result.Score is { } s ? $"{s} / 100" : "N/A";
        ScoreState = ScoreMath.StatusOf(result.Score, 75, 50).ToDataState();
        var open = result.Findings.Count(f => f.Status is CheckStatus.Warning or CheckStatus.Fail);
        Summary = open == 0 ? "Alles bereit für Spiele." : $"{open} Punkt(e) mit Verbesserungspotenzial.";
    }

    [RelayCommand]
    private void OpenGameMode() => ShowResult(UriLauncher.OpenSettings("gaming-gamemode"));

    [RelayCommand]
    private void OpenGraphicsSettings() => ShowResult(UriLauncher.OpenSettings("display-advancedgraphics"));

    [RelayCommand]
    private void OpenPowerSettings() => ShowResult(UriLauncher.OpenSettings("powersleep"));

    [RelayCommand]
    private void OpenCanRun() => nav.Navigate(nav.Page<CanRunViewModel>());

    [RelayCommand]
    private void OpenBenchmarks() => nav.Navigate(nav.Page<BenchmarkViewModel>());
}
