using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed record ProcessRow(int Pid, string Name, string Path, string Cpu, string Ram, string Publisher, string Signature, DataState State, double CpuValue);

/// <summary>
/// Prozesse mit Pfad, Herausgeber und Signatur (§77). Nur Anzeige – unbekannte/unsignierte Prozesse werden
/// bewusst nicht als Malware bezeichnet, sondern neutral als "nicht signiert" angezeigt.
/// </summary>
public sealed partial class ProcessesViewModel : PageViewModel
{
    public override string Title => "Prozesse";
    public override string Glyph => "\uE9F5";
    public override string Keywords => "prozesse process task laufend pid signatur publisher cpu ram arbeitsspeicher";

    public ObservableCollection<ProcessRow> Rows { get; } = [];

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _verifySignatures = true;
    [ObservableProperty] private string? _summary;

    private IReadOnlyList<ProcessInfo> _last = [];
    private int _tick;

    partial void OnFilterChanged(string value) => Apply();

    protected override async Task LoadAsync()
    {
        var verify = VerifySignatures;
        _last = await Task.Run(() => AppServices.Processes.Snapshot(verify));
        Apply();
    }

    private void Apply()
    {
        var q = Filter.Trim();
        Rows.Clear();
        foreach (var p in _last)
        {
            if (q.Length > 0 && !p.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                && !(p.Path?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
                && !(p.Signature?.Publisher?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)) continue;
            var state = p.Signature?.Status switch
            {
                SignatureStatus.Valid => DataState.Ok,
                SignatureStatus.Tampered => DataState.Problem,
                SignatureStatus.Untrusted or SignatureStatus.NotSigned => DataState.Warning,
                _ => DataState.NotAvailable,
            };
            Rows.Add(new ProcessRow(p.Pid, p.Name, p.Path ?? "(kein Zugriff – geschützter Prozess)",
                p.CpuPercent.ToString("0.0") + " %", Format.Bytes(p.WorkingSetBytes),
                p.Signature?.Publisher ?? "–", p.Signature?.StatusText ?? "nicht geprüft", state, p.CpuPercent));
        }
        var unsigned = _last.Count(p => p.Signature?.Status is SignatureStatus.NotSigned or SignatureStatus.Untrusted);
        Summary = $"{_last.Count} Prozesse · {unsigned} ohne gültige eingebettete Signatur (viele Windows-Prozesse sind katalogsigniert)";
    }

    // Live-Aktualisierung alle 3 Sekunden, nur solange die Seite sichtbar ist (§89).
    public override void OnNavigatedTo()
    {
        MonitorHub.SampleArrived -= OnTick;
        MonitorHub.SampleArrived += OnTick;
    }

    public override void OnNavigatedFrom() => MonitorHub.SampleArrived -= OnTick;

    private async void OnTick(Services.Monitoring.MonitorSample _)
    {
        if (++_tick % 3 != 0 || IsBusy) return;
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenLocation(ProcessRow? row)
    {
        if (row is null || !File.Exists(row.Path)) return;
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { "/select," + row.Path },
            UseShellExecute = false,
        });
    }
}
