using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Gaming;

namespace UltimateX.App.ViewModels;

public sealed record RequirementRow(string Area, string Mine, string Minimum, string Recommended, string StatusText, DataState State);

public sealed record FpsRow(string Resolution, string Low, string Medium, string High, string Ultra);

public sealed record TargetRow(string Target, bool Reached)
{
    public string Text => Reached ? "erreichbar (ESTIMATED)" : "nicht erreichbar";
    public DataState State => Reached ? DataState.Ok : DataState.NotAvailable;
}

public sealed record InstalledGameRow(string Name, string Launcher, string Size, string Suitability, DataState State, string Performance, string Bottleneck, string Recommended);

public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// CAN MY PC RUN IT? (v1.2 §13–§17): Vergleich mit Mindest-/Empfehlungsanforderungen, vorsichtige FPS-Schätzung (ESTIMATED),
/// Spiele-/Software-Suche im Standardbrowser und Erkennung installierter Spiele. Vergleichsdaten: eingebaute Datenbank sowie
/// (v1.3, nur auf Knopfdruck) Anforderungen aus dem öffentlichen Steam-Shop, die lokal gespeichert werden.
/// </summary>
public sealed partial class CanRunViewModel : PageViewModel
{
    private CollectedFacts? _facts;
    private List<InstalledGame> _installed = [];
    private CancellationTokenSource? _onlineCts;

    public override string Title => "Can My PC Run It";
    public override string Glyph => "";
    public override string Keywords => "can my pc run it spiel anforderungen systemanforderungen fps schätzung game requirements installierte spiele steam epic xbox gog";
    public override string? Subtitle => "Spiele mit diesem PC vergleichen";

    public ObservableCollection<GameInfo> Suggestions { get; } = [];
    public ObservableCollection<RequirementRow> Checks { get; } = [];
    public ObservableCollection<FpsRow> FpsTable { get; } = [];
    public ObservableCollection<TargetRow> Targets { get; } = [];
    public ObservableCollection<InstalledGameRow> InstalledGames { get; } = [];
    public ObservableCollection<string> GameQueries { get; } = [];

    public IReadOnlyList<Choice<Resolution>> Resolutions { get; } =
        [new(Resolution.P1080, "1080p"), new(Resolution.P1440, "1440p"), new(Resolution.P2160, "4K")];
    public IReadOnlyList<Choice<Quality>> Qualities { get; } =
        [new(Quality.Low, "Low"), new(Quality.Medium, "Medium"), new(Quality.High, "High"), new(Quality.Ultra, "Ultra")];
    public IReadOnlyList<Choice<Upscaler>> Upscalers { get; } =
        [new(Upscaler.None, "Kein Upscaling"), new(Upscaler.Dlss, "DLSS"), new(Upscaler.Fsr, "FSR"), new(Upscaler.Xess, "XeSS")];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private GameInfo? _selectedGame;
    [ObservableProperty] private string? _gameTitle;
    [ObservableProperty] private string _ratingText = "–";
    [ObservableProperty] private DataState _ratingState = DataState.NotAvailable;
    [ObservableProperty] private string? _ratingSummary;
    [ObservableProperty] private Choice<Resolution>? _selectedResolution;
    [ObservableProperty] private Choice<Quality>? _selectedQuality;
    [ObservableProperty] private Choice<Upscaler>? _selectedUpscaler;
    [ObservableProperty] private bool _rayTracing;
    [ObservableProperty] private string _fpsText = "–";
    [ObservableProperty] private string? _fpsLimit;
    [ObservableProperty] private string? _myPc;
    [ObservableProperty] private string? _notFound;
    [ObservableProperty] private bool _isScanningGames;
    [ObservableProperty] private bool _isOnlineBusy;
    [ObservableProperty] private string? _onlineProgress;
    [ObservableProperty] private string? _selectedSource;
    [ObservableProperty] private bool _isSelectedOnline;
    [ObservableProperty] private int _missingCount;

    public string DatabaseInfo =>
        $"Spieledatenbank {GameCompatibility.DatabaseVersion} · {GameCompatibility.BuiltInGames.Count} Spiele eingebaut · " +
        $"{GameCompatibility.UserGames.Count} online gespeichert · {GameCompatibility.DatabaseNote}";

    public string OnlinePrivacyNote => GameRequirementsOnlineService.PrivacyNote;

    public CanRunViewModel()
    {
        SelectedResolution = Resolutions[0];
        SelectedQuality = Qualities[2];
        SelectedUpscaler = Upscalers[0];
    }

    protected override async Task LoadAsync()
    {
        _facts = await FactsCollector.CollectAsync(includeProcesses: false);
        var p = _facts.ToPcProfile();
        MyPc = $"{p.CpuName} · {p.GpuName} · {(p.VramGb is { } v ? v + " GB VRAM · " : "")}{(p.RamGb is { } r ? $"{r:0} GB RAM" : "")}" +
               $" · DirectX {(p.DirectXVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A")}";
        // Gespeicherte Online-Spiele laden, damit sie in Suche und Spieleliste erscheinen (kein Netzwerkzugriff).
        var loaded = await Task.Run(() => AppServices.OnlineGames.LoadUserGames());
        if (!loaded.Success) StatusMessage = loaded.Message;
        OnPropertyChanged(nameof(DatabaseInfo));
        UpdateSuggestions();
        UpdateQueries();
        await ScanGamesAsync();
    }

    partial void OnSearchTextChanged(string value) => UpdateSuggestions();

    private void UpdateSuggestions()
    {
        Suggestions.Clear();
        foreach (var g in GameCompatibility.Search(SearchText, 12)) Suggestions.Add(g);
        NotFound = SearchText.Trim().Length >= 3 && Suggestions.Count == 0
            ? $"„{SearchText.Trim()}“ ist nicht in der lokalen Datenbank. Mit „Online suchen“ (oder Enter) werden die Anforderungen von Steam geladen."
            : null;
    }

    partial void OnSelectedGameChanged(GameInfo? value) => Evaluate();
    partial void OnSelectedResolutionChanged(Choice<Resolution>? value) { UpdateEstimate(); UpdateQueries(); }
    partial void OnSelectedQualityChanged(Choice<Quality>? value) => UpdateEstimate();
    partial void OnSelectedUpscalerChanged(Choice<Upscaler>? value) => UpdateEstimate();
    partial void OnRayTracingChanged(bool value) { UpdateEstimate(); BuildFpsTable(); }

    private void Evaluate()
    {
        Checks.Clear();
        FpsTable.Clear();
        if (SelectedGame is not { } game || _facts is null)
        {
            GameTitle = null;
            SelectedSource = null;
            IsSelectedOnline = false;
            RatingText = "–";
            RatingState = DataState.NotAvailable;
            RatingSummary = null;
            return;
        }
        var pc = _facts.ToPcProfile();
        var r = GameCompatibility.Evaluate(game, pc);
        GameTitle = game.Name;
        IsSelectedOnline = game.Online;
        SelectedSource = game.Online
            ? $"Quelle: {game.Source ?? "Steam (online)"}"
            : $"Quelle: eingebaute Datenbank {GameCompatibility.DatabaseVersion}";
        RatingText = r.RatingText;
        RatingState = r.Status.ToDataState();
        RatingSummary = r.Summary;
        foreach (var c in r.Checks) Checks.Add(new RequirementRow(c.Area, c.Mine, c.Minimum, c.Recommended, c.Status.Label(), c.Status.ToDataState()));
        BuildFpsTable();
        UpdateEstimate();
    }

    private void BuildFpsTable()
    {
        FpsTable.Clear();
        if (SelectedGame is not { } game || _facts is null) return;
        var pc = _facts.ToPcProfile();
        var up = SelectedUpscaler?.Value ?? Upscaler.None;
        string Cell(Resolution res, Quality q) => GameCompatibility.EstimateFps(game, pc, res, q, RayTracing, up) is { FpsLow: { } l, FpsHigh: { } h } ? $"{l}–{h}" : "N/A";
        foreach (var res in Resolutions)
            FpsTable.Add(new FpsRow(res.Label, Cell(res.Value, Quality.Low), Cell(res.Value, Quality.Medium), Cell(res.Value, Quality.High), Cell(res.Value, Quality.Ultra)));
    }

    private void UpdateEstimate()
    {
        Targets.Clear();
        if (SelectedGame is not { } game || _facts is null || SelectedResolution is null || SelectedQuality is null)
        {
            FpsText = "–";
            FpsLimit = null;
            return;
        }
        var e = GameCompatibility.EstimateFps(game, _facts.ToPcProfile(), SelectedResolution.Value, SelectedQuality.Value, RayTracing, SelectedUpscaler?.Value ?? Upscaler.None);
        FpsText = e.Text;
        FpsLimit = e.Limit;
        foreach (var (target, reached) in GameCompatibility.Targets(e)) Targets.Add(new TargetRow($"{target} FPS", reached));
        BuildFpsTable();
    }

    // ---------------------------------------------------------------- Installierte Spiele (§17)

    [RelayCommand]
    private async Task ScanGamesAsync()
    {
        if (IsScanningGames) return;
        IsScanningGames = true;
        try
        {
            var games = await Task.Run(() =>
            {
                AppServices.OnlineGames.LoadUserGames();
                return InstalledGamesService.Detect(AppServices.Programs.GetPrograms().Select(p => (p.Name, p.Publisher, p.InstallLocation)));
            });
            _installed = games.ToList();
            RenderInstalled();
        }
        finally { IsScanningGames = false; }
    }

    private void RenderInstalled()
    {
        InstalledGames.Clear();
        MissingCount = _installed.Count(g => g.Known is null);
        var pc = _facts?.ToPcProfile();
        foreach (var g in _installed)
        {
            if (g.Known is null || pc is null)
            {
                InstalledGames.Add(new InstalledGameRow(g.Name, g.Launcher, Services.Common.Format.Bytes(g.SizeBytes),
                    "N/A", DataState.NotAvailable, "Keine Anforderungsdaten in der lokalen Datenbank", "–", "–"));
                continue;
            }
            var r = GameCompatibility.Evaluate(g.Known, pc);
            var (res, q, fps) = BestSetting(g.Known, pc);
            var weakest = r.Checks.Where(c => c.Status is CheckStatus.Fail or CheckStatus.Warning).Select(c => c.Area).FirstOrDefault();
            InstalledGames.Add(new InstalledGameRow(g.Name, g.Launcher, Services.Common.Format.Bytes(g.SizeBytes),
                r.RatingText, r.Status.ToDataState(), fps?.Text ?? "N/A", weakest ?? "kein Engpass erkannt",
                res is null ? "–" : $"{SearchQueries.ResolutionText(res.Value)} · {q} (ESTIMATED)"));
        }
        if (InstalledGames.Count == 0)
            InstalledGames.Add(new InstalledGameRow("Keine installierten Spiele gefunden", "Steam, Epic, Xbox, GOG, EA, Ubisoft", "–", "–", DataState.NotAvailable, "–", "–", "–"));
    }

    /// <summary>Installierte Spiele neu zuordnen und bewerten (nach Online-Abruf/Entfernen) – ohne die Bibliotheken neu einzulesen.</summary>
    private void RefreshInstalledMatches()
    {
        _installed = _installed.Select(InstalledGamesService.Rematch).ToList();
        RenderInstalled();
    }

    // ---------------------------------------------------------------- Online-Anforderungen (v1.3, Steam-Shop ohne Konto)

    private void SelectGame(GameInfo game)
    {
        SearchText = game.Name; // aktualisiert die Vorschläge
        if (!Suggestions.Contains(game)) Suggestions.Insert(0, game);
        SelectedGame = game;
    }

    /// <summary>„Online suchen“ / Enter im Suchfeld: Steam-Suche, besten Treffer laden, speichern, auswählen und bewerten.</summary>
    [RelayCommand]
    private async Task OnlineSearchAsync()
    {
        if (IsOnlineBusy) return;
        var name = SearchText.Trim();
        if (name.Length < 2) { StatusMessage = "Bitte zuerst einen Spielnamen eingeben."; return; }
        if (GameCompatibility.FindExact(name) is { } local)
        {
            SelectGame(local);
            StatusMessage = $"„{local.Name}“ ist bereits vorhanden – kein Online-Abruf nötig.";
            return;
        }

        IsOnlineBusy = true;
        OnlineProgress = $"Suche „{name}“ bei Steam …";
        _onlineCts = new CancellationTokenSource();
        try
        {
            var r = await AppServices.OnlineGames.FindAndSaveAsync(name, _onlineCts.Token);
            StatusMessage = r.Message;
            if (r.Game is { } game)
            {
                OnPropertyChanged(nameof(DatabaseInfo));
                SelectGame(game);
                RefreshInstalledMatches();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Online-Suche abgebrochen.";
        }
        catch (Exception ex)
        {
            AppLog.Error(Title, "Online-Suche fehlgeschlagen", ex);
            StatusMessage = "Online-Suche fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsOnlineBusy = false;
            OnlineProgress = null;
            _onlineCts?.Dispose();
            _onlineCts = null;
        }
    }

    /// <summary>Lädt für installierte Spiele ohne Daten die Anforderungen (Steam per AppID, andere per Name).</summary>
    [RelayCommand]
    private async Task FetchMissingAsync()
    {
        if (IsOnlineBusy || IsScanningGames) return;
        var missing = _installed.Where(g => g.Known is null).ToList();
        if (missing.Count == 0) { StatusMessage = "Alle erkannten Spiele haben bereits Anforderungsdaten."; return; }
        if (!Dialogs.Confirm("Anforderungen online laden",
                $"Für {missing.Count} Spiele ohne Daten werden die Anforderungen von store.steampowered.com geladen. " +
                "Gesendet wird nur der Spielname bzw. die Steam-AppID. Zwischen den Abrufen wird jeweils kurz gewartet. Fortfahren?"))
            return;

        IsOnlineBusy = true;
        OnlineProgress = "Starte Online-Abruf …";
        _onlineCts = new CancellationTokenSource();
        var progress = new Progress<string>(s => { if (IsOnlineBusy) OnlineProgress = s; });
        try
        {
            var r = await AppServices.OnlineGames.FetchMissingAsync(missing.Select(g => (g.Name, g.SteamAppId)), progress, _onlineCts.Token);
            StatusMessage = r.Message;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Online-Abruf abgebrochen.";
        }
        catch (Exception ex)
        {
            AppLog.Error(Title, "Online-Abruf fehlgeschlagen", ex);
            StatusMessage = "Online-Abruf fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            IsOnlineBusy = false;
            OnlineProgress = null;
            _onlineCts?.Dispose();
            _onlineCts = null;
            OnPropertyChanged(nameof(DatabaseInfo));
            UpdateSuggestions();
            RefreshInstalledMatches();
        }
    }

    [RelayCommand]
    private void CancelOnline() => _onlineCts?.Cancel();

    /// <summary>Entfernt das ausgewählte, online gespeicherte Spiel (nach Bestätigung).</summary>
    [RelayCommand]
    private void RemoveSavedGame()
    {
        if (SelectedGame is not { Online: true } game) return;
        if (!Dialogs.Confirm("Gespeichertes Spiel entfernen",
                $"„{game.Name}“ aus den online gespeicherten Spielen entfernen? Die Anforderungen können jederzeit erneut geladen werden."))
            return;
        var r = AppServices.OnlineGames.Remove(game);
        ShowResult(r);
        if (!r.Success) return;
        SelectedGame = null;
        OnPropertyChanged(nameof(DatabaseInfo));
        UpdateSuggestions();
        RefreshInstalledMatches();
    }

    /// <summary>Höchste Auflösung/Qualität, bei der die Schätzung mindestens 60 FPS (E-Sport: 144 FPS) erreicht.</summary>
    private static (Resolution? Res, Quality? Q, FpsEstimate? Fps) BestSetting(GameInfo game, PcProfile pc)
    {
        var target = game.Esports ? 144 : 60;
        foreach (var res in new[] { Resolution.P2160, Resolution.P1440, Resolution.P1080 })
            foreach (var q in new[] { Quality.Ultra, Quality.High, Quality.Medium, Quality.Low })
            {
                var e = GameCompatibility.EstimateFps(game, pc, res, q, false, Upscaler.None);
                if (e.FpsLow is { } low && low >= target) return (res, q, e);
            }
        var lowest = GameCompatibility.EstimateFps(game, pc, Resolution.P1080, Quality.Low, false, Upscaler.None);
        return lowest.FpsLow is null ? (null, null, null) : (Resolution.P1080, Quality.Low, lowest);
    }

    // ---------------------------------------------------------------- Web-Suche (§15)

    private void UpdateQueries()
    {
        GameQueries.Clear();
        if (_facts is null) return;
        foreach (var q in SearchQueries.GameQueries(_facts.Facts.CpuName, _facts.Facts.GpuName, _facts.Facts.RamGb, SelectedResolution?.Value ?? Resolution.P1080))
            GameQueries.Add(q);
    }

    [RelayCommand]
    private void OpenQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        ShowResult(UriLauncher.Open(SearchQueries.SearchUrl(query)));
    }

    [RelayCommand]
    private void SearchRequirements()
    {
        var name = SelectedGame?.Name ?? SearchText.Trim();
        if (name.Length < 2) { StatusMessage = "Bitte zuerst einen Spielnamen eingeben."; return; }
        ShowResult(UriLauncher.Open(SearchQueries.SearchUrl(SearchQueries.GameRequirementsQuery(name))));
    }
}
