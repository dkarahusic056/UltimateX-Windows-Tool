using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.Monitoring;
using UltimateX.Services.Sensors;

namespace UltimateX.App.ViewModels;

/// <summary>Anzeigezeile einer Sensorquelle.</summary>
public sealed record SensorSourceRow(SensorSourceStatus Status, int Priority)
{
    public string Name => Status.Name;
    public string Headline => Status.Headline;
    public string StateText => Status.StateText;
    public string Detail => Status.Detail;
    public string Counts => $"{Status.SensorCount} Sensoren · {Status.UsedSensors} verwendet · {Status.HardwareCount} Hardware";
    public string LastUpdateText => Status.LastUpdate is { } t ? t.ToString("HH:mm:ss") : "–";
    public string PriorityText => $"Priorität {Priority}";
    public DataState State => Status.State switch
    {
        SensorSourceState.Connected => DataState.Ok,
        SensorSourceState.Available => DataState.Ok,
        SensorSourceState.Inactive => DataState.Warning,
        _ => DataState.NotAvailable,
    };
}

/// <summary>Anzeigezeile eines vereinheitlichten Sensors (Current/Min/Average/Max/Source/Last Update).</summary>
public sealed record UnifiedSensorRow(UnifiedSensor Sensor)
{
    public string Key => $"{Sensor.Code}|{Sensor.Hardware}|{Sensor.Label}|{Sensor.Source}";
    public string Code => Sensor.Code;
    public string Label => Sensor.Label;
    public string Hardware => Sensor.Hardware;
    public string Current => Fmt(Sensor.Current);
    public string Min => Fmt(Sensor.Min);
    public string Average => Fmt(Sensor.Average);
    public string Max => Fmt(Sensor.Max);
    public string Source => Sensor.SourceName;
    public string LastUpdate => Sensor.LastUpdate.ToString("HH:mm:ss");
    public DataState State => Sensor.Unit == "°C"
        ? Sensor.Current >= 90 ? DataState.Problem : Sensor.Current >= 80 ? DataState.Warning : DataState.Ok
        : DataState.Ok;

    private string Fmt(double v) => $"{v.ToString(Sensor.Unit is "MHz" or "RPM" ? "0" : "0.#", Core.Localization.Loc.Culture)} {Sensor.Unit}".Trim();
}

/// <summary>
/// SENSOR SOURCES (v1.1 Phase 2): Status aller Quellen (HWiNFO, LibreHardwareMonitor, Windows nativ, ACPI),
/// automatisches Failover nach Priorität und das vereinheitlichte Sensormodell mit Min/Ø/Max.
/// </summary>
public sealed partial class SensorSourcesViewModel : PageViewModel
{
    private const int HistoryLength = 300;
    private readonly Dictionary<string, Queue<double>> _history = new(StringComparer.Ordinal);

    public override string Title => "Sensorquellen";
    public override string Glyph => "";
    public override string Keywords => "sensor sensoren quellen hwinfo librehardwaremonitor lhm acpi temperatur lüfter fan takt leistung failover sources";
    public override string? Subtitle => "HWiNFO · LibreHardwareMonitor · Windows · ACPI – automatisches Failover";

    public ObservableCollection<SensorSourceRow> Sources { get; } = [];
    public ObservableCollection<UnifiedSensorRow> Sensors { get; } = [];

    public IReadOnlyList<string> Filters { get; } = ["Alle", "Temperaturen", "Lüfter", "Takt & Leistung", "Auslastung"];
    [ObservableProperty] private string _filter = "Alle";
    [ObservableProperty] private UnifiedSensorRow? _selectedSensor;
    [ObservableProperty] private IReadOnlyList<double>? _selectedHistory;
    [ObservableProperty] private string _activeSourceText = "–";
    [ObservableProperty] private string _summary = "Sensoren werden gelesen …";
    private int _tick;

    partial void OnFilterChanged(string value) => Refresh();

    partial void OnSelectedSensorChanged(UnifiedSensorRow? value) => UpdateSelectedHistory();

    protected override Task LoadAsync()
    {
        Refresh();
        return Task.CompletedTask;
    }

    public override void OnNavigatedTo()
    {
        MonitorHub.SampleArrived -= OnSample;
        MonitorHub.SampleArrived += OnSample;
        Refresh();
    }

    public override void OnNavigatedFrom() => MonitorHub.SampleArrived -= OnSample;

    private void OnSample(MonitorSample s)
    {
        if (_tick++ % 2 == 0) Refresh();
    }

    [RelayCommand]
    private void ResetStatistics()
    {
        AppServices.Monitor.SensorManager.ResetStatistics();
        _history.Clear();
        StatusMessage = "Min/Ø/Max wurden zurückgesetzt.";
        Refresh();
    }

    [RelayCommand]
    private void ShowSetupHelp() => Dialogs.Info("Sensorquellen einrichten",
        "ULTIMATE X installiert keinen eigenen Kernel-Treiber. Echte CPU-Kerntemperaturen, Spannungen und Mainboard-Lüfter " +
        "werden von diesen optionalen Programmen übernommen, sobald sie laufen:\n\n" +
        "1. HWiNFO (höchste Priorität): Einstellungen → „Shared Memory Support“ aktivieren, Sensorfenster geöffnet lassen.\n" +
        "2. LibreHardwareMonitor: als Administrator starten – die Werte kommen über WMI.\n" +
        "3. Windows nativ: CPU-Last und -Takt, GPU-Last, GPU-Temperatur (Grafiktreiber, WDDM 2.4+), NVIDIA über NVML.\n" +
        "4. ACPI-Thermalzonen: Firmware-Sensoren des Mainboards (keine CPU-Kerntemperatur).\n\n" +
        "Fällt eine Quelle aus, wechselt ULTIMATE X automatisch zur nächsten. Fehlt ein Sensor in allen Quellen, wird er nicht angezeigt – es werden keine Werte geschätzt.");

    private void Refresh()
    {
        var mgr = AppServices.Monitor.SensorManager;
        var sources = mgr.Sources;
        Sources.Clear();
        foreach (var s in sources) Sources.Add(new SensorSourceRow(s, (int)s.Kind + 1));

        var all = mgr.Sensors;
        foreach (var s in all)
        {
            var key = new UnifiedSensorRow(s).Key;
            if (!_history.TryGetValue(key, out var q)) _history[key] = q = new Queue<double>(HistoryLength);
            if (q.Count == HistoryLength) q.Dequeue();
            q.Enqueue(s.Current);
        }

        var filtered = all.Where(s => Filter switch
        {
            "Temperaturen" => s.Unit == "°C",
            "Lüfter" => s.Id is UnifiedSensorId.CpuFan or UnifiedSensorId.GpuFan or UnifiedSensorId.SystemFan,
            "Takt & Leistung" => s.Id is UnifiedSensorId.CpuClock or UnifiedSensorId.GpuClock or UnifiedSensorId.CpuPower or UnifiedSensorId.GpuPower,
            "Auslastung" => s.Id is UnifiedSensorId.CpuLoad or UnifiedSensorId.GpuLoad,
            _ => true,
        }).Select(s => new UnifiedSensorRow(s)).ToList();

        var selectedKey = SelectedSensor?.Key;
        Sensors.Clear();
        foreach (var r in filtered) Sensors.Add(r);
        if (selectedKey is not null) SelectedSensor = Sensors.FirstOrDefault(r => r.Key == selectedKey);
        UpdateSelectedHistory();

        var connected = sources.Where(s => s.State == SensorSourceState.Connected).Select(s => s.Name).ToList();
        ActiveSourceText = connected.Count == 0 ? "Keine Sensorquelle verbunden" : string.Join(" · ", connected);
        Summary = all.Count == 0
            ? "Noch keine Sensorwerte – der erste Durchlauf dauert bis zu zwei Sekunden."
            : $"{all.Count} Sensoren im einheitlichen Modell · Stand {mgr.LastUpdate:HH:mm:ss}";
    }

    private void UpdateSelectedHistory()
    {
        SelectedHistory = SelectedSensor is { } s && _history.TryGetValue(s.Key, out var q) && q.Count > 1 ? q.ToArray() : null;
    }
}
