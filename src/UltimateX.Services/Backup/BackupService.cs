using System.Text.Json;
using UltimateX.Core.Backup;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Services.Backup;

/// <summary>Ein erkanntes Backup-Ziel (Laufwerk/Ordner mit Markierungsdatei).</summary>
public sealed record BackupTargetStatus(bool Available, string? CurrentPath, string Message);

/// <summary>
/// Backup-Center: Profile (JSON), Verlauf, Zeitplan und Zielerkennung. Die eigentliche Arbeit macht <see cref="FileBackupEngine"/>.
///
/// Zeitpläne laufen nur, solange ULTIMATE X geöffnet ist (auch minimiert/im Infobereich): Ein Timer prüft alle 60 Sekunden
/// fällige Profile und angesteckte Zielplatten (DriveInfo-Abfrage). Die Zielplatte wird über die Markierungsdatei
/// UltimateX-Backup\target.json (ID) erkannt – auch wenn Windows ihr einen anderen Laufwerksbuchstaben gibt.
/// </summary>
public sealed class BackupService : IDisposable
{
    private static readonly JsonSerializerOptions Json = FileBackupEngine.Json;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _runLock = new(1, 1);
    private readonly HashSet<string> _presentTargets = new(StringComparer.OrdinalIgnoreCase);
    private Timer? _timer;
    private int _ticking;
    private BackupStoreData _data;
    private CancellationTokenSource? _scheduledCts;

    public BackupService() : this(Path.Combine(AppPaths.UserDataRoot, "backup-profiles.json"), Path.Combine(AppPaths.UserDataRoot, "backup-history.json"))
    {
    }

    /// <summary>Für Tests: eigene Ablageorte.</summary>
    public BackupService(string profilesFile, string historyFile, FileBackupEngine? engine = null)
    {
        ProfilesFile = profilesFile;
        HistoryFile = historyFile;
        Engine = engine ?? new FileBackupEngine();
        _data = LoadStore(profilesFile) ?? new BackupStoreData();
    }

    public string ProfilesFile { get; }
    public string HistoryFile { get; }
    public FileBackupEngine Engine { get; }

    /// <summary>true, solange ein Backup (manuell oder geplant) läuft.</summary>
    public bool IsRunning => _runLock.CurrentCount == 0;

    /// <summary>Wird nach jeder geplanten Sicherung ausgelöst (Hintergrund-Thread!).</summary>
    public event Action<BackupProfile, BackupRunResult>? ScheduledRunCompleted;

    /// <summary>Wird ausgelöst, wenn Profile/Verlauf sich geändert haben (Hintergrund-Thread möglich).</summary>
    public event Action? Changed;

    /// <summary>Fortschritt einer geplanten Sicherung (Hintergrund-Thread).</summary>
    public event Action<BackupProfile, BackupProgress>? ScheduledProgress;

    // ======================================================================================
    // Profile
    // ======================================================================================

    public BackupSettings Settings { get { lock (_gate) return _data.Settings; } }

    /// <summary>Kopien aller Profile.</summary>
    public IReadOnlyList<BackupProfile> GetProfiles()
    {
        lock (_gate) return _data.Profiles.Select(Clone).ToList();
    }

    /// <summary>Kopie eines Profils (zum Bearbeiten – Änderungen erst mit <see cref="SaveProfile"/> übernehmen).</summary>
    public BackupProfile? GetProfile(string id)
    {
        lock (_gate)
        {
            var p = _data.Profiles.FirstOrDefault(x => x.Id == id);
            return p is null ? null : Clone(p);
        }
    }

    public static BackupProfile Clone(BackupProfile p)
        => JsonSerializer.Deserialize<BackupProfile>(JsonSerializer.Serialize(p, Json), Json)!;

    /// <summary>Legt ein Profil an oder ersetzt es (gleiche Id). Validiert Name, Quellen, Ausschlüsse, Aufbewahrung.</summary>
    public OperationResult SaveProfile(BackupProfile profile)
    {
        var error = Validate(profile);
        if (error is not null) return OperationResult.Fail(error);
        lock (_gate)
        {
            var idx = _data.Profiles.FindIndex(p => p.Id == profile.Id);
            if (string.IsNullOrEmpty(profile.FolderName) || !BackupPaths.IsValidFolderName(profile.FolderName))
                profile.FolderName = BackupPaths.MakeFolderName(profile.Name, _data.Profiles.Where(p => p.Id != profile.Id).Select(p => p.FolderName));
            else if (_data.Profiles.Any(p => p.Id != profile.Id && p.FolderName.Equals(profile.FolderName, StringComparison.OrdinalIgnoreCase)))
                profile.FolderName = BackupPaths.MakeFolderName(profile.FolderName, _data.Profiles.Select(p => p.FolderName));
            if (idx >= 0)
            {
                // Zielwechsel → neue Platte, alte ID gilt nicht mehr
                // Laufzeitstatus kommt immer aus dem gespeicherten Profil (die bearbeitete Kopie kann veraltet sein).
                var stored = _data.Profiles[idx];
                profile.LastAttemptUtc = stored.LastAttemptUtc;
                profile.LastSuccessUtc = stored.LastSuccessUtc;
                profile.LastResult = stored.LastResult;
                profile.CreatedUtc = stored.CreatedUtc;
                profile.TargetId = string.Equals(stored.TargetPath, profile.TargetPath, StringComparison.OrdinalIgnoreCase) ? stored.TargetId : null;
                _data.Profiles[idx] = Clone(profile);
            }
            else
            {
                _data.Profiles.Add(Clone(profile));
            }
            SaveStore();
        }
        Changed?.Invoke();
        AppLog.Info("Backup", $"Backup-Profil gespeichert: {profile.Name}", $"{profile.Sources.Count} Quellen · Ziel {profile.TargetPath} · {profile.Schedule.Kind}");
        return OperationResult.Ok("Profil gespeichert.");
    }

    /// <summary>Entfernt nur das Profil – vorhandene Sicherungen auf der Zielplatte bleiben unangetastet.</summary>
    public void DeleteProfile(string id)
    {
        lock (_gate)
        {
            _data.Profiles.RemoveAll(p => p.Id == id);
            SaveStore();
        }
        Changed?.Invoke();
    }

    public void UpdateSettings(Action<BackupSettings> change)
    {
        lock (_gate)
        {
            change(_data.Settings);
            _data.Settings.OverdueDays = Math.Clamp(_data.Settings.OverdueDays, 1, 365);
            _data.Settings.VerifySampleSize = Math.Clamp(_data.Settings.VerifySampleSize, 0, 1000);
            SaveStore();
        }
        Changed?.Invoke();
    }

    public static string? Validate(BackupProfile p)
    {
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 60 || p.Name.Any(char.IsControl)) return "Bitte einen Profilnamen (max. 60 Zeichen) eingeben.";
        if (p.Sources.Count == 0 && p.Templates.Count == 0) return "Bitte mindestens eine Quelle (Ordner oder Vorlage) wählen.";
        if (p.Templates.Any(t => BackupTemplates.SourceTemplates.All(x => x.Id != t))) return "Unbekannte Vorlage.";
        foreach (var s in p.Sources)
        {
            if (string.IsNullOrWhiteSpace(s.Path) || !Path.IsPathFullyQualified(s.Path) || s.Path.StartsWith(@"\\", StringComparison.Ordinal))
                return $"Ungültige Quelle (nur lokale Pfade): {s.Path}";
        }
        if (string.IsNullOrWhiteSpace(p.TargetPath) || !Path.IsPathFullyQualified(p.TargetPath) || p.TargetPath.StartsWith(@"\\", StringComparison.Ordinal))
            return "Bitte ein lokales Ziel wählen (z. B. externe Festplatte E:\\).";
        if (PathGuard.IsProtected(p.TargetPath)) return "Das Ziel liegt in einem geschützten Systemordner.";
        if (p.KeepVersions is < BackupProfile.MinKeep or > BackupProfile.MaxKeep) return "Aufbewahrung: 1 bis 365 Versionen.";
        if (p.Exclusions.Any(e => ExclusionMatcher.Normalize(e) is null)) return "Ein Ausschlussmuster ist ungültig.";
        var probe = new BackupProfile { Sources = BackupTemplates.ExpandSources(p, KnownFolders.Current()), FolderName = "Profil" };
        return FileBackupEngine.ValidateProfileAgainstTarget(probe, p.TargetPath) is { } err && err != FileBackupEngine.TargetUnavailableMessage ? err : null;
    }

    public OperationResult Export(string path)
    {
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_data, Json);
            File.WriteAllText(path, json);
            return OperationResult.Ok("Profile exportiert: " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return OperationResult.Fail("Export fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Importiert Profile (neue IDs bei Kollision, Status wird zurückgesetzt). Ungültige Profile werden übersprungen.</summary>
    public OperationResult Import(string path)
    {
        BackupStoreData? imported;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 5 * 1024 * 1024) return OperationResult.Fail("Datei nicht gefunden oder zu groß.");
            imported = JsonSerializer.Deserialize<BackupStoreData>(File.ReadAllText(path), Json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return OperationResult.Fail("Import fehlgeschlagen: " + ex.Message);
        }
        if (imported?.Profiles is null) return OperationResult.Fail("Keine Profile in der Datei.");
        int ok = 0, bad = 0;
        foreach (var p in imported.Profiles)
        {
            p.Sources ??= [];
            p.Templates ??= [];
            p.Exclusions ??= [];
            p.Schedule ??= new BackupScheduleSettings();
            p.LastAttemptUtc = null;
            p.LastSuccessUtc = null;
            p.LastResult = null;
            p.TargetId = null;
            p.CreatedUtc = DateTime.UtcNow;
            lock (_gate)
            {
                if (_data.Profiles.Any(x => x.Id == p.Id)) p.Id = Guid.NewGuid().ToString("N");
            }
            p.FolderName = string.Empty;
            if (SaveProfile(p).Success) ok++; else bad++;
        }
        return OperationResult.Ok($"{ok} Profil(e) importiert{(bad > 0 ? $", {bad} ungültig übersprungen" : "")}.");
    }

    // ======================================================================================
    // Ziel-Erkennung (Markierungsdatei)
    // ======================================================================================

    /// <summary>
    /// Sucht die Zielplatte des Profils: zuerst am gespeicherten Pfad, dann unter demselben Ordner auf allen anderen bereiten
    /// Laufwerken (anderer Buchstabe). Maßgeblich ist die ID in UltimateX-Backup\target.json.
    /// </summary>
    public BackupTargetStatus ResolveTarget(BackupProfile profile, IEnumerable<string>? driveRoots = null)
    {
        if (string.IsNullOrWhiteSpace(profile.TargetPath) || !Path.IsPathFullyQualified(profile.TargetPath))
            return new(false, null, "Kein Ziel festgelegt.");
        var marker = ReadMarker(profile.TargetPath);
        if (profile.TargetId is null)
            return Directory.Exists(profile.TargetPath)
                ? new(true, profile.TargetPath, marker is null ? "Ziel bereit (wird beim ersten Backup markiert)." : "Ziel bereit.")
                : new(false, null, "Ziel nicht angeschlossen.");
        if (marker?.Id == profile.TargetId) return new(true, profile.TargetPath, "Ziel bereit.");

        var relative = profile.TargetPath.Length > 3 ? profile.TargetPath[3..] : string.Empty;
        foreach (var root in driveRoots ?? ReadyDriveRoots())
        {
            var candidate = relative.Length == 0 ? root : Path.Combine(root, relative);
            if (candidate.Equals(profile.TargetPath, StringComparison.OrdinalIgnoreCase)) continue;
            if (ReadMarker(candidate)?.Id == profile.TargetId)
                return new(true, candidate, $"Zielplatte unter neuem Laufwerksbuchstaben gefunden: {candidate}");
        }
        return marker is null
            ? new(false, null, "Zielplatte nicht angeschlossen.")
            : new(false, null, "Unter dem Zielpfad ist eine ANDERE Backup-Platte angeschlossen (Markierung passt nicht). Aus Sicherheitsgründen wird dort nicht gesichert.");
    }

    public static BackupTargetMarker? ReadMarker(string targetPath)
    {
        try
        {
            var path = BackupPaths.MarkerPath(targetPath);
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length > 64 * 1024) return null;
            var m = JsonSerializer.Deserialize<BackupTargetMarker>(File.ReadAllText(path), Json);
            return m is not null && m.Id.Length is >= 8 and <= 64 && m.Id.All(char.IsAsciiLetterOrDigit) ? m : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Legt die Markierungsdatei an (falls nicht vorhanden) und liefert deren ID.</summary>
    public static string EnsureMarker(string targetPath)
    {
        var existing = ReadMarker(targetPath);
        if (existing is not null) return existing.Id;
        var root = BackupPaths.BackupRoot(targetPath);
        Directory.CreateDirectory(root);
        var drive = Path.GetPathRoot(Path.GetFullPath(targetPath))!;
        if (!CommandCatalog.IsReparseFreeChain(drive, root)) throw new IOException("Der Backup-Ordner ist eine Verknüpfung (Junction/Symlink).");
        var marker = new BackupTargetMarker { Computer = Environment.MachineName };
        File.WriteAllText(BackupPaths.MarkerPath(targetPath), JsonSerializer.Serialize(marker, Json));
        return marker.Id;
    }

    private static IEnumerable<string> ReadyDriveRoots()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { drives = []; }
        var list = new List<string>();
        foreach (var d in drives)
        {
            try
            {
                if (d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable) list.Add(d.RootDirectory.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return list;
    }

    // ======================================================================================
    // Ausführen
    // ======================================================================================

    /// <summary>Führt ein Profil aus (manuell oder geplant). Es läuft immer nur eine Sicherung gleichzeitig.</summary>
    public async Task<BackupRunResult> RunProfileAsync(string profileId, IProgress<BackupProgress>? progress, CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var profile = GetProfile(profileId);
        if (profile is null) return Failed("Profil nicht gefunden.");
        if (!await _runLock.WaitAsync(0, CancellationToken.None).ConfigureAwait(false)) return Failed("Es läuft bereits eine Sicherung.");
        try
        {
            var target = ResolveTarget(profile);
            if (!target.Available || target.CurrentPath is null)
            {
                var r = Failed(target.Message);
                Record(profile, r, started, profile.TargetPath, countAsAttempt: false);
                return r;
            }
            try
            {
                var id = EnsureMarker(target.CurrentPath);
                if (profile.TargetId is null)
                {
                    lock (_gate)
                    {
                        profile.TargetId = id;
                        var live = _data.Profiles.FirstOrDefault(x => x.Id == profile.Id);
                        if (live is not null) live.TargetId = id;
                        SaveStore();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                var r = Failed("Ziel ist nicht beschreibbar: " + ex.Message);
                Record(profile, r, started, target.CurrentPath, countAsAttempt: true);
                return r;
            }

            // Vorlagen bei jedem Lauf neu auflösen (z. B. neu angelegtes Browserprofil, OneDrive erst später eingerichtet).
            profile.Sources = BackupTemplates.ExpandSources(profile, KnownFolders.Current());
            profile.Templates = [];
            var settings = Settings;
            var result = await Engine.RunAsync(profile, target.CurrentPath, new BackupRunOptions(settings.StoreHashes, settings.VerifySampleSize), progress, ct).ConfigureAwait(false);
            Record(profile, result, started, target.CurrentPath, countAsAttempt: true);
            return result;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private static BackupRunResult Failed(string message)
        => new(false, BackupVersionStatus.Failed, message, null, 0, 0, 0, 0, 0, 0, [], null, [], TimeSpan.Zero);

    private void Record(BackupProfile profile, BackupRunResult r, DateTime startedUtc, string target, bool countAsAttempt)
    {
        lock (_gate)
        {
            var p = _data.Profiles.FirstOrDefault(x => x.Id == profile.Id);
            if (p is not null)
            {
                if (countAsAttempt) p.LastAttemptUtc = startedUtc;
                if (r.Success) p.LastSuccessUtc = DateTime.UtcNow;
                p.LastResult = r.Message;
                SaveStore();
            }
        }
        AddHistory(new BackupHistoryEntry
        {
            TimeUtc = startedUtc,
            Kind = "Datei-Backup",
            Name = profile.Name,
            Target = target,
            Success = r.Success,
            Duration = r.Duration,
            Message = r.Message,
        });
    }

    /// <summary>Profile, deren letzte erfolgreiche Sicherung älter als <see cref="BackupSettings.OverdueDays"/> ist (für die Tray-Überwachung).</summary>
    public IReadOnlyList<BackupOverdueInfo> GetOverdueProfiles()
    {
        var now = DateTime.UtcNow;
        lock (_gate)
        {
            var days = _data.Settings.OverdueDays;
            return _data.Profiles.Select(p => BackupPlanner.CheckOverdue(p, days, now)).OfType<BackupOverdueInfo>().ToList();
        }
    }

    /// <summary>Namen der überfälligen Profile (stabil, als Schlüssel geeignet) – passend für <c>TrayMonitor.BackupOverdueProvider</c>.</summary>
    public IReadOnlyList<string> GetOverdueProfileNames() => GetOverdueProfiles().Select(o => o.ProfileName).ToList();

    // ======================================================================================
    // Zeitplan (nur solange ULTIMATE X läuft)
    // ======================================================================================

    /// <summary>Startet die Zeitplan-Überwachung (alle 60 s). Mehrfacher Aufruf ist unschädlich.</summary>
    public void StartScheduler()
    {
        lock (_gate)
        {
            if (_timer is not null) return;
            _timer = new Timer(_ => _ = TickAsync(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
        }
        AppLog.Info("Backup", "Zeitplan-Überwachung gestartet", "Prüfung alle 60 Sekunden, solange ULTIMATE X läuft");
    }

    public void StopScheduler()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
        _scheduledCts?.Cancel();
    }

    /// <summary>Bricht eine laufende geplante Sicherung ab.</summary>
    public void CancelScheduledRun() => _scheduledCts?.Cancel();

    public bool SchedulerRunning { get { lock (_gate) return _timer is not null; } }

    private async Task TickAsync()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            if (IsRunning) return;
            var nowLocal = DateTime.Now;
            var nowUtc = DateTime.UtcNow;
            foreach (var profile in GetProfiles())
            {
                if (!profile.Enabled || profile.Schedule.Kind == BackupScheduleKind.Manual) continue;
                var target = ResolveTarget(profile);
                var key = profile.Id;
                bool due;
                if (profile.Schedule.Kind == BackupScheduleKind.OnTargetConnected)
                {
                    var wasPresent = _presentTargets.Contains(key);
                    if (target.Available) _presentTargets.Add(key); else _presentTargets.Remove(key);
                    // Beim Programmstart gilt eine bereits angeschlossene Platte als „gerade angesteckt“.
                    due = target.Available && !wasPresent && BackupPlanner.ShouldRunOnConnect(profile, nowUtc);
                }
                else
                {
                    due = target.Available && BackupPlanner.IsDue(profile, nowLocal);
                }
                if (!due) continue;

                AppLog.Info("Backup", $"Geplante Sicherung startet: {profile.Name}", profile.Schedule.Kind.ToString());
                using var cts = new CancellationTokenSource();
                _scheduledCts = cts;
                var progress = new SyncProgress<BackupProgress>(p => ScheduledProgress?.Invoke(profile, p));
                var result = await RunProfileAsync(profile.Id, progress, cts.Token).ConfigureAwait(false);
                _scheduledCts = null;
                ScheduledRunCompleted?.Invoke(profile, result);
                Changed?.Invoke();
                break; // höchstens eine Sicherung je Durchlauf
            }
        }
        catch (Exception ex)
        {
            // Der Zeitplan darf die App nie beenden.
            AppLog.Error("Backup", "Zeitplan-Prüfung fehlgeschlagen", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // ======================================================================================
    // Verlauf
    // ======================================================================================

    public IReadOnlyList<BackupHistoryEntry> GetHistory()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(HistoryFile)) return [];
                return JsonSerializer.Deserialize<List<BackupHistoryEntry>>(File.ReadAllText(HistoryFile), Json) ?? [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                AppLog.Warn("Backup", "Backup-Verlauf nicht lesbar", ex.Message);
                return [];
            }
        }
    }

    /// <summary>Fügt einen Verlaufseintrag hinzu (auch für System-Abbild und Treibersicherung). Max. 300 Einträge.</summary>
    public void AddHistory(BackupHistoryEntry entry)
    {
        lock (_gate)
        {
            var list = GetHistory().ToList();
            list.Insert(0, entry);
            if (list.Count > 300) list.RemoveRange(300, list.Count - 300);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);
                File.WriteAllText(HistoryFile, JsonSerializer.Serialize(list, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("Backup", "Backup-Verlauf nicht gespeichert", ex.Message);
            }
        }
        Changed?.Invoke();
    }

    // ======================================================================================
    // Speicher
    // ======================================================================================

    private static BackupStoreData? LoadStore(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            var data = JsonSerializer.Deserialize<BackupStoreData>(File.ReadAllText(file), Json);
            if (data is null) return null;
            data.Settings ??= new BackupSettings();
            data.Profiles ??= [];
            foreach (var p in data.Profiles)
            {
                p.Sources ??= [];
                p.Templates ??= [];
                p.Exclusions ??= [];
                p.Schedule ??= new BackupScheduleSettings();
            }
            foreach (var p in data.Profiles.Where(p => !BackupPaths.IsValidFolderName(p.FolderName)).ToList())
                p.FolderName = BackupPaths.MakeFolderName(p.Name, data.Profiles.Where(x => x != p).Select(x => x.FolderName));
            return data;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            AppLog.Warn("Backup", "Backup-Profile nicht lesbar", ex.Message);
            return null;
        }
    }

    private void SaveStore()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilesFile)!);
            var tmp = ProfilesFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, Json));
            File.Move(tmp, ProfilesFile, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Backup", "Backup-Profile nicht gespeichert", ex.Message);
        }
    }

    public void Dispose()
    {
        StopScheduler();
        _runLock.Dispose();
    }
}
