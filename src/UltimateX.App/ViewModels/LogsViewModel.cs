using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.App.ViewModels;

public sealed record LogRow(string Time, string Level, string Category, string Action, string Message, string Details, DataState State);

/// <summary>Aktions- und Security-Log (§80, §81) mit Export als TXT/JSON.</summary>
public sealed partial class LogsViewModel : PageViewModel
{
    public override string Title => "Logs";
    public override string Glyph => "\uE81C";
    public override string Keywords => "logs protokoll verlauf history security log export";

    public ObservableCollection<LogRow> Rows { get; } = [];

    [ObservableProperty] private bool _securityOnly;

    private IReadOnlyList<LogEntry> _entries = [];

    partial void OnSecurityOnlyChanged(bool value) => _ = RefreshAsync();

    protected override async Task LoadAsync()
    {
        var cat = SecurityOnly ? LogCategory.Security : (LogCategory?)null;
        _entries = await Task.Run(() => AppLog.ReadRecent(30, cat));
        Rows.Clear();
        foreach (var e in _entries.Take(2000))
        {
            Rows.Add(new LogRow(
                e.Timestamp.ToString("dd.MM.yyyy HH:mm:ss"),
                e.Level switch { LogLevel.Error => "Fehler", LogLevel.Warning => "Warnung", _ => "Info" },
                e.Category == LogCategory.Security ? "Security" : "Allgemein",
                e.Action, e.Message, e.Details ?? "",
                e.Level switch { LogLevel.Error => DataState.Problem, LogLevel.Warning => DataState.Warning, _ => DataState.Ok }));
        }
    }

    [RelayCommand]
    private void Export()
    {
        var path = Dialogs.SaveFile($"UltimateX-Log-{DateTime.Now:yyyyMMdd-HHmm}", "Textdatei (*.txt)|*.txt|JSON (*.json)|*.json");
        if (path is null) return;
        try
        {
            var content = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? AppLog.ExportAsJson(_entries) : AppLog.ExportAsText(_entries);
            File.WriteAllText(path, content);
            AppLog.Security("ReportExport", "Log exportiert", details: Path.GetFileName(path));
            StatusMessage = "Log exportiert: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Export fehlgeschlagen: " + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { AppPaths.LogDirectory },
            UseShellExecute = false,
        });
    }
}
