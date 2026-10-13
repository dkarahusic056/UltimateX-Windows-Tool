using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Common;
using UltimateX.Services.Network;

namespace UltimateX.App.ViewModels;

public sealed record SpeedtestRow(string Date, string Download, string Upload, string Ping, string Jitter);

/// <summary>Speedtest mit Tacho, Historie und 7/30-Tage-Vergleich (§23).</summary>
public sealed partial class SpeedtestViewModel : PageViewModel
{
    public override string Title => "Speedtest";
    public override string Glyph => "\uEC4A";
    public override string Keywords => "speedtest internet geschwindigkeit download upload ping jitter bandbreite";

    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _phaseText = "Bereit";
    [ObservableProperty] private double _gaugeMbps;
    [ObservableProperty] private string _gaugeText = "–";
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private SpeedtestResult? _lastResult;
    [ObservableProperty] private string? _average7;
    [ObservableProperty] private string? _average30;

    public ObservableCollection<SpeedtestRow> History { get; } = [];

    public string? LastDownload => LastResult is null ? null : Format.BitsPerSecond(LastResult.DownloadBps);
    public string? LastUpload => LastResult is null ? null : Format.BitsPerSecond(LastResult.UploadBps);

    partial void OnLastResultChanged(SpeedtestResult? value)
    {
        OnPropertyChanged(nameof(LastDownload));
        OnPropertyChanged(nameof(LastUpload));
    }

    protected override Task LoadAsync()
    {
        LoadHistory();
        return Task.CompletedTask;
    }

    private void LoadHistory()
    {
        var all = AppServices.Speedtest.LoadHistory();
        History.Clear();
        foreach (var r in all.OrderByDescending(r => r.Time).Take(50))
            History.Add(new SpeedtestRow(r.Time.ToString("dd.MM.yyyy HH:mm"), Format.BitsPerSecond(r.DownloadBps), Format.BitsPerSecond(r.UploadBps), $"{r.PingMs:0} ms", $"{r.JitterMs:0.0} ms"));

        string? Avg(int days)
        {
            var list = all.Where(r => r.Time >= DateTime.Now.AddDays(-days)).ToList();
            return list.Count == 0 ? null
                : $"Ø ↓ {Format.BitsPerSecond(list.Average(r => r.DownloadBps))} · ↑ {Format.BitsPerSecond(list.Average(r => r.UploadBps))} · Ping {list.Average(r => r.PingMs):0} ms ({list.Count} Tests)";
        }
        Average7 = Avg(7);
        Average30 = Avg(30);
        LastResult ??= all.LastOrDefault();
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning) return;
        if (!AppServices.Settings.SpeedtestNoticeAccepted)
        {
            if (!Dialogs.Confirm("Hinweis zum Datenverbrauch",
                    "Ein Speedtest überträgt je nach Anschluss etwa 100 MB bis über 1 GB an Daten.\n" +
                    "Bei getakteten oder mobilen Verbindungen kann das Kosten verursachen.\n\n" +
                    "Gemessen wird per HTTPS gegen speed.cloudflare.com. Fortfahren?"))
                return;
            AppServices.Settings.SpeedtestNoticeAccepted = true;
            AppServices.SaveSettings();
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        var progress = new Progress<SpeedtestProgress>(p =>
        {
            PhaseText = p.Phase switch
            {
                SpeedtestPhase.Latency => "Latenz wird gemessen …",
                SpeedtestPhase.Download => "Download wird gemessen …",
                SpeedtestPhase.Upload => "Upload wird gemessen …",
                _ => "Fertig",
            };
            if (p.Phase is SpeedtestPhase.Download or SpeedtestPhase.Upload)
            {
                GaugeMbps = p.CurrentBitsPerSecond / 1e6;
                GaugeText = Format.BitsPerSecond(p.CurrentBitsPerSecond);
            }
            ProgressValue = p.Phase switch
            {
                SpeedtestPhase.Latency => p.Fraction * 0.1,
                SpeedtestPhase.Download => 0.1 + p.Fraction * 0.5,
                SpeedtestPhase.Upload => 0.6 + p.Fraction * 0.4,
                _ => 1,
            };
        });

        try
        {
            LastResult = await AppServices.Speedtest.RunAsync(progress, _cts.Token);
            GaugeMbps = LastResult.DownloadBps / 1e6;
            GaugeText = Format.BitsPerSecond(LastResult.DownloadBps);
            PhaseText = "Fertig";
            LoadHistory();
        }
        catch (OperationCanceledException)
        {
            PhaseText = "Abgebrochen";
        }
        catch (HttpRequestException ex)
        {
            PhaseText = "Fehler";
            StatusMessage = "Speedtest fehlgeschlagen – keine Verbindung zum Messserver. " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
