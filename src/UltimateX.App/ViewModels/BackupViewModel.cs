using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Backup;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Services.Backup;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Vorlage zum Anhaken im Profil-Editor.</summary>
public sealed partial class BackupTemplateOption(BackupSourceTemplate template, bool available) : ObservableObject
{
    public string Id { get; } = template.Id;
    public string Label { get; } = template.Label;
    public bool IsAvailable { get; } = available;
    public string Description { get; } = available ? template.Description : template.Description + " – auf diesem PC nicht gefunden";
    [ObservableProperty] private bool _isChecked;
}

public sealed record BackupProfileRow(BackupProfile Profile, string LastText, string ScheduleText, string TargetText, DataState State)
{
    public string Name => Profile.Name;
    public override string ToString() => Profile.Name;
}

public sealed record BackupHistoryRow(string Time, string Kind, string Name, string Target, string Result, string Duration, DataState State);

public sealed record BackupVersionRow(BackupVersionInfo Info)
{
    public string Display => $"{Format.DateTimeText(Info.Created)} · {Info.Files} Dateien · {StatusText}";
    public string StatusText => Info.Status switch
    {
        BackupVersionStatus.Completed => "vollständig",
        BackupVersionStatus.CompletedWithWarnings => "vollständig (mit Hinweisen)",
        BackupVersionStatus.Cancelled => "abgebrochen",
        BackupVersionStatus.Running => "unvollständig",
        BackupVersionStatus.Failed => "fehlgeschlagen",
        _ => "ohne Manifest",
    };
    public override string ToString() => Display;
}

public sealed record RestoreItemRow(string StorePath, string OriginalPath, bool IsFolder, string SizeText, string Modified)
{
    public string Glyph => IsFolder ? "" : "";
}

public sealed record BackupDriveChoice(char Letter, string Display)
{
    public override string ToString() => Display;
}

public sealed record BackupSkippedRow(string Path, string Reason);

public sealed record BackupChoice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed record ImageFileRow(SystemImageFile File)
{
    public string Name => System.IO.Path.GetFileName(File.Path);
    public string Detail => $"{File.BackupSet} · {Format.DateTimeText(File.LastWrite)} · {Format.Bytes(File.Size)}";
}

/// <summary>
/// v1.3 BACKUP-CENTER: System-Abbild (wbadmin), Datei-Backup mit Versionen (eigene Implementierung, ohne Admin),
/// Wiederherstellen, Treiber sichern (pnputil) und Rettungs-USB-Stick (RecoveryDrive). Nichts wird gelöscht außer alten
/// Backup-Versionen gemäß der eingestellten Aufbewahrung.
/// </summary>
public sealed partial class BackupViewModel : PageViewModel
{
    public override string Title => "Backup-Center";
    public override string Glyph => "";
    public override string Keywords => "backup sicherung datensicherung abbild image systemabbild wbadmin wiederherstellen restore versionen treiber pnputil rettungsstick recovery usb acronis";
    public override string? Subtitle => "System-Abbild, Datei-Backup mit Versionen, Wiederherstellung, Treiber, Rettungs-USB-Stick";

    private BackupService Service => AppServices.Backup;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _restoreCts;
    private string? _editingId;
    private bool _reloading;
    private BackupManifest? _manifest;

    public BackupViewModel()
    {
        Service.StartScheduler(); // idempotent – startet die Überwachung spätestens beim Öffnen dieser Seite
        Service.Changed += () => Application.Current?.Dispatcher.InvokeAsync(() => _ = ReloadProfilesAsync());
        Service.ScheduledProgress += (p, prog) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ScheduledText = $"Geplante Sicherung „{p.Name}“: {prog.Phase} · {prog.Percent:0} %";
        });
        Service.ScheduledRunCompleted += (p, r) => Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ScheduledText = $"Geplante Sicherung „{p.Name}“: {r.Message}";
        });
        foreach (var t in BackupTemplates.SourceTemplates)
            Templates.Add(new BackupTemplateOption(t, BackupTemplates.Resolve(t.Id, KnownFolders.Current()).Count > 0));
        SelectedScheduleKind = ScheduleKinds[0];
        SelectedDay = Days[6];
        SelectedConflictMode = ConflictModes[0];
        NewProfile();
    }

    // ======================================================================================
    // Übersicht
    // ======================================================================================

    public ObservableCollection<InfoItem> Overview { get; } = [];
    public ObservableCollection<BackupHistoryRow> History { get; } = [];

    [ObservableProperty] private string? _scheduledText;

    public string SchedulerNote =>
        "Zeitpläne laufen nur, solange ULTIMATE X geöffnet ist – minimiert oder im Infobereich reicht. Ist die App geschlossen oder der PC aus, " +
        "wird eine verpasste tägliche/wöchentliche Sicherung beim nächsten Start nachgeholt (sobald die Zielplatte angeschlossen ist).";

    public string BitLockerNote =>
        "Empfehlung: Verschlüsseln Sie die Backup-Platte mit „BitLocker To Go“ (Explorer → Rechtsklick auf das Laufwerk → BitLocker aktivieren; " +
        "verfügbar in Windows Pro/Enterprise). Eine verlorene Backup-Platte enthält sonst alle Ihre Dateien im Klartext. " +
        "Bewahren Sie den Wiederherstellungsschlüssel getrennt von der Platte auf.";

    protected override async Task LoadAsync()
    {
        await ReloadProfilesAsync();
        await LoadDrivesAsync();
    }

    private async Task ReloadProfilesAsync()
    {
        var (profiles, history, overdue, rows) = await Task.Run(() =>
        {
            var ps = Service.GetProfiles();
            var rs = ps.Select(p => MakeRow(p, Service.ResolveTarget(p))).ToList();
            return (ps, Service.GetHistory(), Service.GetOverdueProfiles(), rs);
        });

        // Auswahl beibehalten, ohne den Editor (ungespeicherte Eingaben) oder die Versionsliste neu zu laden.
        var selectedId = SelectedProfileRow?.Profile.Id ?? _editingId;
        var restoreId = RestoreProfile?.Profile.Id;
        _reloading = true;
        try
        {
            Profiles.Clear();
            foreach (var r in rows) Profiles.Add(r);
            SelectedProfileRow = selectedId is null ? null : Profiles.FirstOrDefault(r => r.Profile.Id == selectedId);
            _restoreProfile = restoreId is null ? null : Profiles.FirstOrDefault(r => r.Profile.Id == restoreId);
            OnPropertyChanged(nameof(RestoreProfile));
        }
        finally
        {
            _reloading = false;
        }

        History.Clear();
        foreach (var h in history.Take(50))
            History.Add(new BackupHistoryRow(Format.DateTimeText(h.TimeUtc.ToLocalTime()), h.Kind, h.Name, h.Target, h.Message,
                Format.Duration(h.Duration), h.Success ? DataState.Ok : DataState.Problem));

        Overview.Clear();
        Overview.Add(new InfoItem("Zeitplan-Überwachung", Service.SchedulerRunning ? "aktiv (solange ULTIMATE X läuft)" : "aus", Service.SchedulerRunning ? DataState.Ok : DataState.Warning));
        if (profiles.Count == 0)
            Overview.Add(new InfoItem("Datei-Backup", "Noch kein Profil angelegt – Bereich „Datei-Backup“ öffnen.", DataState.Warning));
        foreach (var r in rows) Overview.Add(new InfoItem(r.Name, $"{r.LastText} · {r.ScheduleText} · {r.TargetText}", r.State));
        foreach (var o in overdue)
            Overview.Add(new InfoItem("Warnung", o.LastSuccessUtc is null
                ? $"„{o.ProfileName}“ wurde noch nie erfolgreich gesichert."
                : $"Letzte erfolgreiche Sicherung von „{o.ProfileName}“ ist {o.DaysSinceSuccess} Tage alt.", DataState.Problem));
        var lastImage = history.FirstOrDefault(h => h.Kind == "System-Abbild" && h.Success);
        Overview.Add(lastImage is null
            ? new InfoItem("System-Abbild", "Kein erfolgreiches Abbild im Verlauf.", DataState.Warning)
            : new InfoItem("System-Abbild", $"zuletzt {Format.DateTimeText(lastImage.TimeUtc.ToLocalTime())} auf {lastImage.Target}", DataState.Ok));
        var lastDrivers = history.FirstOrDefault(h => h.Kind == "Treiber" && h.Success);
        Overview.Add(lastDrivers is null
            ? new InfoItem("Treiber", "Noch nicht gesichert.", DataState.NotAvailable)
            : new InfoItem("Treiber", $"zuletzt {Format.DateTimeText(lastDrivers.TimeUtc.ToLocalTime())} · {lastDrivers.Target}", DataState.Ok));
        StoreHashes = Service.Settings.StoreHashes;
        OverdueDaysText = Service.Settings.OverdueDays.ToString(CultureInfo.InvariantCulture);
    }

    private static BackupProfileRow MakeRow(BackupProfile p, BackupTargetStatus target)
    {
        var last = p.LastSuccessUtc is { } s ? "letzte Sicherung " + Format.DateTimeText(s.ToLocalTime()) : "noch nie gesichert";
        var state = !p.Enabled ? DataState.NotAvailable
            : p.LastSuccessUtc is null ? DataState.Warning
            : p.LastResult is not null && p.LastAttemptUtc > p.LastSuccessUtc ? DataState.Problem
            : DataState.Ok;
        return new BackupProfileRow(p, last, ScheduleText(p.Schedule), target.Available ? "Ziel bereit" : target.Message, state);
    }

    private static string ScheduleText(BackupScheduleSettings s) => s.Kind switch
    {
        BackupScheduleKind.Daily => $"täglich {s.TimeOfDay:hh\\:mm}",
        BackupScheduleKind.Weekly => $"wöchentlich {DayName(s.DayOfWeek)} {s.TimeOfDay:hh\\:mm}",
        BackupScheduleKind.OnTargetConnected => "beim Anstecken der Zielplatte",
        _ => "manuell",
    };

    private static string DayName(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        _ => "Sonntag",
    };

    // ======================================================================================
    // Datei-Backup: Profile bearbeiten
    // ======================================================================================

    public ObservableCollection<BackupProfileRow> Profiles { get; } = [];
    public ObservableCollection<BackupTemplateOption> Templates { get; } = [];
    public ObservableCollection<BackupSource> ExtraSources { get; } = [];
    public ObservableCollection<BackupSkippedRow> SkippedRows { get; } = [];

    public IReadOnlyList<BackupChoice<BackupScheduleKind>> ScheduleKinds { get; } =
    [
        new(BackupScheduleKind.Manual, "Manuell"),
        new(BackupScheduleKind.Daily, "Täglich"),
        new(BackupScheduleKind.Weekly, "Wöchentlich"),
        new(BackupScheduleKind.OnTargetConnected, "Beim Anstecken der Zielplatte"),
    ];

    public IReadOnlyList<BackupChoice<DayOfWeek>> Days { get; } =
    [
        new(DayOfWeek.Monday, "Montag"), new(DayOfWeek.Tuesday, "Dienstag"), new(DayOfWeek.Wednesday, "Mittwoch"),
        new(DayOfWeek.Thursday, "Donnerstag"), new(DayOfWeek.Friday, "Freitag"), new(DayOfWeek.Saturday, "Samstag"), new(DayOfWeek.Sunday, "Sonntag"),
    ];

    [ObservableProperty] private BackupProfileRow? _selectedProfileRow;
    [ObservableProperty] private BackupSource? _selectedExtraSource;
    [ObservableProperty] private string _editName = "Meine Dateien";
    [ObservableProperty] private string _editTarget = string.Empty;
    [ObservableProperty] private string _exclusionsText = string.Join(Environment.NewLine, BackupTemplates.DefaultExclusions);
    [ObservableProperty] private BackupChoice<BackupScheduleKind>? _selectedScheduleKind;
    [ObservableProperty] private string _scheduleTime = "19:00";
    [ObservableProperty] private BackupChoice<DayOfWeek>? _selectedDay;
    [ObservableProperty] private string _keepVersionsText = "10";
    [ObservableProperty] private bool _useHashCompare;
    [ObservableProperty] private bool _editEnabled = true;
    [ObservableProperty] private string? _targetHint;
    [ObservableProperty] private bool _storeHashes = true;
    [ObservableProperty] private string _overdueDaysText = "7";

    // Laufender Datei-Backup
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private double _runPercent;
    [ObservableProperty] private string? _runPhase;
    [ObservableProperty] private string? _runDetail;
    [ObservableProperty] private string? _runSummary;
    [ObservableProperty] private DataState _runState = DataState.NotAvailable;

    public bool ShowWeekday => SelectedScheduleKind?.Value == BackupScheduleKind.Weekly;
    public bool ShowTime => SelectedScheduleKind?.Value is BackupScheduleKind.Daily or BackupScheduleKind.Weekly;
    public bool IsEditingExisting => _editingId is not null;

    partial void OnSelectedScheduleKindChanged(BackupChoice<BackupScheduleKind>? value)
    {
        OnPropertyChanged(nameof(ShowWeekday));
        OnPropertyChanged(nameof(ShowTime));
    }

    partial void OnSelectedProfileRowChanged(BackupProfileRow? value)
    {
        if (value is null || _reloading) return;
        var p = Service.GetProfile(value.Profile.Id);
        if (p is null) return;
        _editingId = p.Id;
        EditName = p.Name;
        EditTarget = p.TargetPath;
        foreach (var t in Templates) t.IsChecked = p.Templates.Contains(t.Id);
        ExtraSources.Clear();
        foreach (var s in p.Sources) ExtraSources.Add(s);
        ExclusionsText = string.Join(Environment.NewLine, p.Exclusions);
        SelectedScheduleKind = ScheduleKinds.FirstOrDefault(k => k.Value == p.Schedule.Kind) ?? ScheduleKinds[0];
        ScheduleTime = p.Schedule.TimeOfDay.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        SelectedDay = Days.FirstOrDefault(d => d.Value == p.Schedule.DayOfWeek) ?? Days[6];
        KeepVersionsText = p.KeepVersions.ToString(CultureInfo.InvariantCulture);
        UseHashCompare = p.CompareMode == BackupCompareMode.SizeTimeAndHash;
        EditEnabled = p.Enabled;
        OnPropertyChanged(nameof(IsEditingExisting));
        UpdateTargetHint();
    }

    partial void OnEditTargetChanged(string value) => UpdateTargetHint();

    private void UpdateTargetHint()
    {
        if (string.IsNullOrWhiteSpace(EditTarget)) { TargetHint = "Ziel wählen – am besten eine externe Festplatte (NTFS, damit unveränderte Dateien per Hardlink kaum Platz brauchen)."; return; }
        try
        {
            var root = System.IO.Path.GetPathRoot(EditTarget);
            var sameDrive = ExtraSources.Any(s => string.Equals(System.IO.Path.GetPathRoot(s.Path), root, StringComparison.OrdinalIgnoreCase))
                            || (Templates.Any(t => t.IsChecked) && string.Equals(root, System.IO.Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), StringComparison.OrdinalIgnoreCase));
            var d = root is null ? null : new System.IO.DriveInfo(root);
            var fs = d is { IsReady: true } ? d.DriveFormat : "N/A";
            var sb = new StringBuilder();
            sb.Append(Loc.F("Dateisystem: {0}.", fs));
            if (!string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase)) sb.Append(' ').Append(Loc.T("Kein NTFS: Jede Version wird vollständig kopiert (keine Hardlinks) und braucht entsprechend Platz."));
            if (sameDrive) sb.Append(' ').Append(Loc.T("Achtung: Quelle und Ziel liegen auf demselben Laufwerk – das schützt nicht vor einem Plattendefekt."));
            TargetHint = sb.ToString();
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException or UnauthorizedAccessException)
        {
            TargetHint = null;
        }
    }

    [RelayCommand]
    private void NewProfile()
    {
        _editingId = null;
        SelectedProfileRow = null;
        EditName = "Meine Dateien";
        EditTarget = string.Empty;
        foreach (var t in Templates) t.IsChecked = t.IsAvailable && t.Id is BackupTemplates.Desktop or BackupTemplates.Documents or BackupTemplates.Pictures;
        ExtraSources.Clear();
        ExclusionsText = string.Join(Environment.NewLine, BackupTemplates.DefaultExclusions);
        SelectedScheduleKind = ScheduleKinds[0];
        ScheduleTime = "19:00";
        KeepVersionsText = "10";
        UseHashCompare = false;
        EditEnabled = true;
        OnPropertyChanged(nameof(IsEditingExisting));
        UpdateTargetHint();
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Ordner für das Backup auswählen"), Multiselect = true };
        if (dlg.ShowDialog() != true) return;
        AddPaths(dlg.FolderNames);
    }

    [RelayCommand]
    private void AddDropped(string[]? paths)
    {
        if (paths is null) return;
        AddPaths(paths);
    }

    private void AddPaths(IEnumerable<string> paths)
    {
        var rejected = 0;
        foreach (var raw in paths)
        {
            string full;
            try { full = System.IO.Path.GetFullPath(raw); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.IO.PathTooLongException) { rejected++; continue; }
            var isDir = System.IO.Directory.Exists(full);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || (!isDir && !System.IO.File.Exists(full))) { rejected++; continue; }
            if (ExtraSources.Any(s => s.Path.Equals(full, StringComparison.OrdinalIgnoreCase))) continue;
            ExtraSources.Add(new BackupSource { Path = full, IsFile = !isDir });
        }
        StatusMessage = rejected > 0 ? Loc.F("{0} Eintrag/Einträge nicht übernommen (Netzwerkpfade oder nicht vorhanden).", rejected) : null;
        UpdateTargetHint();
    }

    [RelayCommand]
    private void RemoveSource()
    {
        if (SelectedExtraSource is { } s) ExtraSources.Remove(s);
    }

    [RelayCommand]
    private void ChooseTarget()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Backup-Ziel wählen (z. B. externe Festplatte)") };
        if (dlg.ShowDialog() == true) EditTarget = dlg.FolderName;
    }

    [RelayCommand]
    private void AddProgrammingExclusions()
    {
        var current = ExclusionsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        foreach (var e in BackupTemplates.ProgrammingExclusions)
            if (!current.Contains(e, StringComparer.OrdinalIgnoreCase)) current.Add(e);
        ExclusionsText = string.Join(Environment.NewLine, current);
    }

    private BackupProfile? BuildProfileFromEditor(out string? error)
    {
        error = null;
        if (!TimeSpan.TryParseExact(ScheduleTime.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var tod) || tod >= TimeSpan.FromDays(1))
        {
            error = "Uhrzeit bitte als HH:MM eingeben (z. B. 19:00).";
            return null;
        }
        if (!int.TryParse(KeepVersionsText, NumberStyles.None, CultureInfo.InvariantCulture, out var keep) || keep is < BackupProfile.MinKeep or > BackupProfile.MaxKeep)
        {
            error = "Aufbewahrung: Zahl zwischen 1 und 365.";
            return null;
        }
        var existing = _editingId is null ? null : Service.GetProfile(_editingId);
        var p = existing ?? new BackupProfile();
        p.Name = EditName.Trim();
        p.TargetPath = EditTarget.Trim();
        p.Templates = Templates.Where(t => t.IsChecked).Select(t => t.Id).ToList();
        p.Sources = ExtraSources.Select(s => new BackupSource { Path = s.Path, IsFile = s.IsFile }).ToList();
        p.Exclusions = ExclusionsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        p.Schedule = new BackupScheduleSettings
        {
            Kind = SelectedScheduleKind?.Value ?? BackupScheduleKind.Manual,
            TimeOfDay = tod,
            DayOfWeek = SelectedDay?.Value ?? DayOfWeek.Sunday,
        };
        p.KeepVersions = keep;
        p.CompareMode = UseHashCompare ? BackupCompareMode.SizeTimeAndHash : BackupCompareMode.SizeAndTime;
        p.Enabled = EditEnabled;
        return p;
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        var p = BuildProfileFromEditor(out var error);
        if (p is null) { StatusMessage = error; return; }
        var r = await Task.Run(() => Service.SaveProfile(p));
        ShowResult(r);
        if (!r.Success) return;
        _editingId = p.Id;
        OnPropertyChanged(nameof(IsEditingExisting));
        await ReloadProfilesAsync();
        SelectedProfileRow = Profiles.FirstOrDefault(x => x.Profile.Id == p.Id);
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (_editingId is null) return;
        if (!Dialogs.Confirm("Profil entfernen", "Das Profil wird aus ULTIMATE X entfernt. Bereits erstellte Sicherungen auf der Zielplatte bleiben vollständig erhalten.\n\nProfil entfernen?"))
            return;
        var id = _editingId;
        await Task.Run(() => Service.DeleteProfile(id));
        NewProfile();
        await ReloadProfilesAsync();
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!int.TryParse(OverdueDaysText, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days is < 1 or > 365)
        {
            StatusMessage = "Warnschwelle: 1 bis 365 Tage.";
            return;
        }
        var hashes = StoreHashes;
        await Task.Run(() => Service.UpdateSettings(s => { s.StoreHashes = hashes; s.OverdueDays = days; }));
        StatusMessage = "Einstellungen gespeichert.";
    }

    [RelayCommand]
    private async Task RunNowAsync()
    {
        if (IsRunning) return;
        if (_editingId is null)
        {
            StatusMessage = "Bitte das Profil zuerst speichern.";
            return;
        }
        var profile = Service.GetProfile(_editingId);
        if (profile is null) return;
        IsRunning = true;
        RunSummary = null;
        RunState = DataState.NotAvailable;
        RunPercent = 0;
        RunPhase = "Wird vorbereitet";
        SkippedRows.Clear();
        _runCts = new CancellationTokenSource();
        var progress = new Progress<BackupProgress>(p =>
        {
            RunPercent = p.Percent;
            RunPhase = p.Phase;
            RunDetail = p.FilesTotal > 0
                ? Loc.F("{0} von {1} Dateien · {2} von {3}", p.FilesDone, p.FilesTotal, Format.Bytes(p.BytesDone), Format.Bytes(p.BytesTotal))
                : p.CurrentPath;
        });
        try
        {
            var r = await Service.RunProfileAsync(profile.Id, progress, _runCts.Token);
            RunSummary = r.Message + (r.VerifyResult is null ? "" : Environment.NewLine + r.VerifyResult)
                         + (r.DeletedVersions.Count > 0 ? Environment.NewLine + Loc.F("Aufbewahrung: {0} alte Version(en) entfernt.", r.DeletedVersions.Count) : "")
                         + (r.Success ? Environment.NewLine + Loc.F("Dauer {0} · {1} kopiert · {2} unverändert (Hardlink) · {3} Kopie statt Hardlink",
                             Format.Duration(r.Duration), Format.Bytes(r.BytesCopied), r.Linked, r.LinkFallbacks) : "");
            RunState = r.Success ? (r.Skipped.Count > 0 ? DataState.Warning : DataState.Ok) : r.Status == BackupVersionStatus.Cancelled ? DataState.Warning : DataState.Problem;
            foreach (var s in r.Skipped.Take(1000)) SkippedRows.Add(new BackupSkippedRow(s.Path, s.Reason));
            RunPercent = r.Success ? 100 : RunPercent;
            RunPhase = r.Success ? "Fertig" : "Beendet";
        }
        finally
        {
            IsRunning = false;
            _runCts.Dispose();
            _runCts = null;
        }
        await ReloadProfilesAsync();
    }

    [RelayCommand]
    private void CancelRun()
    {
        _runCts?.Cancel();
        _restoreCts?.Cancel();
        Service.CancelScheduledRun();
    }

    [RelayCommand]
    private void ExportProfiles()
    {
        var path = Dialogs.SaveFile("ultimatex-backup-profile.json", "JSON-Datei (*.json)|*.json");
        if (path is not null) ShowResult(Service.Export(path));
    }

    [RelayCommand]
    private async Task ImportProfilesAsync()
    {
        var path = Dialogs.OpenFile("JSON-Datei (*.json)|*.json");
        if (path is null) return;
        ShowResult(await Task.Run(() => Service.Import(path)));
        await ReloadProfilesAsync();
    }

    [RelayCommand]
    private async Task OpenTargetFolderAsync()
    {
        if (_editingId is null) return;
        var p = Service.GetProfile(_editingId);
        if (p is null) return;
        var t = await Task.Run(() => Service.ResolveTarget(p));
        if (!t.Available || t.CurrentPath is null) { StatusMessage = t.Message; return; }
        var folder = System.IO.Path.Combine(BackupPaths.BackupRoot(t.CurrentPath), p.FolderName);
        ShowResult(SystemImageService.OpenFolder(System.IO.Directory.Exists(folder) ? folder : t.CurrentPath));
    }

    // ======================================================================================
    // Wiederherstellen
    // ======================================================================================

    public ObservableCollection<BackupVersionRow> Versions { get; } = [];
    public ObservableCollection<RestoreItemRow> RestoreItems { get; } = [];

    public IReadOnlyList<BackupChoice<RestoreConflictMode>> ConflictModes { get; } =
    [
        new(RestoreConflictMode.KeepExisting, "Vorhandene Dateien behalten (überspringen)"),
        new(RestoreConflictMode.KeepBoth, "Beide behalten (Sicherung mit neuem Namen)"),
        new(RestoreConflictMode.Replace, "Ersetzen (vorhandene Datei in den Papierkorb)"),
    ];

    private BackupProfileRow? _restoreProfile;

    public BackupProfileRow? RestoreProfile
    {
        get => _restoreProfile;
        set
        {
            if (SetProperty(ref _restoreProfile, value) && !_reloading) _ = LoadVersionsAsync();
        }
    }

    [ObservableProperty] private BackupVersionRow? _selectedVersion;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private RestoreItemRow? _selectedRestoreItem;
    [ObservableProperty] private bool _restoreToOriginal = true;
    [ObservableProperty] private string _restoreFolder = string.Empty;
    [ObservableProperty] private BackupChoice<RestoreConflictMode>? _selectedConflictMode;
    [ObservableProperty] private string? _restoreSummary;
    [ObservableProperty] private string? _versionsNote;
    [ObservableProperty] private bool _isRestoring;

    partial void OnSelectedVersionChanged(BackupVersionRow? value) => _ = LoadManifestAsync();

    private async Task LoadVersionsAsync()
    {
        Versions.Clear();
        RestoreItems.Clear();
        _manifest = null;
        VersionsNote = null;
        var row = RestoreProfile;
        if (row is null) return;
        var p = Service.GetProfile(row.Profile.Id);
        if (p is null) return;
        var (target, versions) = await Task.Run(() =>
        {
            var t = Service.ResolveTarget(p);
            IReadOnlyList<BackupVersionInfo> v = t.Available && t.CurrentPath is not null && BackupPaths.IsValidFolderName(p.FolderName)
                ? FileBackupEngine.ListVersions(BackupPaths.ProfileRoot(t.CurrentPath, p.FolderName))
                : [];
            return (t, v);
        });
        if (!target.Available) { VersionsNote = target.Message; return; }
        foreach (var v in versions) Versions.Add(new BackupVersionRow(v));
        VersionsNote = versions.Count == 0 ? "Auf dem Ziel wurden noch keine Versionen gefunden." : null;
        SelectedVersion = Versions.FirstOrDefault(v => v.Info.IsComplete) ?? Versions.FirstOrDefault();
    }

    private async Task LoadManifestAsync()
    {
        RestoreItems.Clear();
        _manifest = null;
        var v = SelectedVersion;
        if (v is null) return;
        _manifest = await Task.Run(() => FileBackupEngine.LoadManifest(v.Info.Path));
        if (_manifest is null) { VersionsNote = "Diese Version hat kein lesbares Manifest – bitte im Explorer öffnen."; return; }
        if (!v.Info.IsComplete) VersionsNote = "Hinweis: Diese Version ist unvollständig (abgebrochen/fehlgeschlagen).";
        Search();
    }

    [RelayCommand]
    private void Search()
    {
        RestoreItems.Clear();
        var m = _manifest;
        if (m is null) return;
        var term = SearchText.Trim();
        if (term.Length == 0)
        {
            // Ohne Suche: die gesicherten Quellen als Einstiegspunkte
            foreach (var src in m.Sources)
            {
                string store;
                try { store = BackupPaths.ToStoreRelative(src); }
                catch (ArgumentException) { continue; }
                var entries = FileBackupEngine.SelectEntries(m, store);
                if (entries.Count == 0) continue;
                var isFolder = !(entries.Count == 1 && entries[0].Path.Equals(store, StringComparison.OrdinalIgnoreCase));
                RestoreItems.Add(new RestoreItemRow(store, BackupPaths.ToOriginalPath(store), isFolder,
                    Loc.F("{0} Dateien · {1}", entries.Count, Format.Bytes(entries.Sum(e => e.Size))), isFolder ? "" : Format.DateTimeText(entries[0].LastWriteUtc.ToLocalTime())));
            }
            return;
        }

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in m.Files)
        {
            var idx = f.Path.LastIndexOf('\\');
            while (idx > 1 && folders.Count < 100)
            {
                var dir = f.Path[..idx];
                var name = dir[(dir.LastIndexOf('\\') + 1)..];
                if (name.Contains(term, StringComparison.OrdinalIgnoreCase)) folders.Add(dir);
                idx = dir.LastIndexOf('\\');
            }
            if (folders.Count >= 100) break;
        }
        foreach (var d in folders.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var entries = FileBackupEngine.SelectEntries(m, d);
            RestoreItems.Add(new RestoreItemRow(d, BackupPaths.ToOriginalPath(d), true, Loc.F("{0} Dateien · {1}", entries.Count, Format.Bytes(entries.Sum(e => e.Size))), ""));
        }
        foreach (var f in m.Files.Where(f => f.Path[(f.Path.LastIndexOf('\\') + 1)..].Contains(term, StringComparison.OrdinalIgnoreCase)).Take(400))
            RestoreItems.Add(new RestoreItemRow(f.Path, BackupPaths.ToOriginalPath(f.Path), false, Format.Bytes(f.Size), Format.DateTimeText(f.LastWriteUtc.ToLocalTime())));
        if (RestoreItems.Count == 0) VersionsNote = "Keine Treffer in dieser Version.";
    }

    [RelayCommand]
    private void ChooseRestoreFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Zielordner für die Wiederherstellung wählen") };
        if (dlg.ShowDialog() != true) return;
        RestoreFolder = dlg.FolderName;
        RestoreToOriginal = false;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (IsRestoring) return;
        var m = _manifest;
        var v = SelectedVersion;
        var item = SelectedRestoreItem;
        if (m is null || v is null || item is null) { StatusMessage = "Bitte Version und eine Datei bzw. einen Ordner auswählen."; return; }
        string? folder = null;
        if (!RestoreToOriginal)
        {
            folder = RestoreFolder.Trim();
            if (folder.Length == 0 || !System.IO.Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\", StringComparison.Ordinal) || !System.IO.Directory.Exists(folder))
            {
                StatusMessage = "Bitte einen vorhandenen lokalen Zielordner wählen.";
                return;
            }
        }
        var mode = SelectedConflictMode?.Value ?? RestoreConflictMode.KeepExisting;
        var entries = FileBackupEngine.SelectEntries(m, item.StorePath);
        var request = new RestoreRequest(v.Info.Path, entries, item.StorePath, folder, mode);
        var preview = await Task.Run(() => FileBackupEngine.Preview(request));
        if (preview.Files == 0)
        {
            StatusMessage = preview.BlockedReasons.Count > 0 ? preview.BlockedReasons[0] : "Nichts wiederherzustellen.";
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(Loc.F("{0} Datei(en) ({1}) aus der Sicherung vom {2} werden wiederhergestellt.", preview.Files, Format.Bytes(preview.Bytes), Format.DateTimeText(v.Info.Created)));
        text.AppendLine(Loc.F("Ziel: {0}", folder ?? Loc.T("Originalort") + " (" + item.OriginalPath + ")"));
        if (preview.Conflicts > 0)
        {
            text.AppendLine(Loc.F("{0} Datei(en) existieren dort bereits → {1}.", preview.Conflicts, Loc.T(SelectedConflictMode?.Label ?? "")));
            if (mode == RestoreConflictMode.Replace) text.AppendLine(Loc.T("Die vorhandenen Dateien werden in den Papierkorb verschoben."));
        }
        if (preview.Blocked > 0) text.AppendLine(Loc.F("{0} Datei(en) werden nicht wiederhergestellt (geschützter Ort oder Laufwerk fehlt).", preview.Blocked));
        if (!Dialogs.Confirm("Wiederherstellen", text.ToString() + Environment.NewLine + Loc.T("Fortfahren?"), warning: mode == RestoreConflictMode.Replace && preview.Conflicts > 0))
            return;

        IsRestoring = true;
        RestoreSummary = "Wiederherstellung läuft …";
        _restoreCts = new CancellationTokenSource();
        var progress = new Progress<BackupProgress>(p => RestoreSummary = Loc.F("Wiederherstellung: {0} von {1} Dateien", p.FilesDone, p.FilesTotal));
        try
        {
            var r = await Service.Engine.RestoreAsync(request, progress, _restoreCts.Token);
            RestoreSummary = r.Summary + (r.Problems.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, r.Problems.Take(20)) : "");
        }
        finally
        {
            IsRestoring = false;
            _restoreCts.Dispose();
            _restoreCts = null;
        }
    }

    [RelayCommand]
    private void OpenVersionFolder()
    {
        if (SelectedVersion is { } v) ShowResult(SystemImageService.OpenFolder(v.Info.Path));
    }

    // ======================================================================================
    // System-Abbild
    // ======================================================================================

    public ObservableCollection<BackupDriveChoice> ImageDrives { get; } = [];
    public ObservableCollection<BackupDriveChoice> LocalDrives { get; } = [];
    public ObservableCollection<ImageFileRow> ImageFiles { get; } = [];

    [ObservableProperty] private BackupDriveChoice? _selectedImageDrive;
    [ObservableProperty] private string? _imageVerdict;
    [ObservableProperty] private DataState _imageVerdictState = DataState.NotAvailable;
    [ObservableProperty] private bool _imageRunning;
    [ObservableProperty] private double _imagePercent;
    [ObservableProperty] private string? _imagePhase;
    [ObservableProperty] private string _imageOutput = string.Empty;
    [ObservableProperty] private string? _imageFilesNote;

    public IReadOnlyList<string> RestoreGuide { get; } =
    [
        "1. Externe Festplatte mit dem System-Abbild anschließen. Startet Windows nicht mehr: vom Rettungs-USB-Stick oder Windows-Installationsmedium starten.",
        "2. In Windows: Knopf „Erweiterter Start“ unten (Einstellungen → System → Wiederherstellung → Jetzt neu starten).",
        "3. Im blauen Menü: Problembehandlung → Erweiterte Optionen → Systemimage-Wiederherstellung (ggf. „Weitere Wiederherstellungsoptionen anzeigen“).",
        "4. Konto wählen und Kennwort eingeben. Windows schlägt das neueste Abbild vor – oder „Systemabbild auswählen“ für ein älteres.",
        "5. Achtung: Das Systemlaufwerk wird vollständig mit dem Stand des Abbilds überschrieben. Neuere Dateien vorher mit dem Datei-Backup sichern.",
        "6. Fertigstellen und warten (je nach Größe 20 Minuten bis mehrere Stunden). Danach startet Windows im gesicherten Zustand.",
    ];

    private async Task LoadDrivesAsync()
    {
        var (drives, local) = await Task.Run(() =>
        {
            var targets = AppServices.SystemImage.GetTargetDrives();
            var all = new List<BackupDriveChoice>();
            try
            {
                foreach (var di in System.IO.DriveInfo.GetDrives())
                {
                    try
                    {
                        if (di.IsReady && di.DriveType is System.IO.DriveType.Fixed or System.IO.DriveType.Removable)
                            all.Add(new BackupDriveChoice(char.ToUpperInvariant(di.Name[0]), $"{di.Name.TrimEnd('\\')} {di.VolumeLabel} ({di.DriveFormat}, {Format.Bytes(di.AvailableFreeSpace)} frei)"));
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
            return (targets, all);
        });
        var selImage = SelectedImageDrive?.Letter;
        var selDriver = SelectedDriverDrive?.Letter;
        ImageDrives.Clear();
        foreach (var drive in drives.Where(x => x.IsReady)) ImageDrives.Add(new BackupDriveChoice(drive.Letter, drive.Display));
        LocalDrives.Clear();
        foreach (var drive in local) LocalDrives.Add(drive);
        SelectedImageDrive = ImageDrives.FirstOrDefault(x => x.Letter == selImage) ?? ImageDrives.FirstOrDefault();
        SelectedDriverDrive = LocalDrives.FirstOrDefault(x => x.Letter == selDriver) ?? LocalDrives.FirstOrDefault(x => x.Letter != Core.Security.CommandCatalog.SystemDriveLetter) ?? LocalDrives.FirstOrDefault();
        if (ImageDrives.Count == 0) { ImageVerdict = "Kein weiteres Laufwerk gefunden. Bitte eine externe Festplatte anschließen und „Aktualisieren“ wählen."; ImageVerdictState = DataState.Warning; }
    }

    partial void OnSelectedImageDriveChanged(BackupDriveChoice? value) => _ = CheckImageTargetAsync();

    private async Task CheckImageTargetAsync()
    {
        var d = SelectedImageDrive;
        if (d is null) return;
        var verdict = await Task.Run(() => AppServices.SystemImage.Check(d.Letter));
        ImageVerdict = verdict.Message;
        ImageVerdictState = verdict.Ok ? DataState.Ok : DataState.Problem;
    }

    [RelayCommand]
    private async Task RefreshDrivesAsync() => await LoadDrivesAsync();

    [RelayCommand]
    private async Task CreateImageAsync()
    {
        if (ImageRunning || SelectedImageDrive is null) return;
        var letter = SelectedImageDrive.Letter;
        var verdict = await Task.Run(() => AppServices.SystemImage.Check(letter));
        ImageVerdict = verdict.Message;
        ImageVerdictState = verdict.Ok ? DataState.Ok : DataState.Problem;
        if (!verdict.Ok) return;

        await AppServices.Guard.RunAsync("System-Abbild erstellen",
            Loc.F("Windows (wbadmin) sichert alle für den Start nötigen Laufwerke (System, EFI, Wiederherstellung) als Abbild auf {0}:.", letter) + "\n" +
            Loc.T("Dauer: je nach Datenmenge 20 Minuten bis mehrere Stunden. Der PC kann währenddessen benutzt werden.") + "\n" +
            Loc.T("Hinweis: Auf dem Ziel wird pro PC nur das neueste Abbild sicher behalten – ältere können von Windows überschrieben werden.") + "\n" +
            Loc.T("Erfordert Administratorrechte (UAC)."),
            async () =>
            {
                ImageRunning = true;
                ImagePercent = 0;
                ImagePhase = "Wird gestartet – bitte die Windows-Abfrage (UAC) bestätigen";
                ImageOutput = string.Empty;
                _imageCts = new CancellationTokenSource();
                var sb = new StringBuilder();
                var progress = new Progress<WbadminProgress>(p =>
                {
                    if (p.Percent is { } pc) ImagePercent = pc;
                    ImagePhase = p.PhaseText + (p.Percent is { } x ? $" · {x:0} %" : "");
                });
                var raw = new Progress<string>(chunk =>
                {
                    sb.Append(chunk);
                    if (sb.Length > 100_000) sb.Remove(0, sb.Length - 100_000);
                    ImageOutput = sb.ToString();
                });
                try
                {
                    var r = await AppServices.SystemImage.CreateImageAsync(letter, progress, raw, _imageCts.Token);
                    ImagePhase = r.Message;
                    if (r.Success) ImagePercent = 100;
                    if (r.Output.Length > 0 && ImageOutput.Length == 0) ImageOutput = r.Output;
                    StatusMessage = r.Message;
                    return r.Success;
                }
                finally
                {
                    ImageRunning = false;
                    _imageCts.Dispose();
                    _imageCts = null;
                }
            }, warning: false);
        await ReloadProfilesAsync();
    }

    [RelayCommand]
    private void CancelImage() => _imageCts?.Cancel();

    [RelayCommand]
    private async Task ShowImageVersionsAsync()
    {
        var letter = SelectedImageDrive?.Letter;
        ImageOutput = Loc.T("wbadmin get versions wird ausgeführt (UAC) …");
        var r = await SystemImageService.GetVersionsAsync(letter, CancellationToken.None);
        var times = WbadminProgressParser.ParseVersionTimes(r.Output);
        ImageOutput = (times.Count > 0 ? Loc.F("{0} Abbild(er) gefunden: {1}", times.Count, string.Join(" · ", times)) + Environment.NewLine + Environment.NewLine : "") + r.Output;
    }

    [RelayCommand]
    private async Task FindImagesAsync()
    {
        var d = SelectedImageDrive;
        if (d is null) return;
        var (files, problem) = await Task.Run(() => (SystemImageService.FindImageFiles(d.Letter, out var p), p));
        ImageFiles.Clear();
        foreach (var f in files) ImageFiles.Add(new ImageFileRow(f));
        ImageFilesNote = problem ?? (files.Count == 0 ? "Keine VHDX-Dateien gefunden." : null);
    }

    [RelayCommand]
    private void OpenImageFile(ImageFileRow? row)
    {
        if (row is null) return;
        if (!Dialogs.Confirm("Abbild durchsuchen",
                "Windows bindet die VHDX-Datei als Laufwerk ein – und zwar BESCHREIBBAR.\n\n" +
                "• Nur Dateien herauskopieren, nichts im eingebundenen Laufwerk ändern oder löschen – sonst kann das Abbild für die Wiederherstellung unbrauchbar werden.\n" +
                "• Danach im Explorer: Rechtsklick auf das Laufwerk → „Auswerfen“.\n\nJetzt öffnen?", warning: true))
            return;
        ShowResult(SystemImageService.OpenImageFile(row.File.Path));
    }

    [RelayCommand]
    private void OpenImageFolder()
    {
        if (SelectedImageDrive is { } d) ShowResult(SystemImageService.OpenFolder(SystemImageService.ImageRoot(d.Letter)));
    }

    [RelayCommand]
    private void AdvancedStartup()
    {
        if (!Dialogs.Confirm("Erweiterter Start", "Es öffnen sich die Windows-Einstellungen „Wiederherstellung“. Dort bei „Erweiterter Start“ auf „Jetzt neu starten“ klicken – der PC startet dann neu in die Windows-Wiederherstellung.\n\nBitte vorher alle Arbeiten speichern."))
            return;
        ShowResult(UriLauncher.OpenSettings("recovery"));
    }

    [RelayCommand]
    private async Task RecoveryDriveAsync()
    {
        if (!Dialogs.Confirm("Rettungs-USB-Stick erstellen",
                "Der Windows-Assistent „Wiederherstellungslaufwerk“ wird gestartet (UAC).\n\n" +
                "• USB-Stick mit mindestens 16 GB (mit Systemdateien: 32 GB) bereithalten.\n" +
                "• ALLE Daten auf dem Stick werden gelöscht.\n" +
                "• Mit dem Stick startet ein PC, der nicht mehr hochfährt, in die Windows-Wiederherstellung – dort kann das System-Abbild zurückgespielt werden.\n\nAssistent starten?"))
            return;
        var r = await SystemImageService.StartRecoveryDriveAsync();
        StatusMessage = r.ExitCode == 0 ? Loc.T("Der Windows-Assistent wurde gestartet.") : r.Output;
    }

    // ======================================================================================
    // Treiber sichern
    // ======================================================================================

    public ObservableCollection<ExportedDriver> ExportedDrivers { get; } = [];
    public ObservableCollection<string> DriverBackups { get; } = [];

    [ObservableProperty] private BackupDriveChoice? _selectedDriverDrive;
    [ObservableProperty] private bool _driverRunning;
    [ObservableProperty] private string? _driverSummary;
    [ObservableProperty] private string? _driverInstructions;
    [ObservableProperty] private string? _lastDriverFolder;

    partial void OnSelectedDriverDriveChanged(BackupDriveChoice? value)
    {
        DriverBackups.Clear();
        if (value is null) return;
        foreach (var d in DriverBackupService.ListDriverBackups(value.Letter)) DriverBackups.Add(d);
    }

    [RelayCommand]
    private async Task ExportDriversAsync()
    {
        if (DriverRunning || SelectedDriverDrive is null) return;
        var letter = SelectedDriverDrive.Letter;
        await AppServices.Guard.RunAsync("Treiber sichern",
            Loc.F("Alle installierten Treiberpakete (Driver Store) werden mit pnputil nach {0}:\\UltimateX-Backup\\Treiber\\<Datum> kopiert.", letter) + "\n" +
            Loc.T("Es wird nichts installiert oder verändert. Erfordert Administratorrechte (UAC)."),
            async () =>
            {
                DriverRunning = true;
                DriverSummary = "Läuft … bitte die Windows-Abfrage (UAC) bestätigen.";
                ExportedDrivers.Clear();
                try
                {
                    var r = await AppServices.DriverBackup.ExportAsync(letter, null, CancellationToken.None);
                    DriverSummary = r.Message;
                    foreach (var d in r.Drivers) ExportedDrivers.Add(d);
                    LastDriverFolder = r.Folder;
                    DriverInstructions = r.Folder is null ? null : Loc.T(DriverBackupService.RestoreInstructions(r.Folder)); // TextBox wird nicht automatisch übersetzt
                    OnSelectedDriverDriveChanged(SelectedDriverDrive);
                    return r.Success;
                }
                finally
                {
                    DriverRunning = false;
                }
            }, warning: false);
        await ReloadProfilesAsync();
    }

    [RelayCommand]
    private void OpenDriverFolder(string? folder)
    {
        var f = folder ?? LastDriverFolder;
        if (f is null) return;
        ShowResult(SystemImageService.OpenFolder(f));
        DriverInstructions = Loc.T(DriverBackupService.RestoreInstructions(f));
        ExportedDrivers.Clear();
        foreach (var d in DriverBackupService.ListExported(f)) ExportedDrivers.Add(d);
    }
}
