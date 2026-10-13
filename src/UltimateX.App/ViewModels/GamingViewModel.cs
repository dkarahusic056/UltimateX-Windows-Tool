using CommunityToolkit.Mvvm.ComponentModel;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.Common;
using UltimateX.Services.Monitoring;

namespace UltimateX.App.ViewModels;

public sealed partial class GamingTile(string title, string glyph) : ObservableObject
{
    public string Title { get; } = title;
    public string Glyph { get; } = glyph;
    [ObservableProperty] private string _value = "–";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private double? _percent;
    [ObservableProperty] private DataState _state = DataState.NotAvailable;
}

/// <summary>Gaming / Performance Mode (§6): große Live-Kacheln für CPU, GPU, RAM, VRAM, Temperaturen, Takt, Netzwerk und Bildwiederholrate.</summary>
public sealed partial class GamingViewModel : PageViewModel
{
    public override string Title => "Gaming Mode";
    public override string Glyph => "\uE7FC";
    public override string Keywords => "gaming performance spiele fps hz vram gpu leistung";
    public override string? Subtitle => "Live-Leistung auf einen Blick";

    public GamingTile Cpu { get; } = new("CPU", "\uE950");
    public GamingTile Gpu { get; } = new("GPU", "\uE7F4");
    public GamingTile Ram { get; } = new("RAM", "\uE964");
    public GamingTile Vram { get; } = new("VRAM", "\uE7F4");
    public GamingTile GpuTemp { get; } = new("GPU-Temperatur", "\uE9CA");
    public GamingTile CpuTemp { get; } = new("CPU-Temperatur", "\uE9CA");
    public GamingTile Clock { get; } = new("CPU-Takt (max.)", "\uEC4A");
    public GamingTile Network { get; } = new("Netzwerk", "\uE968");
    public GamingTile Display { get; } = new("Bildschirm", "\uE7F8");

    [ObservableProperty] private IReadOnlyList<double>? _cpuHistory;
    [ObservableProperty] private IReadOnlyList<double>? _gpuHistory;
    [ObservableProperty] private IReadOnlyList<double>? _ramHistory;

    protected override async Task LoadAsync()
    {
        var hw = AppServices.LastHardware ?? await Task.Run(() => AppServices.Hardware.Collect());
        AppServices.LastHardware = hw;
        var monitors = await Task.Run(() => AppServices.Displays.GetMonitors());

        Clock.Value = hw.Cpu?.MaxClockMHz is { } mhz ? $"{mhz / 1000.0:0.00} GHz" : "nicht verfügbar";
        Clock.Detail = hw.Cpu is null ? "" : $"{hw.Cpu.Name} · {hw.Cpu.Cores}C/{hw.Cpu.Threads}T";
        Clock.State = hw.Cpu is null ? DataState.NotAvailable : DataState.Ok;

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
        Display.Value = primary is null ? "nicht verfügbar" : $"{primary.RefreshHz} Hz";
        Display.Detail = primary is null ? "" : $"{primary.Width} × {primary.Height} · {primary.Hdr} · {monitors.Count} Monitor(e)";
        Display.State = primary is null ? DataState.NotAvailable : DataState.Ok;

        var gpu = hw.Gpus.FirstOrDefault(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) ?? hw.Gpus.FirstOrDefault();
        Gpu.Detail = gpu?.Name ?? "";
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
        Set(Cpu, s.CpuPercent, s.CpuPercent is { } c ? Format.Percent(c) : "nicht verfügbar", p => p >= 90 ? DataState.Warning : DataState.Ok);
        Set(Gpu, s.GpuPercent, s.GpuPercent is { } g ? Format.Percent(g) : "nicht verfügbar", _ => DataState.Ok);
        Set(Ram, s.RamPercent, s.RamPercent is { } r ? $"{Format.Percent(r)} · {Format.Bytes(s.RamUsedBytes)}" : "nicht verfügbar", p => p >= 90 ? DataState.Warning : DataState.Ok);

        var vramTotal = AppServices.LastHardware?.Gpus.Select(x => x.VramBytes).Where(x => x is not null).DefaultIfEmpty(null).Max();
        double? vramPct = s.VramUsedBytes is { } u && vramTotal is > 0 ? 100.0 * u / vramTotal.Value : null;
        Set(Vram, vramPct, s.VramUsedBytes is { } used ? $"{Format.Bytes(used)} / {Format.Bytes(vramTotal)}" : "nicht verfügbar", p => p >= 95 ? DataState.Warning : DataState.Ok);

        CpuTemp.Value = s.CpuTempC is { } cpuT ? $"{cpuT} °C" : "nicht verfügbar";
        CpuTemp.Detail = s.CpuTempC is not null ? s.CpuTempSource ?? "" : "HWiNFO oder LibreHardwareMonitor starten";
        CpuTemp.Percent = s.CpuTempC;
        CpuTemp.State = s.CpuTempC switch { null => DataState.NotAvailable, >= 90 => DataState.Problem, >= 80 => DataState.Warning, _ => DataState.Ok };
        GpuTemp.Value = s.GpuTempC is { } t ? $"{t} °C" : "nicht verfügbar";
        GpuTemp.Detail = s.GpuTempC is not null
            ? (s.GpuFanPercent is { } f ? $"Lüfter {f} % · {s.GpuTempSource}" : s.GpuTempSource ?? "")
            : "Grafiktreiber meldet keine Temperatur";
        GpuTemp.Percent = s.GpuTempC;
        GpuTemp.State = s.GpuTempC switch { null => DataState.NotAvailable, >= 85 => DataState.Problem, >= 75 => DataState.Warning, _ => DataState.Ok };
        if (s.GpuTempC >= 85)
            AppServices.Notifications.Notify(Services.SystemTools.NotificationKind.TemperatureWarning, "GPU-Temperatur hoch", $"Die Grafikkarte meldet {s.GpuTempC} °C.");

        Network.Value = $"↓ {Format.BitsPerSecond(s.DownloadBitsPerSecond)}";
        Network.Detail = $"↑ {Format.BitsPerSecond(s.UploadBitsPerSecond)}";
        Network.State = DataState.Ok;

        var recent = MonitorHub.Recent(120);
        CpuHistory = recent.Select(x => x.CpuPercent ?? 0).ToArray();
        GpuHistory = recent.Select(x => x.GpuPercent ?? 0).ToArray();
        RamHistory = recent.Select(x => x.RamPercent ?? 0).ToArray();
    }

    private static void Set(GamingTile tile, double? percent, string value, Func<double, DataState> state)
    {
        tile.Value = value;
        tile.Percent = percent;
        tile.State = percent is { } p ? state(p) : DataState.NotAvailable;
    }
}
