using System.Collections.ObjectModel;
using System.Windows.Interop;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Cleanup;
using UltimateX.Services.Common;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed partial class CleanupItem(CleanupCandidate candidate) : ObservableObject
{
    public CleanupCandidate Candidate { get; } = candidate;
    public string Title => Candidate.Title;
    public string Description => Candidate.Description;
    public string Size => Format.Bytes(Candidate.SizeBytes);
    public string Files => $"{Candidate.FileCount:N0} Datei(en)";
    public string Paths => string.Join(" · ", Candidate.Roots);
    public string? Note => Candidate.Note;
    [ObservableProperty] private bool _selected = candidate.RecommendedByDefault && candidate.FileCount > 0;
}

/// <summary>Storage Cleaner (§52): erst analysieren und anzeigen, dann nur ausgewählte Kategorien nach Bestätigung bereinigen.</summary>
public sealed partial class CleanupViewModel : PageViewModel
{
    public override string Title => "Speicher bereinigen";
    public override string Glyph => "\uE74D";
    public override string Keywords => "cleanup bereinigen speicher temp temporär cache papierkorb aufräumen platz storage cleaner";

    public ObservableCollection<CleanupItem> Items { get; } = [];

    [ObservableProperty] private string? _summary;
    [ObservableProperty] private bool _isCleaning;

    protected override async Task LoadAsync()
    {
        var candidates = await Task.Run(() => AppServices.Cleaner.Analyze());
        Items.Clear();
        foreach (var c in candidates) Items.Add(new CleanupItem(c));
        foreach (var i in Items) i.PropertyChanged += (_, _) => UpdateSummary();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var sel = Items.Where(i => i.Selected).ToList();
        Summary = $"Ausgewählt: {sel.Sum(i => i.Candidate.FileCount):N0} Dateien · {Format.Bytes(sel.Sum(i => i.Candidate.SizeBytes))}";
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var sel = Items.Where(i => i.Selected).ToList();
        if (sel.Count == 0) { StatusMessage = "Bitte mindestens eine Kategorie auswählen."; return; }

        var details = string.Join("\n", sel.Select(i => $"• {i.Title}: {i.Files}, {i.Size}\n   {i.Paths}"));
        var critical = sel.Any(i => i.Candidate.Category == CleanupCategory.RecycleBin);
        var explanation = "Folgende Dateien werden endgültig gelöscht:\n\n" + details +
                          "\n\nPersönliche Dokumente, Desktop, Downloads und Bilder sind nicht betroffen. Gesperrte Dateien werden übersprungen.";

        Func<Task<bool>> work = async () =>
        {
            IsCleaning = true;
            try
            {
                var handle = new WindowInteropHelper(System.Windows.Application.Current.MainWindow).Handle;
                var cats = sel.Select(i => i.Candidate.Category).ToList();
                var r = await Task.Run(() => AppServices.Cleaner.Clean(cats, handle));
                StatusMessage = $"Bereinigt: {r.DeletedFiles:N0} Dateien, {Format.Bytes(r.FreedBytes)} freigegeben" +
                                (r.SkippedFiles > 0 ? $" · {r.SkippedFiles:N0} gesperrte/geschützte Dateien übersprungen." : ".");
                return true;
            }
            finally { IsCleaning = false; }
        };

        // Papierkorb leeren ist eine kritische Cleanup-Aktion (§57) → Authentifizierung; sonst nur Bestätigung.
        var ok = critical
            ? await AppServices.Guard.RunAsync("Speicher bereinigen", explanation, work)
            : Dialogs.Confirm("Speicher bereinigen", explanation + "\n\nFortfahren?") && await work();
        if (ok) await RefreshAsync();
    }

    [RelayCommand]
    private void OpenStorageSettings() => ShowResult(AppServices.Tools.Launch(SystemTool.StorageSettings));
}
