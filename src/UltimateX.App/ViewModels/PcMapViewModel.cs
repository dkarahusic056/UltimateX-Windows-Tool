using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.Common;
using UltimateX.Services.Monitoring;

namespace UltimateX.App.ViewModels;

/// <summary>Ein Knoten der Visual PC Map. Position X/Y in einem 1000×640-Koordinatensystem (skaliert per Viewbox).</summary>
public sealed partial class MapNode(string key, string title, string glyph, double x, double y) : ObservableObject
{
    public string Key { get; } = key;
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    public double X { get; } = x;
    public double Y { get; } = y;
    /// <summary>Mittelpunkt für die Verbindungslinie zur Mitte.</summary>
    public double CenterX => X + 80;
    public double CenterY => Y + 40;

    [ObservableProperty] private string _device = "–";
    [ObservableProperty] private string _load = "";
    [ObservableProperty] private string _temperature = "Temperatur: nicht verfügbar";
    [ObservableProperty] private DataState _state = DataState.NotAvailable;

    public string ToolTipText => $"{Device}\n{Load}\n{Temperature}\nStatus: {State switch { DataState.Ok => "OK", DataState.Warning => "Hinweis", DataState.Problem => "Problem", _ => "nicht verfügbar" }}";

    partial void OnDeviceChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnLoadChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnTemperatureChanged(string value) => OnPropertyChanged(nameof(ToolTipText));
    partial void OnStateChanged(DataState value) => OnPropertyChanged(nameof(ToolTipText));
}

/// <summary>Visual PC Map (§8, §101): Mainboard in der Mitte, Komponenten ringsum, Hover = Details, Klick = Detailseite.</summary>
public sealed partial class PcMapViewModel : PageViewModel
{
    private readonly INavigator _nav;

    public PcMapViewModel(INavigator nav)
    {
        _nav = nav;
        Nodes =
        [
            Cpu, Ram, Gpu, Storage, Network, Displays, Usb, Internet,
        ];
    }

    public override string Title => "Visual PC Map";
    public override string Glyph => "\uE80A";
    public override string Keywords => "pc map karte visuell grafisch übersicht komponenten";
    public override string? Subtitle => "Komponente anklicken für Details";

    public MapNode Cpu { get; } = new("cpu", "CPU", "\uE950", 140, 60);
    public MapNode Ram { get; } = new("ram", "RAM", "\uE964", 420, 20);
    public MapNode Gpu { get; } = new("gpu", "GPU", "\uE7F4", 700, 60);
    public MapNode Storage { get; } = new("storage", "SSD / HDD", "\uEDA2", 780, 280);
    public MapNode Network { get; } = new("net", "Netzwerk", "\uE968", 700, 500);
    public MapNode Internet { get; } = new("inet", "Internet", "\uE774", 420, 540);
    public MapNode Usb { get; } = new("usb", "USB", "\uE88E", 140, 500);
    public MapNode Displays { get; } = new("display", "Monitore", "\uE7F8", 60, 280);

    public IReadOnlyList<MapNode> Nodes { get; }

    [ObservableProperty] private string _boardTitle = "MAINBOARD";
    [ObservableProperty] private string _boardDetail = "";

    protected override async Task LoadAsync()
    {
        var hw = AppServices.LastHardware ?? await Task.Run(() => AppServices.Hardware.Collect());
        AppServices.LastHardware = hw;
        var monitors = await Task.Run(() => AppServices.Displays.GetMonitors());
        var adapters = await Task.Run(() => AppServices.Network.GetAdapters());
        var (devices, _) = await Task.Run(() => AppServices.Inventory.GetDevices());

        BoardDetail = $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim();
        Cpu.Device = hw.Cpu?.Name ?? "nicht verfügbar";
        Ram.Device = $"{Format.Bytes(hw.Memory.TotalBytes)} · {hw.Memory.Modules.Count} Modul(e)";
        var gpu = hw.Gpus.FirstOrDefault(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) ?? hw.Gpus.FirstOrDefault();
        Gpu.Device = gpu?.Name ?? "nicht verfügbar";
        Storage.Device = string.Join(", ", hw.Disks.Select(d => $"{d.Name} ({d.MediaType})"));
        Storage.State = hw.Disks.Any(d => d.HealthState == DataState.Problem) ? DataState.Problem : hw.Disks.Count > 0 ? DataState.Ok : DataState.NotAvailable;
        var sys = hw.Volumes.FirstOrDefault();
        Storage.Load = sys is null ? "" : $"{sys.Letter} {sys.UsedPercent:0} % belegt";
        var smart = Services.Hardware.SmartService.LoadCached();
        var temp = smart?.Disks.Select(d => d.TemperatureC).FirstOrDefault(t => t is not null);
        Storage.Temperature = temp is { } st ? $"Temperatur: {st} °C (SMART)" : "Temperatur: „SMART lesen“ auf der Hardware-Seite";

        Displays.Device = monitors.Count == 0 ? "nicht verfügbar" : string.Join(", ", monitors.Select(m => m.FriendlyName));
        Displays.Load = string.Join(" · ", monitors.Select(m => $"{m.Width}×{m.Height} @ {m.RefreshHz} Hz"));
        Displays.State = monitors.Count > 0 ? DataState.Ok : DataState.NotAvailable;

        var active = adapters.Where(a => a.IsUp).ToList();
        Network.Device = active.Count == 0 ? "Kein aktiver Adapter" : string.Join(", ", active.Select(a => $"{a.Name} ({a.Kind})"));
        Network.State = active.Count > 0 ? DataState.Ok : DataState.Problem;
        Internet.Device = active.Any(a => a.Gateways.Count > 0) ? "Verbunden (Gateway vorhanden)" : "Keine Verbindung";
        Internet.State = active.Any(a => a.Gateways.Count > 0) ? DataState.Ok : DataState.Problem;
        var last = AppServices.Speedtest.LoadHistory().LastOrDefault();
        Internet.Load = last is null ? "Noch kein Speedtest" : $"↓ {Format.BitsPerSecond(last.DownloadBps)} · ↑ {Format.BitsPerSecond(last.UploadBps)} · {last.PingMs:0} ms";

        var usb = devices.Where(d => d.Class.Equals("USB", StringComparison.OrdinalIgnoreCase)).ToList();
        Usb.Device = $"{usb.Count} USB-Geräte/-Controller";
        Usb.State = usb.Any(d => d.State != DataState.Ok) ? DataState.Warning : usb.Count > 0 ? DataState.Ok : DataState.NotAvailable;
        Usb.Load = usb.Any(d => d.State != DataState.Ok) ? "Mindestens ein Gerät meldet einen Fehler" : "Alle Geräte funktionieren";

        OnNavigatedTo();
    }

    public override void OnNavigatedTo()
    {
        MonitorHub.SampleArrived -= OnSample;
        MonitorHub.SampleArrived += OnSample;
        if (MonitorHub.Latest is { } s) OnSample(s);
    }

    public override void OnNavigatedFrom() => MonitorHub.SampleArrived -= OnSample;

    private void OnSample(MonitorSample s)
    {
        Cpu.Load = s.CpuPercent is { } c ? $"Auslastung {Format.Percent(c)}" : "";
        Cpu.State = s.CpuPercent is null ? DataState.NotAvailable : s.CpuPercent >= 95 ? DataState.Warning : DataState.Ok;
        Ram.Load = s.RamPercent is { } r ? $"Belegt {Format.Percent(r)}" : "";
        Ram.State = s.RamPercent is null ? DataState.NotAvailable : s.RamPercent >= 90 ? DataState.Warning : DataState.Ok;
        Gpu.Load = s.GpuPercent is { } g ? $"Auslastung {Format.Percent(g)}" : "Auslastung: nicht verfügbar";
        Gpu.Temperature = s.GpuTempC is { } t ? $"Temperatur: {t} °C ({s.GpuTempSource})" : "Temperatur: nicht verfügbar";
        Cpu.Temperature = s.CpuTempC is { } ct ? $"Temperatur: {ct} °C ({s.CpuTempSource})" : "Temperatur: HWiNFO/LibreHardwareMonitor starten";
        Gpu.State = s.GpuTempC >= 85 ? DataState.Problem : s.GpuPercent is null && s.GpuTempC is null ? DataState.NotAvailable : DataState.Ok;
        Network.Load = $"↓ {Format.BitsPerSecond(s.DownloadBitsPerSecond)} · ↑ {Format.BitsPerSecond(s.UploadBitsPerSecond)}";
    }

    [RelayCommand]
    private void Open(MapNode? node)
    {
        PageViewModel? target = node?.Key switch
        {
            "cpu" or "ram" or "gpu" or "storage" or "display" => _nav.Page<HardwareViewModel>(),
            "net" => _nav.Page<NetworkViewModel>(),
            "inet" => _nav.Page<SpeedtestViewModel>(),
            "usb" => _nav.Page<DriversViewModel>(),
            _ => null,
        };
        if (target is not null) _nav.Navigate(target);
    }

    [RelayCommand]
    private void OpenBoard() => _nav.Navigate(_nav.Page<TechnicianViewModel>());
}
