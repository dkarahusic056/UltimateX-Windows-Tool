using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Network;

namespace UltimateX.Services.Gaming;

/// <summary>Ergebnis eines Online-Abrufs: Spiel (bei Erfolg), Meldung für die Oberfläche und die Suchtreffer.</summary>
public sealed record OnlineGameResult(bool Success, GameInfo? Game, string Message, IReadOnlyList<SteamSearchItem> Candidates)
{
    public static OnlineGameResult Fail(string message, IReadOnlyList<SteamSearchItem>? candidates = null) => new(false, null, message, candidates ?? []);
}

/// <summary>Zusammenfassung von „Fehlende Anforderungen online laden“.</summary>
public sealed record OnlineBatchResult(int Requested, int Loaded, int NotFound, int Failed, bool Cancelled, string Message);

/// <summary>
/// v1.3: Lädt Systemanforderungen über die öffentliche Steam-Shop-API (ohne Steam-Konto, ohne Anmeldung) und speichert sie lokal
/// in %LOCALAPPDATA%\UltimateX\games-user.json. Gesendet wird ausschließlich der Spielname (Suche) bzw. die Steam-AppID
/// an store.steampowered.com – keine Hardware-, Konto- oder Gerätedaten. Nur HTTPS, keine Umleitungen (SecureHttp),
/// Größenlimit, Zeitlimit, abbrechbar; zwischen Abrufen wird eine Pause eingehalten (Rate-Limit von Steam).
/// </summary>
public sealed class GameRequirementsOnlineService
{
    private const int MaxResponseBytes = 4 * 1024 * 1024;
    private const int MaxFileBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>Pause zwischen zwei Abrufen bei vielen Spielen (Steam begrenzt die Anzahl der Abfragen).</summary>
    public static readonly TimeSpan BatchPause = TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan SinglePause = TimeSpan.FromMilliseconds(300);

    private readonly SemaphoreSlim _network = new(1, 1);
    private readonly object _sync = new();
    private readonly string _filePath;
    private List<GameInfo> _games = [];
    private bool _loaded;
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public GameRequirementsOnlineService() : this(Path.Combine(AppPaths.UserDataRoot, "games-user.json")) { }

    /// <summary>Für Tests: eigener Speicherort.</summary>
    internal GameRequirementsOnlineService(string filePath) => _filePath = filePath;

    public string FilePath => _filePath;

    /// <summary>Hinweis für die Oberfläche: was gesendet wird.</summary>
    public const string PrivacyNote = "Beim Online-Abruf wird nur der Spielname bzw. die Steam-AppID an store.steampowered.com gesendet – keine Hardware-, Konto- oder Gerätedaten. Ein Steam-Konto ist nicht nötig.";

    // ------------------------------------------------------------------ Speichern / Laden

    /// <summary>Gespeicherte Spiele (lädt die Datei beim ersten Zugriff).</summary>
    public IReadOnlyList<GameInfo> SavedGames
    {
        get
        {
            EnsureLoaded();
            lock (_sync) return _games.ToList();
        }
    }

    /// <summary>Lädt games-user.json (nur beim ersten Aufruf) und übergibt die Spiele an <see cref="GameCompatibility.SetUserGames"/>.</summary>
    public OperationResult LoadUserGames()
    {
        lock (_sync)
        {
            if (_loaded) return OperationResult.Ok($"{_games.Count} gespeicherte Online-Spiele geladen.");
            _loaded = true;
            try
            {
                if (!File.Exists(_filePath))
                {
                    _games = [];
                    GameCompatibility.SetUserGames(_games);
                    return OperationResult.Ok("Noch keine Online-Spiele gespeichert.");
                }
                var info = new FileInfo(_filePath);
                if (info.Length > MaxFileBytes)
                    return Broken("Die Datei mit gespeicherten Online-Spielen ist ungewöhnlich groß und wurde nicht geladen.", null);
                _games = UserGameStore.Deserialize(File.ReadAllText(_filePath, Encoding.UTF8)).ToList();
                GameCompatibility.SetUserGames(_games);
                AppLog.Info("Games", $"{_games.Count} gespeicherte Online-Spiele geladen");
                return OperationResult.Ok($"{_games.Count} gespeicherte Online-Spiele geladen.");
            }
            catch (JsonException ex)
            {
                return Broken("Die gespeicherten Online-Spiele sind beschädigt und wurden nicht geladen (Sicherungskopie angelegt).", ex);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                AppLog.Warn("Games", "games-user.json nicht lesbar", ex.Message);
                _games = [];
                GameCompatibility.SetUserGames(_games);
                return OperationResult.Fail("Die gespeicherten Online-Spiele konnten nicht gelesen werden: " + ex.Message);
            }
        }
    }

    private OperationResult Broken(string message, Exception? ex)
    {
        AppLog.Warn("Games", "games-user.json ungültig", ex?.Message);
        try { File.Copy(_filePath, _filePath + ".broken", overwrite: true); }
        catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { }
        _games = [];
        GameCompatibility.SetUserGames(_games);
        return OperationResult.Fail(message);
    }

    private void EnsureLoaded()
    {
        bool loaded;
        lock (_sync) loaded = _loaded;
        if (!loaded) LoadUserGames();
    }

    /// <summary>Speichert atomar (temporäre Datei + Austausch). Aufruf nur innerhalb von <see cref="_sync"/>.</summary>
    private void SaveLocked()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, UserGameStore.Serialize(_games), new UTF8Encoding(false));
            File.Move(tmp, _filePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Fügt ein Spiel hinzu bzw. ersetzt einen gespeicherten Eintrag mit gleicher AppID/gleichem Namen.</summary>
    private OperationResult Store(GameInfo game)
    {
        EnsureLoaded();
        lock (_sync)
        {
            var key = GameCompatibility.Normalize(game.Name);
            var updated = _games.Where(g => !(game.SteamAppId > 0 && g.SteamAppId == game.SteamAppId) && GameCompatibility.Normalize(g.Name) != key).ToList();
            updated.Add(game);
            var previous = _games;
            _games = updated.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            try
            {
                SaveLocked();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _games = previous;
                AppLog.Warn("Games", "games-user.json konnte nicht gespeichert werden", ex.Message);
                return OperationResult.Fail("Die Anforderungen wurden abgerufen, konnten aber nicht gespeichert werden: " + ex.Message);
            }
            GameCompatibility.SetUserGames(_games);
            return OperationResult.Ok($"„{game.Name}“ gespeichert.");
        }
    }

    /// <summary>Entfernt ein gespeichertes Online-Spiel (eingebaute Spiele können nicht entfernt werden).</summary>
    public OperationResult Remove(GameInfo game)
    {
        if (!game.Online) return OperationResult.Fail("Eingebaute Spiele können nicht entfernt werden.");
        EnsureLoaded();
        lock (_sync)
        {
            var key = GameCompatibility.Normalize(game.Name);
            var updated = _games.Where(g => !(game.SteamAppId > 0 && g.SteamAppId == game.SteamAppId) && GameCompatibility.Normalize(g.Name) != key).ToList();
            if (updated.Count == _games.Count) return OperationResult.Fail($"„{game.Name}“ ist nicht gespeichert.");
            var previous = _games;
            _games = updated;
            try
            {
                SaveLocked();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _games = previous;
                return OperationResult.Fail("Das gespeicherte Spiel konnte nicht entfernt werden: " + ex.Message);
            }
            GameCompatibility.SetUserGames(_games);
            AppLog.Info("Games", $"Online-Spiel entfernt: {game.Name}");
            return OperationResult.Ok($"„{game.Name}“ wurde aus den gespeicherten Online-Spielen entfernt.");
        }
    }

    // ------------------------------------------------------------------ Netzwerk

    private async Task ThrottleAsync(TimeSpan minGap, CancellationToken ct)
    {
        var wait = _lastRequestUtc + minGap - DateTime.UtcNow;
        if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
    }

    /// <summary>Ruft eine Steam-URL ab. Nur HTTPS, nur store.steampowered.com, Zeit- und Größenlimit. Fehler → <see cref="OnlineRequestException"/>.</summary>
    private async Task<string> GetAsync(string url, TimeSpan minGap, CancellationToken ct)
    {
        var uri = SecureHttp.RequireHttps(url);
        if (!string.Equals(uri.Host, SteamRequirements.StoreHost, StringComparison.OrdinalIgnoreCase))
            throw new OnlineRequestException("Nicht erlaubter Host für den Abruf von Spielanforderungen.");

        await _network.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await ThrottleAsync(minGap, ct).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);
            try
            {
                return await SecureHttp.GetSmallStringAsync(uri.AbsoluteUri, MaxResponseBytes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new OnlineRequestException("Steam hat nicht rechtzeitig geantwortet (Zeitüberschreitung). Bitte später erneut versuchen.");
            }
            catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
            {
                throw new OnlineRequestException(ex.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "Steam begrenzt gerade die Anzahl der Abfragen (HTTP 429). Bitte in einigen Minuten erneut versuchen.",
                    HttpStatusCode.Forbidden => "Steam hat die Anfrage abgelehnt (HTTP 403). Bitte später erneut versuchen.",
                    { } code when (int)code is >= 300 and < 400 => $"Steam hat auf eine andere Adresse umgeleitet (HTTP {(int)code}). Umleitungen werden aus Sicherheitsgründen nicht befolgt.",
                    { } code => $"Steam ist nicht erreichbar (HTTP {(int)code}).",
                    null => "Keine Verbindung zu store.steampowered.com. Bitte Internetverbindung prüfen.",
                }, ex.StatusCode == HttpStatusCode.TooManyRequests);
            }
            catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException && !ct.IsCancellationRequested)
            {
                throw new OnlineRequestException("Die Verbindung zu store.steampowered.com ist fehlgeschlagen: " + ex.Message);
            }
            finally
            {
                _lastRequestUtc = DateTime.UtcNow;
            }
        }
        finally
        {
            _network.Release();
        }
    }

    private static string CleanTerm(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sb = new StringBuilder();
        foreach (var c in name.Trim())
            if (!char.IsControl(c)) sb.Append(c);
        var s = sb.ToString().Trim();
        return s.Length > 100 ? s[..100] : s;
    }

    /// <summary>Sucht im Steam-Shop nach einem Spielnamen (sendet nur den Namen). Fehler → <see cref="OnlineRequestException"/>.</summary>
    public async Task<IReadOnlyList<SteamSearchItem>> SearchAsync(string name, CancellationToken ct = default)
        => await SearchAsync(name, SinglePause, ct).ConfigureAwait(false);

    private async Task<IReadOnlyList<SteamSearchItem>> SearchAsync(string name, TimeSpan gap, CancellationToken ct)
    {
        var term = CleanTerm(name);
        if (term.Length < 2) throw new OnlineRequestException("Bitte mindestens zwei Zeichen als Spielnamen eingeben.");
        var json = await GetAsync(SteamRequirements.SearchUrl(term), gap, ct).ConfigureAwait(false);
        return SteamRequirements.ParseSearch(json);
    }

    /// <summary>Lädt die Anforderungen zu einer Steam-AppID (sendet nur die AppID). Fehler → <see cref="OnlineRequestException"/>.</summary>
    public async Task<GameInfo> FetchAsync(int appId, CancellationToken ct = default)
        => await FetchAsync(appId, SinglePause, ct).ConfigureAwait(false);

    private async Task<GameInfo> FetchAsync(int appId, TimeSpan gap, CancellationToken ct)
    {
        if (appId is <= 0 or > 99_999_999) throw new OnlineRequestException("Ungültige Steam-AppID.");
        var json = await GetAsync(SteamRequirements.AppDetailsUrl(appId), gap, ct).ConfigureAwait(false);
        var result = SteamRequirements.ParseAppDetails(json, appId, DateTime.Now);
        if (result.Game is null) throw new OnlineRequestException(result.Error ?? "Steam hat keine verwertbaren Anforderungen geliefert.");
        return result.Game;
    }

    private static GameInfo? BuiltInByAppId(int appId)
        => appId > 0 ? GameCompatibility.BuiltInGames.FirstOrDefault(g => g.SteamAppId == appId) : null;

    /// <summary>Sucht den Namen bei Steam, wählt den besten Treffer, lädt die Anforderungen und speichert sie lokal.</summary>
    public Task<OnlineGameResult> FindAndSaveAsync(string name, CancellationToken ct = default) => FindAndSaveAsync(name, SinglePause, ct);

    private async Task<OnlineGameResult> FindAndSaveAsync(string name, TimeSpan gap, CancellationToken ct)
    {
        EnsureLoaded();
        IReadOnlyList<SteamSearchItem> candidates = [];
        try
        {
            candidates = await SearchAsync(name, gap, ct).ConfigureAwait(false);
            if (candidates.Count == 0)
                return OnlineGameResult.Fail($"Steam hat zu „{CleanTerm(name)}“ kein Spiel gefunden. Spiele außerhalb von Steam (z. B. Valorant) sind dort nicht gelistet.");
            var best = SteamRequirements.PickBestMatch(candidates, name);
            if (best is null)
                return OnlineGameResult.Fail($"Kein eindeutiger Treffer für „{CleanTerm(name)}“. Gefunden: {string.Join(", ", candidates.Take(5).Select(c => c.Name))}.", candidates);
            var result = await FetchAndSaveCoreAsync(best.AppId, gap, ct).ConfigureAwait(false);
            return result with { Candidates = candidates };
        }
        catch (OnlineRequestException ex)
        {
            AppLog.Warn("Games", "Online-Suche fehlgeschlagen", ex.Message);
            return OnlineGameResult.Fail(ex.Message, candidates);
        }
    }

    /// <summary>Lädt die Anforderungen zu einer Steam-AppID und speichert sie lokal.</summary>
    public async Task<OnlineGameResult> FetchAndSaveAsync(int appId, CancellationToken ct = default)
    {
        EnsureLoaded();
        try
        {
            return await FetchAndSaveCoreAsync(appId, SinglePause, ct).ConfigureAwait(false);
        }
        catch (OnlineRequestException ex)
        {
            AppLog.Warn("Games", "Online-Abruf fehlgeschlagen", ex.Message);
            return OnlineGameResult.Fail(ex.Message);
        }
    }

    private async Task<OnlineGameResult> FetchAndSaveCoreAsync(int appId, TimeSpan gap, CancellationToken ct)
    {
        if (BuiltInByAppId(appId) is { } builtIn)
            return new OnlineGameResult(true, builtIn, $"„{builtIn.Name}“ ist bereits in der eingebauten Datenbank enthalten.", []);
        var game = await FetchAsync(appId, gap, ct).ConfigureAwait(false);
        if (GameCompatibility.BuiltInGames.FirstOrDefault(g => GameCompatibility.Normalize(g.Name) == GameCompatibility.Normalize(game.Name)) is { } sameName)
            return new OnlineGameResult(true, sameName, $"„{sameName.Name}“ ist bereits in der eingebauten Datenbank enthalten.", []);
        var stored = Store(game);
        AppLog.Info("Games", $"Anforderungen online geladen: {game.Name} (AppID {appId.ToString(CultureInfo.InvariantCulture)})");
        return stored.Success
            ? new OnlineGameResult(true, game, $"Anforderungen für „{game.Name}“ von Steam geladen und gespeichert.", [])
            : new OnlineGameResult(true, game, stored.Message, []);
    }

    /// <summary>
    /// Lädt für mehrere Spiele ohne Daten die Anforderungen (Steam-Spiele per AppID, andere per Name). Zwischen den Abrufen
    /// wird <see cref="BatchPause"/> gewartet. Bei HTTP 429 wird abgebrochen. Abbrechbar über <paramref name="ct"/>.
    /// </summary>
    public async Task<OnlineBatchResult> FetchMissingAsync(IEnumerable<(string Name, int? SteamAppId)> games, IProgress<string>? progress, CancellationToken ct)
    {
        EnsureLoaded();
        var list = games.Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .GroupBy(g => g.SteamAppId is > 0 ? "id:" + g.SteamAppId.Value.ToString(CultureInfo.InvariantCulture) : GameCompatibility.Normalize(g.Name))
            .Select(g => g.First()).ToList();
        int loaded = 0, notFound = 0, failed = 0, index = 0;
        var cancelled = false;
        string? stopReason = null;
        foreach (var (name, appId) in list)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            index++;
            progress?.Report($"Lade {index}/{list.Count}: {name} …");
            try
            {
                OnlineGameResult r;
                if (appId is > 0)
                {
                    try { r = await FetchAndSaveCoreAsync(appId.Value, BatchPause, ct).ConfigureAwait(false); }
                    catch (OnlineRequestException ex) when (!ex.RateLimited)
                    {
                        // AppID ohne verwertbare Daten (z. B. Region) → Versuch über den Namen.
                        AppLog.Info("Games", $"AppID {appId.Value.ToString(CultureInfo.InvariantCulture)} ohne Daten, Suche per Name", ex.Message);
                        r = await FindAndSaveCoreAsync(name, ct).ConfigureAwait(false);
                    }
                }
                else r = await FindAndSaveCoreAsync(name, ct).ConfigureAwait(false);

                if (r.Success) loaded++;
                else notFound++;
            }
            catch (OnlineRequestException ex) when (ex.RateLimited)
            {
                failed++;
                stopReason = ex.Message;
                break;
            }
            catch (OnlineRequestException ex)
            {
                failed++;
                AppLog.Warn("Games", $"Online-Abruf für {name} fehlgeschlagen", ex.Message);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
        }

        var message = $"{loaded} von {list.Count} Spielen online ergänzt, {notFound} nicht gefunden, {failed} Fehler.";
        if (cancelled) message = "Abgebrochen. " + message;
        if (stopReason is not null) message = stopReason + " " + message;
        AppLog.Info("Games", "Fehlende Anforderungen online geladen", message);
        return new OnlineBatchResult(list.Count, loaded, notFound, failed, cancelled, message);
    }

    /// <summary>Suche + Abruf für den Stapelbetrieb (Pause <see cref="BatchPause"/>); Netzwerkfehler werden weitergereicht.</summary>
    private async Task<OnlineGameResult> FindAndSaveCoreAsync(string name, CancellationToken ct)
    {
        var candidates = await SearchAsync(name, BatchPause, ct).ConfigureAwait(false);
        var best = SteamRequirements.PickBestMatch(candidates, name);
        if (best is null) return OnlineGameResult.Fail($"Kein passender Steam-Eintrag für „{name}“.", candidates);
        return await FetchAndSaveCoreAsync(best.AppId, BatchPause, ct).ConfigureAwait(false);
    }
}

/// <summary>Fehler beim Online-Abruf mit verständlicher deutscher Meldung.</summary>
public sealed class OnlineRequestException : Exception
{
    public OnlineRequestException(string message, bool rateLimited = false) : base(message) => RateLimited = rateLimited;

    public OnlineRequestException() { }

    public OnlineRequestException(string message, Exception innerException) : base(message, innerException) { }

    public bool RateLimited { get; }
}
