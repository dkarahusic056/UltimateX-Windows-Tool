using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.Services.Sensors;
using UltimateX.App.Infrastructure;
using UltimateX.Services.Common;
using UltimateX.Services.Monitoring;

namespace UltimateX.App.ViewModels;

public sealed partial class LiveTile(string title, string glyph, double maximum) : ObservableObject
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    /// <summary>0 = automatische Skalierung.</summary>
    public double Maximum { get; } = maximum;
    [ObservableProperty] private string _value = "–";
    [ObservableProperty] private IReadOnlyList<double>? _history;
}

public sealed record SensorLine(string Name, string Value, string Source);
public sealed record SensorGroup(string Hardware, IReadOnlyList<SensorLine> Lines);

/// <summary>Live Hardware Monitor (§19, §20) mit Zeitbereichen 60 s / 5 min / 15 min.</summary>
public sealed partial class MonitorViewModel : PageViewModel
{
    public override string Title => "Live Monitor";
    public override string Glyph => "\uE9D2";
    public override string Keywords => "live monitor auslastung last performance verlauf temperatur temperaturen sensoren sensor lüfter fan hwinfo download upload disk";

    public LiveTile Cpu { get; } = new("CPU", "\uE950", 100);
    public LiveTile Ram { get; } = new("RAM", "\uE964", 100);
    public LiveTile Disk { get; } = new("Datenträgeraktivität", "\uEDA2", 100);
    public LiveTile Download { get; } = new("Download", "\uE896", 0);
    public LiveTile Upload { get; } = new("Upload", "\uE898", 0);
    public LiveTile Gpu { get; } = new("GPU", "\uE7F4", 100);
    public LiveTile CpuTemp { get; } = new("CPU-Temperatur", "\uE9CA", 110);
    public LiveTile GpuTemp { get; } = new("GPU-Temperatur", "\uE9CA", 110);
    public IReadOnlyList<LiveTile> Tiles => [Cpu, Gpu, Ram, Disk, CpuTemp, GpuTemp, Download, Upload];

    public ObservableCollection<SensorGroup> SensorGroups { get; } = [];
    public ObservableCollection<SensorProviderStatus> Providers { get; } = [];
    [ObservableProperty] private string _sensorFilter = "Temperaturen";
    public IReadOnlyList<string> SensorFilters { get; } = ["Temperaturen", "Lüfter", "Leistung", "Alle"];
    private int _sensorTick;

    partial void OnSensorFilterChanged(string value) => RefreshSensors();

    [RelayCommand]
    private void OpenHwInfoInfo() => Infrastructure.Dialogs.Info("Echte CPU-Temperaturen",
        "Windows selbst stellt CPU-Kerntemperaturen nur über einen Kernel-Treiber bereit. ULTIMATE X installiert bewusst keinen eigenen Treiber (Sicherheit).\n\n" +
        "Wenn eines dieser Programme läuft, übernimmt ULTIMATE X dessen Werte automatisch:\n\n" +
        "• HWiNFO (kostenlos): Einstellungen → „Shared Memory Support“ aktivieren, Sensorfenster geöffnet lassen.\n" +
        "• LibreHardwareMonitor (Open Source): einfach starten.\n\n" +
        "GPU-Temperaturen kommen direkt vom Grafiktreiber (AMD, Intel, NVIDIA), SSD-Temperaturen über SMART (Seite Hardware).");

    /// <summary>Zeitbereich in Sekunden: 60, 300 oder 900.</summary>
    [ObservableProperty] private int _rangeSeconds = 60;

    public bool Range60 { get => RangeSeconds == 60; set { if (value) RangeSeconds = 60; } }
    public bool Range300 { get => RangeSeconds == 300; set { if (value) RangeSeconds = 300; } }
    public bool Range900 { get => RangeSeconds == 900; set { if (value) RangeSeconds = 900; } }

    partial void OnRangeSecondsChanged(int value)
    {
        OnPropertyChanged(nameof(Range60));
        OnPropertyChanged(nameof(Range300));
        OnPropertyChanged(nameof(Range900));
        Redraw();
    }

    protected override Task LoadAsync()
    {
        Redraw();
        return Task.CompletedTask;
    }

    public override void OnNavigatedTo()
    {
        MonitorHub.SampleArrived -= OnSample;
        MonitorHub.SampleArrived += OnSample;
        Redraw();
    }

    public override void OnNavigatedFrom() => MonitorHub.SampleArrived -= OnSample;

    private void OnSample(MonitorSample s)
    {
        Redraw();
        if (_sensorTick++ % 2 == 0) RefreshSensors();
    }

    private void RefreshSensors()
    {
        var kinds = SensorFilter switch
        {
            "Lüfter" => new[] { SensorKind.Fan },
            "Leistung" => new[] { SensorKind.Power, SensorKind.Load, SensorKind.Clock },
            "Alle" => Enum.GetValues<SensorKind>(),
            _ => new[] { SensorKind.Temperature },
        };
        var readings = AppServices.Monitor.Sensors.Where(r => kinds.Contains(r.Kind)).ToList();
        var groups = readings.GroupBy(r => r.Hardware)
            .Select(g => new SensorGroup(g.Key, g.Take(60).Select(r => new SensorLine(r.Name, $"{r.Value:0.#} {r.Unit}".Trim(), r.Source)).ToList()))
            .ToList();
        SensorGroups.Clear();
        foreach (var g in groups) SensorGroups.Add(g);
        Providers.Clear();
        foreach (var p in AppServices.Monitor.SensorProviders) Providers.Add(p);
    }

    private void Redraw()
    {
        var samples = MonitorHub.Recent(RangeSeconds);
        if (samples.Count == 0) return;
        var last = samples[^1];

        Cpu.Value = last.CpuPercent is { } c ? Format.Percent(c) : "nicht verfügbar";
        Cpu.History = samples.Select(x => x.CpuPercent ?? 0).ToArray();

        Ram.Value = last.RamPercent is { } r ? $"{Format.Percent(r)} · {Format.Bytes(last.RamUsedBytes)}" : "nicht verfügbar";
        Ram.History = samples.Select(x => x.RamPercent ?? 0).ToArray();

        Disk.Value = last.DiskActivePercent is { } d ? Format.Percent(d) : "nicht verfügbar";
        Disk.History = samples.Select(x => x.DiskActivePercent ?? 0).ToArray();

        Gpu.Value = last.GpuPercent is { } g ? Format.Percent(g) : "nicht verfügbar";
        Gpu.History = samples.Select(x => x.GpuPercent ?? 0).ToArray();

        CpuTemp.Value = last.CpuTempC is { } ct ? $"{ct} °C" : last.AcpiTempC is { } at ? $"ACPI {at} °C" : "nicht verfügbar";
        CpuTemp.History = samples.Select(x => (double)(x.CpuTempC ?? x.AcpiTempC ?? 0)).ToArray();

        GpuTemp.Value = last.GpuTempC is { } gt ? $"{gt} °C" : "nicht verfügbar";
        GpuTemp.History = samples.Select(x => (double)(x.GpuTempC ?? 0)).ToArray();

        Download.Value = Format.BitsPerSecond(last.DownloadBitsPerSecond);
        Download.History = samples.Select(x => x.DownloadBitsPerSecond).ToArray();

        Upload.Value = Format.BitsPerSecond(last.UploadBitsPerSecond);
        Upload.History = samples.Select(x => x.UploadBitsPerSecond).ToArray();
    }
}
