using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;

namespace UltimateX.App.ViewModels;

public sealed record TreeNode(string Title, string? Detail, IReadOnlyList<TreeNode> Children);

/// <summary>Monitor als Grafik: Kastengröße proportional zur Auflösung (§21).</summary>
public sealed record MonitorCard(string Title, string Resolution, string Details, string Gpu, bool IsPrimary, double BoxWidth, double BoxHeight);

public sealed record VolumeView(string Letter, string Title, string Detail, double Percent, DataState State);

/// <summary>Systeminformationen, Hardware-Topologie und Laufwerksvisualisierung (§16, §18, §22).</summary>
public sealed partial class HardwareViewModel : PageViewModel
{
    public override string Title => "Hardware";
    public override string Glyph => "\uE950";
    public override string Keywords => "cpu prozessor gpu grafik nvidia amd intel radeon geforce ram arbeitsspeicher speicher mainboard bios uefi tpm secure disk ssd nvme hdd laufwerk windows build version";

    public ObservableCollection<InfoSection> Sections { get; } = [];
    public ObservableCollection<TreeNode> Topology { get; } = [];
    public ObservableCollection<VolumeView> Volumes { get; } = [];

    [ObservableProperty] private bool _sensitiveRevealed;

    private HardwareSnapshot? _snapshot;
    private IReadOnlyList<MonitorInfo> _monitors = [];
    private BatteryInfo? _battery;
    private SmartSnapshot? _smart;

    public ObservableCollection<MonitorCard> Monitors { get; } = [];

    [ObservableProperty] private bool _isReadingSmart;

    protected override async Task LoadAsync()
    {
        var hwTask = Task.Run(() => AppServices.Hardware.Collect());
        var monTask = Task.Run(() => AppServices.Displays.GetMonitors());
        var batTask = Task.Run(() => AppServices.Inventory.GetBattery());
        _snapshot = await hwTask;
        _monitors = await monTask;
        _battery = await batTask;
        _smart = SmartService.LoadCached();
        AppServices.LastHardware = _snapshot;
        Monitors.Clear();
        var maxW = _monitors.Count == 0 ? 1 : _monitors.Max(m => Math.Max(1, m.Width));
        foreach (var m in _monitors)
        {
            var w = 220.0 * m.Width / maxW;
            var h = m.Width > 0 ? w * m.Height / m.Width : 120;
            Monitors.Add(new MonitorCard(m.FriendlyName, $"{m.Width} × {m.Height} @ {m.RefreshHz} Hz",
                $"Skalierung {m.ScalingPercent} % · {m.Hdr} · Position {m.PositionX}, {m.PositionY}",
                m.Gpu, m.IsPrimary, w, h));
        }
        Build();
        if (_snapshot.Warnings.Count > 0) StatusMessage = string.Join(" ", _snapshot.Warnings);
    }

    public override void OnNavigatedTo()
    {
        // Nach LOCK oder Seitenwechsel sensible Werte wieder maskieren.
        if (SensitiveRevealed && !AppServices.Auth.HasActiveSession)
        {
            SensitiveRevealed = false;
            Build();
        }
    }

    [RelayCommand]
    private async Task RevealSensitiveAsync()
    {
        if (SensitiveRevealed)
        {
            SensitiveRevealed = false;
            Build();
            return;
        }
        if (!await AppServices.Guard.AuthenticateOnlyAsync("Seriennummern anzeigen")) return;
        SensitiveRevealed = true;
        Build();
    }

    /// <summary>SMART-Details erfordern Adminrechte → über Admin-Helper mit UAC (§44).</summary>
    [RelayCommand]
    private async Task ReadSmartAsync()
    {
        IsReadingSmart = true;
        try
        {
            var (snap, error) = await AppServices.Smart.ReadAsync();
            if (snap is null) { StatusMessage = error; return; }
            _smart = snap;
            Build();
            StatusMessage = $"SMART-Daten gelesen ({snap.Disks.Count} Datenträger).";
        }
        finally { IsReadingSmart = false; }
    }

    private string Sensitive(string? v)
        => v is null ? "nicht verfügbar" : (AppServices.Settings.SensitiveMode && !SensitiveRevealed ? SensitiveMasker.MaskGeneric(v) : v);

    private static string N(int? v) => v?.ToString(CultureInfo.CurrentCulture) ?? "nicht verfügbar";

    private void Build()
    {
        var hw = _snapshot;
        if (hw is null) return;
        Sections.Clear();

        var os = hw.Os;
        Sections.Add(new InfoSection("Windows", "\uE770",
        [
            new("Edition", os.Caption),
            new("Version", Format.OrNa(os.DisplayVersion)),
            new("Build", os.Build),
            new("Architektur", os.Architecture),
            new("Installiert am", Format.DateText(os.InstallDate)),
            new("Computername", os.ComputerName),
            new("Uptime", Format.Duration(os.Uptime)),
            new("Benutzerrechte", os.IsElevated ? "Administrator (erhöht)" : "Standardbenutzer"),
        ]));

        if (hw.Cpu is { } cpu)
        {
            Sections.Add(new InfoSection("CPU", "\uE950",
            [
                new("Modell", cpu.Name),
                new("Hersteller", cpu.Manufacturer),
                new("Kerne / Threads", $"{N(cpu.Cores)} / {N(cpu.Threads)}"),
                new("Max. Takt", cpu.MaxClockMHz is { } mhz ? $"{mhz / 1000.0:0.00} GHz" : "nicht verfügbar"),
                new("L2-Cache", cpu.L2CacheKB is { } l2 ? Format.Bytes(l2 * 1024) : "nicht verfügbar"),
                new("L3-Cache", cpu.L3CacheKB is { } l3 && l3 > 0 ? Format.Bytes(l3 * 1024) : "nicht verfügbar"),
                new("Sockel", Format.OrNa(cpu.Socket)),
                InfoItem.Unavailable("Temperatur", "nicht verfügbar (siehe Live Monitor)"),
            ]));
        }
        else
        {
            Sections.Add(new InfoSection("CPU", "\uE950", [InfoItem.Unavailable("Status", "Keine Daten über WMI verfügbar")]));
        }

        foreach (var gpu in hw.Gpus)
        {
            Sections.Add(new InfoSection("GPU", "\uE7F4",
            [
                new("Modell", gpu.Name),
                new("Hersteller", gpu.Vendor),
                new("VRAM", Format.Bytes(gpu.VramBytes), gpu.VramBytes is null ? DataState.NotAvailable : DataState.Ok),
                new("Treiberversion", Format.OrNa(gpu.DriverVersion)),
                new("Treiberdatum", Format.DateText(gpu.DriverDate)),
                new("Aktueller Modus", gpu.CurrentResolution is null ? "nicht verfügbar" : $"{gpu.CurrentResolution} @ {gpu.CurrentRefreshRate} Hz"),
            ]));
        }

        var mem = hw.Memory;
        var memItems = new List<InfoItem>
        {
            new("Gesamt", Format.Bytes(mem.TotalBytes)),
            new("Verwendet", Format.Bytes(mem.UsedBytes)),
            new("Verfügbar", Format.Bytes(mem.AvailableBytes)),
            new("Steckplätze", mem.SlotCount is { } sc ? $"{mem.Modules.Count} von {sc} belegt" : $"{mem.Modules.Count} Modul(e) erkannt"),
        };
        foreach (var m in mem.Modules)
            memItems.Add(new InfoItem(m.Slot, $"{Format.Bytes(m.CapacityBytes)} · {(m.SpeedMTs is { } sp ? sp + " MT/s" : "Takt ?")} · {m.Manufacturer} {m.PartNumber}".Trim()));
        Sections.Add(new InfoSection("Arbeitsspeicher", "\uE964", memItems));

        var b = hw.Board;
        Sections.Add(new InfoSection("Mainboard & Firmware", "\uE964",
        [
            new("Hersteller", Format.OrNa(b.Manufacturer)),
            new("Modell", Format.OrNa(b.Product)),
            new("Seriennummer", Sensitive(b.SerialNumber), b.SerialNumber is null ? DataState.NotAvailable : DataState.Ok, true),
            new("BIOS-Hersteller", Format.OrNa(b.BiosVendor)),
            new("BIOS-Version", Format.OrNa(b.BiosVersion)),
            new("BIOS-Datum", Format.DateText(b.BiosDate)),
            new("Firmware", b.Firmware),
            new("Secure Boot", b.SecureBoot, b.SecureBootState),
            new("TPM", b.Tpm, b.TpmState),
            InfoItem.Unavailable("Chipsatz", "nicht zuverlässig per Windows-API ermittelbar"),
        ]));

        foreach (var d in hw.Disks)
        {
            var items = new List<InfoItem>
            {
                new("Modell", d.Name),
                new("Typ", $"{d.MediaType} ({d.BusType})"),
                new("Größe", Format.Bytes(d.SizeBytes)),
                new("Status (Windows)", d.Health, d.HealthState),
            };
            var sm = _smart?.Disks.FirstOrDefault(x => x.Name == d.Name);
            if (sm is null)
            {
                items.Add(InfoItem.Unavailable("SMART-Details", "„SMART lesen“ (Adminrechte) ausführen"));
            }
            else
            {
                items.Add(new("Temperatur", sm.TemperatureC is { } t ? $"{t} °C" + (sm.TemperatureMaxC is { } tm ? $" (max. {tm} °C)" : "") : "nicht gemeldet",
                    sm.TemperatureC is null ? DataState.NotAvailable : sm.TemperatureC >= 70 ? DataState.Warning : DataState.Ok));
                items.Add(new("Betriebsstunden", sm.PowerOnHours is { } h ? $"{h:N0} h" : "nicht gemeldet", sm.PowerOnHours is null ? DataState.NotAvailable : DataState.Ok));
                items.Add(new("Restlebensdauer", sm.RemainingLifePercent is { } rl ? $"{rl} % (laut Laufwerk)" : "nicht gemeldet",
                    sm.RemainingLifePercent is null ? DataState.NotAvailable : sm.RemainingLifePercent < 10 ? DataState.Problem : sm.RemainingLifePercent < 30 ? DataState.Warning : DataState.Ok));
                items.Add(new("Unkorrigierte Lesefehler", sm.ReadErrorsUncorrected?.ToString("N0") ?? "nicht gemeldet",
                    sm.ReadErrorsUncorrected is null ? DataState.NotAvailable : sm.ReadErrorsUncorrected > 0 ? DataState.Warning : DataState.Ok));
                items.Add(new("Unkorrigierte Schreibfehler", sm.WriteErrorsUncorrected?.ToString("N0") ?? "nicht gemeldet",
                    sm.WriteErrorsUncorrected is null ? DataState.NotAvailable : sm.WriteErrorsUncorrected > 0 ? DataState.Warning : DataState.Ok));
                items.Add(new("Start/Stopp-Zyklen", sm.StartStopCycles?.ToString("N0") ?? "nicht gemeldet", sm.StartStopCycles is null ? DataState.NotAvailable : DataState.Ok));
                items.Add(new("SMART-Stand", _smart!.Time.ToString("dd.MM.yyyy HH:mm")));
            }
            Sections.Add(new InfoSection("Datenträger", "\uEDA2", items,
                "Werte stammen direkt vom Laufwerk. Eine Ausfallprognose wird bewusst nicht gestellt."));
        }

        if (_battery is { } bat)
        {
            Sections.Add(new InfoSection("Akku", "\uE83F",
            [
                new("Ladestand", bat.ChargePercent is { } c ? $"{c} %" : "nicht verfügbar"),
                new("Status", bat.Status),
                new("Designkapazität", bat.DesignCapacity is { } dc ? $"{dc:N0} mWh" : "nicht verfügbar", bat.DesignCapacity is null ? DataState.NotAvailable : DataState.Ok),
                new("Volle Ladekapazität", bat.FullChargeCapacity is { } fc ? $"{fc:N0} mWh" : "nicht verfügbar", bat.FullChargeCapacity is null ? DataState.NotAvailable : DataState.Ok),
                new("Akkugesundheit", bat.HealthPercent is { } hp ? $"{hp} %" : "nicht verfügbar",
                    bat.HealthPercent is null ? DataState.NotAvailable : bat.HealthPercent < 60 ? DataState.Warning : DataState.Ok),
                new("Restlaufzeit", bat.RuntimeMinutes is { } rm ? $"{rm / 60} h {rm % 60} min" : "nicht verfügbar"),
            ]));
        }

        Volumes.Clear();
        foreach (var v in hw.Volumes)
        {
            var pct = v.UsedPercent ?? 0;
            Volumes.Add(new VolumeView(v.Letter,
                $"{v.Letter} {(string.IsNullOrWhiteSpace(v.Label) ? "" : v.Label)}".Trim() + $" · {Format.OrNa(v.FileSystem)}",
                $"{Format.Bytes(v.UsedBytes)} verwendet · {Format.Bytes(v.FreeBytes)} frei · {Format.Bytes(v.SizeBytes)} gesamt · {pct:0} %",
                pct, pct >= 95 ? DataState.Problem : pct >= 85 ? DataState.Warning : DataState.Ok));
        }

        // Hardware-Topologie (§18) – nur aus tatsächlich erkannten Komponenten aufgebaut.
        Topology.Clear();
        var children = new List<TreeNode>();
        if (hw.Cpu is not null) children.Add(new TreeNode("CPU", hw.Cpu.Name, []));
        children.Add(new TreeNode("RAM", Format.Bytes(mem.TotalBytes),
            mem.Modules.Select(m => new TreeNode(m.Slot, Format.Bytes(m.CapacityBytes), [])).ToList()));
        if (hw.Gpus.Count > 0) children.Add(new TreeNode("Grafik", null, hw.Gpus.Select(g => new TreeNode("GPU", g.Name, [])).ToList()));
        if (hw.Disks.Count > 0)
            children.Add(new TreeNode("Speicher", null, hw.Disks.Select(d => new TreeNode(d.BusType, $"{d.Name} · {Format.Bytes(d.SizeBytes)}", [])).ToList()));
        Topology.Add(new TreeNode("MAINBOARD", $"{b.Manufacturer} {b.Product}".Trim(), children));
    }
}
