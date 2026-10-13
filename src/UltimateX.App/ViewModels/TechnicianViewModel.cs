using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;

namespace UltimateX.App.ViewModels;

public sealed partial class TechNode : ObservableObject
{
    private readonly Action<TechNode> _onSelected;

    public TechNode(string title, string glyph, Action<TechNode> onSelected, IReadOnlyList<InfoSection>? sections = null)
    {
        Title = title;
        Glyph = glyph;
        _onSelected = onSelected;
        Sections = sections ?? [];
    }

    public string Title { get; }
    public string Glyph { get; }
    public ObservableCollection<TechNode> Children { get; } = [];
    public IReadOnlyList<InfoSection> Sections { get; set; }

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isExpanded = true;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }
}

/// <summary>
/// Technician Mode (§7): Hardware-Baum links, technische Details rechts – inkl. Hardware-IDs, Device-IDs,
/// Treiberversionen, Geräte-Manager-Fehlercodes, Event-IDs sowie Exit Codes und Originalausgabe der letzten Admin-Befehle.
/// </summary>
public sealed partial class TechnicianViewModel : PageViewModel
{
    public override string Title => "Technician Mode";
    public override string Glyph => "\uE90F";
    public override string Keywords => "technician techniker hardware id device id geräte pnp fehlercode exit code event id diagnose";

    public ObservableCollection<TechNode> Tree { get; } = [];

    [ObservableProperty] private TechNode? _selected;
    [ObservableProperty] private bool _sensitiveRevealed;

    public ObservableCollection<CommandHistoryEntry> Commands => CommandHistory.Entries;

    public bool ShowCommands => Selected?.Title == "Diagnose";

    partial void OnSelectedChanged(TechNode? value) => OnPropertyChanged(nameof(ShowCommands));

    private bool Masked => AppServices.Settings.SensitiveMode && !SensitiveRevealed;

    protected override async Task LoadAsync()
    {
        var hwTask = Task.Run(() => AppServices.Hardware.Collect());
        var devTask = Task.Run(() => AppServices.Inventory.GetDevices());
        var drvTask = Task.Run(() => AppServices.Drivers.GetDrivers());
        var probTask = Task.Run(() => AppServices.Health.GetTopProblems(7, 25));

        var hw = await hwTask;
        AppServices.LastHardware = hw;
        var (devices, _) = await devTask;
        var (drivers, _) = await drvTask;
        var problems = await probTask;

        void Select(TechNode n) => Selected = n;
        string Id(string v) => Masked ? SensitiveMasker.MaskGeneric(v) : v;

        IReadOnlyList<InfoSection> DeviceSections(Func<PnpDevice, bool> filter) => devices.Where(filter).Take(60).Select(d =>
        {
            var drv = drivers.FirstOrDefault(x => x.HardwareId is not null && d.HardwareIds.Contains(x.HardwareId, StringComparer.OrdinalIgnoreCase));
            var items = new List<InfoItem>
            {
                new("Status", d.ErrorText, d.State),
                new("Klasse", d.Class),
                new("Hersteller", Format.OrNa(d.Manufacturer)),
                new("Device ID", Id(d.DeviceId), DataState.Ok, true),
                new("Hardware IDs", d.HardwareIds.Count == 0 ? "–" : string.Join("\n", d.HardwareIds.Take(4).Select(Id)), DataState.Ok, true),
            };
            if (drv is not null)
            {
                items.Add(new("Treiberversion", Format.OrNa(drv.DriverVersion)));
                items.Add(new("Treiberdatum", Format.DateText(drv.DriverDate)));
                items.Add(new("Treiberanbieter", Format.OrNa(drv.Provider)));
                items.Add(new("INF", Format.OrNa(drv.InfName)));
                items.Add(new("Signiert", drv.IsSigned switch { true => $"Ja ({drv.Signer})", false => "Nein", _ => "?" }, drv.IsSigned == false ? DataState.Warning : DataState.Ok));
            }
            return new InfoSection(d.Name, "\uE772", items);
        }).ToList();

        Tree.Clear();
        var root = new TechNode("SYSTEM", "\uE7F4", Select,
        [
            new InfoSection("Windows", "\uE770",
            [
                new("Edition", hw.Os.Caption), new("Version", Format.OrNa(hw.Os.DisplayVersion)), new("Build", hw.Os.Build),
                new("Architektur", hw.Os.Architecture), new("Uptime", Format.Duration(hw.Os.Uptime)),
                new("Prozess erhöht", hw.Os.IsElevated ? "Ja" : "Nein"),
            ]),
        ]);

        root.Children.Add(new TechNode("Mainboard", "\uE964", Select,
        [
            new InfoSection("Mainboard & BIOS", "\uE964",
            [
                new("Hersteller", Format.OrNa(hw.Board.Manufacturer)), new("Modell", Format.OrNa(hw.Board.Product)),
                new("Seriennummer", hw.Board.SerialNumber is null ? "nicht verfügbar" : Id(hw.Board.SerialNumber), DataState.Ok, true),
                new("BIOS", $"{hw.Board.BiosVendor} {hw.Board.BiosVersion}"), new("BIOS-Datum", Format.DateText(hw.Board.BiosDate)),
                new("Firmware", hw.Board.Firmware), new("Secure Boot", hw.Board.SecureBoot, hw.Board.SecureBootState), new("TPM", hw.Board.Tpm, hw.Board.TpmState),
            ]),
            .. DeviceSections(d => d.Class.Equals("System", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("Chipset", StringComparison.OrdinalIgnoreCase)),
        ]));

        root.Children.Add(new TechNode("CPU", "\uE950", Select,
        [
            new InfoSection("Prozessor", "\uE950", CpuItems(hw.Cpu)),
            .. DeviceSections(d => d.Class.Equals("Processor", StringComparison.OrdinalIgnoreCase)).Take(1),
        ]));

        root.Children.Add(new TechNode("Memory", "\uE964", Select,
        [
            new InfoSection("Arbeitsspeicher", "\uE964",
                hw.Memory.Modules.Select(m => new InfoItem(m.Slot, $"{Format.Bytes(m.CapacityBytes)} · {m.SpeedMTs} MT/s · {m.Manufacturer} · {m.PartNumber}")).ToList()),
        ]));

        root.Children.Add(new TechNode("GPU", "\uE7F4", Select, DeviceSections(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase))));
        root.Children.Add(new TechNode("Storage", "\uEDA2", Select, DeviceSections(d => d.Class is "DiskDrive" or "SCSIAdapter" or "HDC")));
        root.Children.Add(new TechNode("Network", "\uE968", Select, DeviceSections(d => d.Class.Equals("Net", StringComparison.OrdinalIgnoreCase))));
        root.Children.Add(new TechNode("USB", "\uE88E", Select, DeviceSections(d => d.Class.Equals("USB", StringComparison.OrdinalIgnoreCase))));

        var faulty = devices.Where(d => d.State != DataState.Ok).ToList();
        root.Children.Add(new TechNode($"Drivers ({faulty.Count} Problem(e))", "\uE772", Select,
            faulty.Count == 0
                ? [new InfoSection("Gerätestatus", "\uE772", [new InfoItem("Ergebnis", "Alle Geräte melden „funktioniert“.")])]
                : DeviceSections(d => d.State != DataState.Ok)));

        var sample = MonitorHub.Latest;
        var smart = SmartService.LoadCached();
        var sensorItems = new List<InfoItem>
        {
            new("CPU-Last", sample?.CpuPercent is { } c ? Format.Percent(c) : "nicht verfügbar"),
            new("GPU-Last", sample?.GpuPercent is { } g ? Format.Percent(g) : "nicht verfügbar"),
            new("GPU-Temperatur", sample?.GpuTempC is { } t ? $"{t} °C · {sample.GpuTempSource}" : "nicht verfügbar (Treiber meldet keine Temperatur)", sample?.GpuTempC is null ? DataState.NotAvailable : DataState.Ok),
            new("CPU-Temperatur", sample?.CpuTempC is { } ct ? $"{ct} °C · {sample.CpuTempSource}" : "nicht verfügbar (HWiNFO/LibreHardwareMonitor nicht aktiv)", sample?.CpuTempC is null ? DataState.NotAvailable : DataState.Ok),
            new("GPU-Lüfter", sample?.GpuFanPercent is { } f ? $"{f} %" : "nicht verfügbar", sample?.GpuFanPercent is null ? DataState.NotAvailable : DataState.Ok),
        };
        foreach (var r in AppServices.Monitor.Sensors.Where(r => r.Kind == Services.Sensors.SensorKind.Temperature).Take(40))
            sensorItems.Add(new InfoItem($"{r.Hardware} · {r.Name}", $"{r.Value:0.#} {r.Unit} ({r.Source})"));
        if (smart is not null)
            foreach (var d in smart.Disks)
                sensorItems.Add(new InfoItem($"{d.Name} (SMART)", d.TemperatureC is { } st ? $"{st} °C · Stand {smart.Time:dd.MM. HH:mm}" : "nicht gemeldet"));
        root.Children.Add(new TechNode("Sensors", "\uE9CA", Select, [new InfoSection("Sensoren", "\uE9CA", sensorItems)]));

        root.Children.Add(new TechNode("Diagnose", "\uE9D9", Select,
        [
            new InfoSection("Ereignisse (7 Tage) – Event-IDs", "\uE81C",
                problems.Select(p => new InfoItem($"{p.Provider} · ID {p.EventId}", $"{p.Count}× {p.Level} ({p.Log}) – zuletzt {p.LastSeen:dd.MM. HH:mm}",
                    p.Level == "Kritisch" ? DataState.Problem : DataState.Warning)).ToList()),
        ]));

        Tree.Add(root);
        root.IsSelected = true;
    }

    private static List<InfoItem> CpuItems(CpuInfo? cpu)
    {
        if (cpu is null) return [InfoItem.Unavailable("Status")];
        return
        [
            new("Modell", cpu.Name),
            new("Kerne/Threads", $"{cpu.Cores}/{cpu.Threads}"),
            new("Max. Takt", cpu.MaxClockMHz is { } m ? $"{m} MHz" : "nicht verfügbar"),
            new("L2/L3", $"{Format.Bytes(cpu.L2CacheKB * 1024)} / {Format.Bytes(cpu.L3CacheKB * 1024)}"),
            new("Sockel", Format.OrNa(cpu.Socket)),
        ];
    }
}
