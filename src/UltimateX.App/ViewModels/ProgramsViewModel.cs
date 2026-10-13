using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Common;
using UltimateX.Services.Software;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed record ProgramRow(InstalledProgram Program, string Name, string Publisher, string Version, string Size, string Date, string Scope);

/// <summary>Programm-Manager (§34). Deinstallation über die Windows-Einstellungen, Updates (winget) in Phase 3.</summary>
public sealed partial class ProgramsViewModel : PageViewModel
{
    public override string Title => "Programme";
    public override string Glyph => "\uE71D";
    public override string Keywords => "programme software apps installiert deinstallieren updates winget anwendungen";

    private List<ProgramRow> _all = [];
    public ObservableCollection<ProgramRow> Rows { get; } = [];

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private ProgramRow? _selected;
    [ObservableProperty] private string? _summary;

    partial void OnFilterChanged(string value) => ApplyFilter();

    protected override async Task LoadAsync()
    {
        var programs = await Task.Run(() => AppServices.Programs.GetPrograms());
        _all = programs.Select(p => new ProgramRow(p, p.Name, p.Publisher ?? "–", p.Version ?? "–",
            p.SizeBytes is null ? "–" : Format.Bytes(p.SizeBytes), p.InstallDate?.ToString("dd.MM.yyyy") ?? "–", p.Scope)).ToList();
        Summary = $"{_all.Count} installierte Programme";
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var q = Filter.Trim();
        foreach (var r in _all)
        {
            if (q.Length > 0 && !r.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) && !r.Publisher.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                continue;
            Rows.Add(r);
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (Selected is null) { StatusMessage = "Bitte zuerst ein Programm auswählen."; return; }
        if (!ProgramService.TryOpenInstallLocation(Selected.Program))
            StatusMessage = "Für dieses Programm ist kein gültiger Installationsordner hinterlegt.";
    }

    /// <summary>
    /// Deinstallation bewusst über "Apps &amp; Features": Windows führt die Deinstallation mit eigener Rückfrage aus.
    /// Die App führt keine fremden UninstallString-Befehlszeilen aus (Command-Injection-Schutz, §67).
    /// </summary>
    [RelayCommand]
    private void Uninstall()
    {
        if (Selected is not null)
            StatusMessage = $"„{Selected.Name}“ in den Windows-Einstellungen auswählen und dort deinstallieren.";
        ShowResult(AppServices.Tools.Launch(SystemTool.AppsAndFeatures));
    }
}
