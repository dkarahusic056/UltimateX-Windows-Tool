using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Services.Common;
using UltimateX.Services.Data;
using UltimateX.Services.Network;

namespace UltimateX.App.ViewModels;

public sealed record SpeedtestRow(string Date, string Download, string Upload, string Ping, string Jitter, string Source);

/// <summary>
/// Speedtest mit Tacho, Historie und 7/30-Tage-Vergleich (§23).
/// v1.3: zusätzlich speedtest.ch eingebettet (WebView2, Controls/SpeedtestChBrowser) mit manueller Ergebnisübernahme
/// in denselben Verlauf (Quelle „speedtest.ch (manuell)“). Die Webseite wird nicht ausgelesen.
/// </summary>
public sealed partial class SpeedtestViewModel : PageViewModel
{
    public override string Title => "Speedtest";
    public override string Glyph => "\uEC4A";
    public override string Keywords => "speedtest internet geschwindigkeit download upload ping jitter bandbreite speedtest.ch cloudflare";

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

    // ---- v1.3: speedtest.ch (eingebettet) ----
    /// <summary>true = Bereich „speedtest.ch“ statt eigenem Test anzeigen (lädt die Webseite erst dann).</summary>
    [ObservableProperty] private bool _showSpeedtestCh;
    [ObservableProperty] private string _manualDownload = "";
    [ObservableProperty] private string _manualUpload = "";
    [ObservableProperty] private string _manualPing = "";
    [ObservableProperty] private string _manualJitter = "";
    [ObservableProperty] private string? _manualError;

    public bool ShowOwnTest
    {
        get => !ShowSpeedtestCh;
        set => ShowSpeedtestCh = !value;
    }

    partial void OnShowSpeedtestChChanged(bool value) => OnPropertyChanged(nameof(ShowOwnTest));

    public string? LastDownload => LastResult is null ? null : Format.BitsPerSecond(LastResult.DownloadBps);
    public string? LastUpload => LastResult is null ? null : Format.BitsPerSecond(LastResult.UploadBps);

    /// <summary>Jitter; bei manuellen Einträgen ohne Jitter-Angabe „N/A“ (gespeichert als 0, siehe <see cref="ApplyManual"/>).</summary>
    public string? LastJitter => LastResult is null ? null : JitterText(LastResult.JitterMs, LastResult.Server);

    partial void OnLastResultChanged(SpeedtestResult? value)
    {
        OnPropertyChanged(nameof(LastDownload));
        OnPropertyChanged(nameof(LastUpload));
        OnPropertyChanged(nameof(LastJitter));
    }

    private static string JitterText(double jitterMs, string server)
        => ManualSpeedtestEntry.IsManualSource(server) && jitterMs <= 0 ? "N/A" : $"{jitterMs:0.0} ms";

    private static string SourceText(string server)
        => ManualSpeedtestEntry.IsManualSource(server) ? server : "Eigener Test (" + server + ")";

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
            History.Add(new SpeedtestRow(r.Time.ToString("dd.MM.yyyy HH:mm"), Format.BitsPerSecond(r.DownloadBps), Format.BitsPerSecond(r.UploadBps), $"{r.PingMs:0} ms",
                JitterText(r.JitterMs, r.Server), SourceText(r.Server)));

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

    /// <summary>
    /// Übernimmt die vom Benutzer abgetippten speedtest.ch-Werte in den Speedtest-Verlauf (Quelle „speedtest.ch (manuell)“).
    /// Datenmodell: Jitter ist in der Datenbank Pflichtfeld – ohne Angabe wird 0 gespeichert und als „N/A“ angezeigt;
    /// Paketverlust bleibt leer (null).
    /// </summary>
    [RelayCommand]
    private void ApplyManual()
    {
        if (!ManualSpeedtestEntry.TryParse(ManualDownload, ManualUpload, ManualPing, ManualJitter, out var m, out var error) || m is null)
        {
            ManualError = error;
            return;
        }
        ManualError = null;
        var record = new SpeedtestRecord(DateTime.Now, m.DownloadMbps * 1e6, m.UploadMbps * 1e6, m.PingMs, m.JitterMs ?? 0, null, ManualSpeedtestEntry.SourceName);
        AppServices.History.AddSpeedtest(record);
        LastResult = new SpeedtestResult(record.Time, record.DownloadBps, record.UploadBps, record.PingMs, record.JitterMs, null, record.Server);
        LoadHistory();
        ManualDownload = ManualUpload = ManualPing = ManualJitter = "";
        StatusMessage = "speedtest.ch-Ergebnis in den Verlauf übernommen.";
    }
}
