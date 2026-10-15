using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualBasic.FileIO;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Logging;
using UltimateX.Core.Localization;
using UltimateX.Core.SpaceAnalysis;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Auswählbares Laufwerk.</summary>
public sealed record SpaceDriveOption(string Root, string Label);

/// <summary>Zeile im Ordnerbaum (Kinder des aktuellen Ordners, nach Größe sortiert).</summary>
public sealed record SpaceFolderRow(FolderNode? Node, string Name, string FullPath, string SizeText, double Percent, string FilesText, bool AccessDenied)
{
    public bool CanOpen => Node is { Children.Count: > 0 };
}

/// <summary>Dateityp-Zeile.</summary>
public sealed record SpaceTypeRow(string Extension, string FilesText, string SizeText, double Percent);

/// <summary>Datei (Top-Liste oder Duplikat) mit Auswahl für „In den Papierkorb“.</summary>
public sealed partial class SpaceFileRow(string fullPath, long size, DateTime? modifiedUtc, int group = 0) : ObservableObject
{
    public string FullPath { get; } = fullPath;
    public string Name { get; } = Path.GetFileName(fullPath);
    public string Folder { get; } = Path.GetDirectoryName(fullPath) ?? "";
    public long Size { get; } = size;
    public string SizeText { get; } = Format.Bytes(size);
    public string ModifiedText { get; } = modifiedUtc is { } m ? Format.DateTimeText(m.ToLocalTime()) : "";
    public int Group { get; } = group;

    /// <summary>Löschen gesperrt (geschützter Systemordner) – Grund als Tooltip.</summary>
    public string? Blocked { get; } = SpaceDeletionGuard.IsAllowedPath(fullPath, SpaceDeletionGuard.ExtraProtectedRoots, out var reason) ? null : reason;

    public bool CanSelect => Blocked is null;

    [ObservableProperty] private bool _isSelected;
}

/// <summary>Gruppe inhaltsgleicher Dateien.</summary>
public sealed record SpaceDuplicateRow(string Header, IReadOnlyList<SpaceFileRow> Files);

/// <summary>
/// SPEICHERPLATZ-ANALYSE (v1.3): Ordnerbaum nach Größe mit Drilldown, größte Dateien, doppelte Dateien und Dateitypen.
/// Scan nur lesend im Hintergrund (Fortschritt, Abbrechen); Reparse-Points werden nicht verfolgt, Zugriffsfehler gezählt.
/// Löschen nur ausgewählter Dateien nach Bestätigung in den Papierkorb – nie in Windows, Programme, ProgramData.
/// </summary>
public sealed partial class SpaceAnalysisViewModel : PageViewModel
{
    public override string Title => "Speicherplatz-Analyse";
    public override string Glyph => "\uEDA2";
    public override string Keywords => "speicherplatz analyse ordner größe groß große dateien duplikate doppelt dateitypen festplatte voll platz frei treesize windirstat disk usage";
    public override string? Subtitle => "Was belegt den Platz? Ordner, größte Dateien, Duplikate";

    public ObservableCollection<SpaceDriveOption> DriveOptions { get; } = [];
    public ObservableCollection<SpaceFolderRow> Folders { get; } = [];
    public ObservableCollection<SpaceFileRow> LargestFiles { get; } = [];
    public ObservableCollection<SpaceDuplicateRow> Duplicates { get; } = [];
    public ObservableCollection<SpaceTypeRow> FileTypes { get; } = [];

    [ObservableProperty] private SpaceDriveOption? _selectedDrive;
    [ObservableProperty] private string? _targetPath;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private string? _summaryText;
    [ObservableProperty] private string? _currentPath;
    [ObservableProperty] private string? _duplicateSummary;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _canGoUp;

    private SpaceScanResult? _result;
    private FolderNode? _current;
    private CancellationTokenSource? _cts;

    protected override Task LoadAsync()
    {
        DriveOptions.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                var label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" {d.VolumeLabel}";
                DriveOptions.Add(new SpaceDriveOption(d.RootDirectory.FullName,
                    $"{d.Name.TrimEnd('\\')}{label} – {Format.Bytes(d.AvailableFreeSpace)} frei von {Format.Bytes(d.TotalSize)}"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        SelectedDrive ??= DriveOptions.FirstOrDefault();
        return Task.CompletedTask;
    }

    partial void OnSelectedDriveChanged(SpaceDriveOption? value)
    {
        if (value is not null) TargetPath = value.Root;
    }

    [RelayCommand]
    private void PickFolder()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("Ordner für die Speicherplatz-Analyse auswählen") };
        if (dlg.ShowDialog() == true) TargetPath = dlg.FolderName;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning || string.IsNullOrWhiteSpace(TargetPath)) return;
        var target = TargetPath!;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsScanning = true;
        StatusMessage = null;
        ProgressText = "Scan startet …";
        var progress = new Progress<SpaceScanProgress>(p =>
            ProgressText = $"{p.Files.ToString("N0", Loc.Culture)} Dateien · {p.Folders.ToString("N0", Loc.Culture)} Ordner · {Format.Bytes(p.Bytes)} – {p.CurrentFolder}");
        try
        {
            var result = await Task.Run(() => SpaceScanner.Scan(target, new SpaceScanOptions(), progress, ct), CancellationToken.None);
            ApplyScan(result);
            AppLog.Info("SpaceAnalysis", "Scan abgeschlossen", $"{result.TotalFiles} Dateien, {result.AccessErrors} Zugriffsfehler, abgebrochen={result.Cancelled}");
        }
        catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Scan nicht möglich: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
            ProgressText = null;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelScan() => _cts?.Cancel();

    private void ApplyScan(SpaceScanResult r)
    {
        _result = r;
        HasResult = true;
        var c = Loc.Culture;
        SummaryText = $"{Format.Bytes(r.TotalBytes)} in {r.TotalFiles.ToString("N0", c)} Dateien und {r.TotalFolders.ToString("N0", c)} Ordnern" +
                      $" · {r.AccessErrors.ToString("N0", c)} Zugriffsfehler (übersprungen) · {r.SkippedReparsePoints.ToString("N0", c)} Verknüpfungen nicht verfolgt" +
                      (r.CloudOnlyFiles > 0 ? $" · {r.CloudOnlyFiles.ToString("N0", c)} Cloud-Dateien (nur online) nicht gezählt" : "") +
                      (r.Cancelled ? " · ABGEBROCHEN – Ergebnis unvollständig" : "");
        Navigate(r.Root);

        LargestFiles.Clear();
        foreach (var f in r.LargestFiles) LargestFiles.Add(new SpaceFileRow(f.FullPath, f.Size, f.LastWriteUtc));

        FileTypes.Clear();
        foreach (var t in r.FileTypes.Take(60))
            FileTypes.Add(new SpaceTypeRow(t.Extension, t.Files.ToString("N0", c), Format.Bytes(t.Bytes), SpaceScanner.Percent(t.Bytes, r.TotalBytes)));

        Duplicates.Clear();
        var candidates = r.SizeGroups.Values.Sum(v => v.Count);
        DuplicateSummary = candidates == 0
            ? "Keine Kandidaten (Dateien ab 1 MB mit gleicher Größe)."
            : $"{candidates.ToString("N0", c)} Dateien ab 1 MB haben eine gleich große Partnerdatei. „Duplikate prüfen“ vergleicht den Inhalt (SHA-256).";
    }

    private void Navigate(FolderNode node)
    {
        _current = node;
        CurrentPath = node.FullPath;
        CanGoUp = node.Parent is not null;
        Folders.Clear();
        var c = Loc.Culture;
        foreach (var child in node.Children.Take(500))
            Folders.Add(new SpaceFolderRow(child, child.Name, child.FullPath, Format.Bytes(child.TotalBytes), SpaceScanner.Percent(child.TotalBytes, node.TotalBytes),
                $"{child.TotalFiles.ToString("N0", c)} Dateien", child.AccessDenied));
        if (node.OwnFiles > 0)
            Folders.Add(new SpaceFolderRow(null, "(Dateien direkt in diesem Ordner)", node.FullPath, Format.Bytes(node.OwnBytes),
                SpaceScanner.Percent(node.OwnBytes, node.TotalBytes), $"{node.OwnFiles.ToString("N0", c)} Dateien", false));
    }

    [RelayCommand]
    private void OpenFolder(SpaceFolderRow? row)
    {
        if (row?.Node is { } n && n.Children.Count > 0) Navigate(n);
    }

    [RelayCommand]
    private void Up()
    {
        if (_current?.Parent is { } p) Navigate(p);
    }

    [RelayCommand]
    private async Task FindDuplicatesAsync()
    {
        if (_result is null || IsScanning) return;
        var groups = _result.SizeGroups;
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        IsScanning = true;
        var progress = new Progress<DuplicateProgress>(p => ProgressText = $"Duplikate: {p.Phase} {p.Done.ToString("N0", Loc.Culture)} / {p.Total.ToString("N0", Loc.Culture)}");
        try
        {
            var r = await Task.Run(() => DuplicateFinder.Find(groups, null, progress, ct), CancellationToken.None);
            Duplicates.Clear();
            var g = 0;
            foreach (var d in r.Groups.Take(500))
            {
                g++;
                Duplicates.Add(new SpaceDuplicateRow($"{d.Paths.Count}× {Format.Bytes(d.Size)} – {Format.Bytes(d.WastedBytes)} mehrfach belegt",
                    d.Paths.Select(p => new SpaceFileRow(p, d.Size, null, g)).ToList()));
            }
            DuplicateSummary = $"{r.Groups.Count.ToString("N0", Loc.Culture)} Gruppen inhaltsgleicher Dateien · {Format.Bytes(r.WastedBytes)} mehrfach belegt" +
                               (r.UnreadableFiles > 0 ? $" · {r.UnreadableFiles.ToString("N0", Loc.Culture)} Dateien nicht lesbar" : "") +
                               (r.Cancelled ? " · ABGEBROCHEN" : "");
        }
        finally
        {
            IsScanning = false;
            ProgressText = null;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void ShowInExplorer(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var isFile = File.Exists(path);
        if (!isFile && !Directory.Exists(path)) { StatusMessage = "Pfad existiert nicht mehr."; return; }
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { isFile ? "/select," + path : path },
            UseShellExecute = false,
        });
    }

    /// <summary>Ausgewählte Dateien (Top-Liste und Duplikate) nach Bestätigung in den Papierkorb verschieben.</summary>
    [RelayCommand]
    private void RecycleSelected()
    {
        var selected = LargestFiles.Concat(Duplicates.SelectMany(d => d.Files))
            .Where(f => f.IsSelected)
            .GroupBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
            .ToList();
        if (selected.Count == 0) { StatusMessage = "Keine Dateien ausgewählt."; return; }

        // Von jeder Duplikatgruppe muss mindestens eine Kopie erhalten bleiben.
        foreach (var d in Duplicates)
        {
            if (d.Files.All(f => selected.Any(s => s.FullPath.Equals(f.FullPath, StringComparison.OrdinalIgnoreCase))))
            {
                StatusMessage = "Bei Duplikaten bitte mindestens eine Kopie behalten – es sind alle Dateien einer Gruppe ausgewählt.";
                return;
            }
        }

        var total = selected.Sum(f => f.Size);
        var list = string.Join("\n", selected.Take(10).Select(f => "• " + f.FullPath)) + (selected.Count > 10 ? $"\n… und {selected.Count - 10} weitere" : "");
        if (!Dialogs.Confirm("In den Papierkorb verschieben",
                $"{selected.Count} Datei(en) mit zusammen {Format.Bytes(total)} in den Papierkorb verschieben?\n\n{list}\n\n" +
                "Die Dateien lassen sich aus dem Papierkorb wiederherstellen. Programme, die diese Dateien benötigen, funktionieren danach evtl. nicht mehr.", true))
            return;

        int ok = 0, blocked = 0, failed = 0;
        long freed = 0;
        foreach (var f in selected)
        {
            if (!SpaceDeletionGuard.CanRecycle(f.FullPath, out var reason))
            {
                blocked++;
                AppLog.Warn("SpaceAnalysis", "Löschen abgelehnt", $"{f.FullPath}: {reason}");
                continue;
            }
            try
            {
                FileSystem.DeleteFile(f.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                ok++;
                freed += f.Size;
                AppLog.Info("SpaceAnalysis", "In den Papierkorb verschoben", f.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException or System.Security.SecurityException)
            {
                failed++;
                AppLog.Warn("SpaceAnalysis", "Löschen fehlgeschlagen", $"{f.FullPath}: {ex.Message}");
            }
        }

        foreach (var f in selected.Where(f => !File.Exists(f.FullPath)))
        {
            LargestFiles.Remove(f);
            foreach (var d in Duplicates) if (d.Files is List<SpaceFileRow> l) l.RemoveAll(x => x.FullPath.Equals(f.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        var dupes = Duplicates.Where(d => d.Files.Count > 1).ToList();
        Duplicates.Clear();
        foreach (var d in dupes) Duplicates.Add(d);

        StatusMessage = $"{ok} Datei(en) in den Papierkorb verschoben ({Format.Bytes(freed)})" +
                        (blocked > 0 ? $" · {blocked} gesperrt (geschützter Ordner)" : "") +
                        (failed > 0 ? $" · {failed} fehlgeschlagen" : "") +
                        ". Der Platz wird erst beim Leeren des Papierkorbs frei. Ordnergrößen zeigen den Stand des Scans.";
    }
}
