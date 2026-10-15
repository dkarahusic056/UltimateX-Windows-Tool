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

/// <summary>Laufwerkskarte: Bewertung GOOD / WARNING / CRITICAL und alle SMART-Werte (N/A, wenn nicht gemeldet).</summary>
public sealed record DriveCard(string Name, string Verdict, DataState State, IReadOnlyList<InfoItem> Items, string Reasons);

/// <summary>STORAGE / DRIVE HEALTH (v1.2 §7) – Zustand aller Laufwerke und Belegung der Volumes.</summary>
public sealed partial class StorageViewModel : PageViewModel
{
    public override string Title => "Storage";
    public override string Glyph => "";
    public override string Keywords => "storage laufwerk ssd hdd nvme smart gesundheit health temperatur tbw betriebsstunden verschleiß festplatte datenträger";
    public override string? Subtitle => "Laufwerksgesundheit (SMART) und Belegung";

    public ObservableCollection<DriveCard> Drives { get; } = [];
    public ObservableCollection<InfoItem> Volumes { get; } = [];

    [ObservableProperty] private string? _smartInfo;

    protected override async Task LoadAsync()
    {
        var smart = SmartService.LoadCached();
        var (drives, volumes) = await Task.Run(() => (AppServices.DriveHealth.GetDrives(smart), AppServices.Hardware.GetVolumes()));
        SmartInfo = smart is null
            ? "SMART-Werte wurden noch nicht gelesen – „SMART lesen“ benötigt Administratorrechte (UAC)."
            : "SMART-Werte vom " + Format.DateTimeText(smart.Time);
        Drives.Clear();
        foreach (var d in drives) Drives.Add(ToCard(d));
        Volumes.Clear();
        foreach (var v in volumes)
            Volumes.Add(new InfoItem($"{v.Letter} {v.Label}".Trim(),
                $"{Format.Bytes(v.FreeBytes)} frei von {Format.Bytes(v.SizeBytes)} · {v.FileSystem}",
                v.UsedPercent >= 95 ? DataState.Problem : v.UsedPercent >= 85 ? DataState.Warning : DataState.Ok));
    }

    private static DriveCard ToCard(DriveHealthInfo d)
    {
        static string Na(object? v, string unit = "") => v is null ? "N/A" : $"{Convert.ToString(v, CultureInfo.CurrentCulture)}{unit}";
        var masked = AppServices.Settings.SensitiveMode && d.SerialNumber is { Length: > 4 } sn ? "••••" + sn[^4..] : d.SerialNumber;
        var errors = d.ReadErrorsUncorrected is null && d.WriteErrorsUncorrected is null ? "N/A"
            : $"{(d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0)} nicht korrigierbar · {Na(d.ReadErrorsTotal)} Lesefehler gesamt";
        var items = new List<InfoItem>
        {
            new("Health", d.Verdict.Label, d.Verdict.State),
            new("Windows-Status", d.WindowsHealth, d.WindowsHealthState),
            new("Temperatur", d.TemperatureC is { } t ? $"{t} °C" + (d.TemperatureMaxC is { } m ? $" (Grenze {m} °C)" : "") : "N/A",
                d.TemperatureC is null ? DataState.NotAvailable : DataState.Ok),
            new("Betriebsstunden", Na(d.PowerOnHours, " h"), d.PowerOnHours is null ? DataState.NotAvailable : DataState.Ok),
            new("Einschaltvorgänge", Na(d.PowerCycles), d.PowerCycles is null ? DataState.NotAvailable : DataState.Ok),
            new("Verschleiß", d.WearPercent is { } w ? $"{w} % (Restlebensdauer {100 - w} %)" : "N/A", d.WearPercent is null ? DataState.NotAvailable : d.WearPercent >= 70 ? DataState.Warning : DataState.Ok),
            new("TBW / Data Written", "N/A", DataState.NotAvailable),
            new("Reallocated Sectors", "N/A", DataState.NotAvailable),
            new("Pending Sectors", "N/A", DataState.NotAvailable),
            new("Fehler", errors, d.ReadErrorsUncorrected is null && d.WriteErrorsUncorrected is null ? DataState.NotAvailable
                : (d.ReadErrorsUncorrected ?? 0) + (d.WriteErrorsUncorrected ?? 0) > 0 ? DataState.Warning : DataState.Ok),
            new("Hersteller", Format.OrNa(d.Manufacturer)),
            new("Modell", Format.OrNa(d.Model)),
            new("Firmware", Format.OrNa(d.Firmware)),
            new("Typ", $"{d.MediaType} · {d.BusType} · {Format.Bytes(d.SizeBytes)}"),
            new("Seriennummer", Format.OrNa(masked), DataState.Ok, IsSensitive: true),
        };
        return new DriveCard(d.Name, d.Verdict.Label, d.Verdict.State, items,
            string.Join(" ", d.Verdict.Reasons) + " TBW sowie neu zugewiesene/ausstehende Sektoren stellt Windows nicht herstellerübergreifend bereit (Herstellerwerkzeug nutzen).");
    }

    [RelayCommand]
    private async Task ReadSmartAsync()
    {
        IsBusy = true;
        try
        {
            var (snap, error) = await AppServices.Smart.ReadAsync();
            StatusMessage = error ?? $"SMART-Werte von {snap?.Disks.Count ?? 0} Laufwerk(en) gelesen.";
        }
        finally { IsBusy = false; }
        await RefreshAsync();
    }
}
