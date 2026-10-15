using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Drivers;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

public sealed record DriverRow(string Device, string RawClass, string Class, string Manufacturer, string Version, string Date, string Signed, string Signer, string Inf, DataState State, string? HardwareId)
{
    /// <summary>Neue Version laut Windows Update (§30 "neue Version", "Quelle").</summary>
    public string NewVersion { get; init; } = "–";
    public string Source { get; init; } = "–";
}

/// <summary>Driver Manager – Inventur (§30). Updates folgen über die Windows-Update-API (Phase 3).</summary>
public sealed partial class DriversViewModel : PageViewModel
{
    public override string Title => "Treiber";
    public override string Glyph => "\uE772";
    public override string Keywords => "treiber driver geräte device chipsatz audio bluetooth usb grafiktreiber nvidia amd intel realtek";

    private List<DriverRow> _all = [];

    public ObservableCollection<DriverRow> Rows { get; } = [];

    /// <summary>DRIVER HEALTH CENTER (v1.2 §34): Gerätefehler, unbekannte Geräte, unsignierte und veraltete Treiber.</summary>
    public ObservableCollection<Finding> Health { get; } = [];

    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private bool _importantOnly = true;
    [ObservableProperty] private string? _summary;

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnImportantOnlyChanged(bool value) => ApplyFilter();

    private static readonly HashSet<string> ImportantClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Display", "Net", "Media", "Bluetooth", "System", "USB", "HDC", "SCSIAdapter" };

    protected override async Task LoadAsync()
    {
        var (drivers, state) = await Task.Run(() => AppServices.Drivers.GetDrivers());
        _all = drivers.Select(d => new DriverRow(
            d.DeviceName,
            d.DeviceClass,
            DriverService.FriendlyClass(d.DeviceClass),
            d.Manufacturer ?? d.Provider ?? "–",
            d.DriverVersion ?? "–",
            d.DriverDate?.ToString("dd.MM.yyyy") ?? "–",
            d.IsSigned switch { true => "Ja", false => "Nein", _ => "?" },
            d.Signer ?? "–",
            d.InfName ?? "–",
            d.IsSigned == false ? DataState.Warning : DataState.Ok,
            d.HardwareId)).ToList();

        var unsigned = drivers.Count(d => d.IsSigned == false);
        Summary = $"{drivers.Count} Gerätetreiber · {unsigned} ohne Signatur";
        var (devices, _) = await Task.Run(() => AppServices.Inventory.GetDevices());
        BuildHealth(drivers, devices, unsigned);
        if (state != DataState.Ok) StatusMessage = "Treiberliste unvollständig (WMI nicht vollständig verfügbar).";
        AnnotateUpdates();
        ApplyFilter();
    }

    private void BuildHealth(IReadOnlyList<DriverInfo> drivers, IReadOnlyList<Services.Hardware.PnpDevice> devices, int unsigned)
    {
        Health.Clear();
        var errors = devices.Where(d => d.ErrorCode is > 0 and not 28 and not 22).ToList();
        var unknown = devices.Where(d => d.ErrorCode == 28 || d.Class.Equals("Unknown", StringComparison.OrdinalIgnoreCase)).ToList();
        Health.Add(errors.Count == 0
            ? new Finding("Gerätefehler", CheckStatus.Pass, "Keine Geräte mit Fehlercode")
            : new Finding("Gerätefehler", CheckStatus.Fail, $"{errors.Count} Gerät(e): " + string.Join(", ", errors.Take(4).Select(e => $"{e.Name} ({e.ErrorText})")),
                "Details im Geräte-Manager; Treiber über Windows Update oder die Herstellerseite installieren."));
        Health.Add(unknown.Count == 0
            ? new Finding("Unbekannte Geräte", CheckStatus.Pass, "Keine")
            : new Finding("Unbekannte Geräte", CheckStatus.Warning, $"{unknown.Count} Gerät(e) ohne Treiber: " + string.Join(", ", unknown.Take(4).Select(u => u.Name)),
                "Chipsatz-/Mainboardtreiber des Herstellers oder Windows Update („Optionale Updates“) installieren."));
        Health.Add(unsigned == 0
            ? new Finding("Unsignierte Treiber", CheckStatus.Pass, "Alle Treiber sind signiert")
            : new Finding("Unsignierte Treiber", CheckStatus.Warning, $"{unsigned} Treiber ohne gültige Signatur", "Herkunft prüfen – nur Treiber vom Gerätehersteller oder aus Windows Update verwenden."));

        var now = DateTime.Now;
        var oldGpu = drivers.Where(d => d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase) && d.DriverDate is { } dt && (now - dt).TotalDays > 365).ToList();
        var oldImportant = drivers.Where(d => ImportantClasses.Contains(d.DeviceClass) && !d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase)
                                              && d.DriverDate is { } dt && (now - dt).TotalDays > 5 * 365
                                              && !(d.Provider ?? "").Contains("Microsoft", StringComparison.OrdinalIgnoreCase)).ToList();
        Health.Add(oldGpu.Count == 0
            ? new Finding("Grafiktreiber", CheckStatus.Pass, "Aktuell (jünger als 1 Jahr)")
            : new Finding("Grafiktreiber", CheckStatus.Warning, string.Join(", ", oldGpu.Select(g => $"{g.DeviceName} vom {g.DriverDate:dd.MM.yyyy}")),
                "Aktuellen Treiber von NVIDIA, AMD oder Intel installieren (offizielle Herstellerseite)."));
        Health.Add(oldImportant.Count == 0
            ? new Finding("Veraltete Treiber", CheckStatus.Pass, "Keine auffällig alten Treiber wichtiger Geräteklassen")
            : new Finding("Veraltete Treiber", CheckStatus.Warning, $"{oldImportant.Count} Treiber älter als 5 Jahre",
                "Mit „Nach Treiber-Updates suchen“ prüfen; ansonsten Herstellerseite. Keine Treiberportale von Drittanbietern verwenden."));
    }

    /// <summary>Ordnet gefundene Windows-Update-Treiber den installierten Geräten über die Hardware-ID zu.</summary>
    private void AnnotateUpdates()
    {
        var updates = UpdatesViewModel.LastDriverUpdates;
        if (updates.Count == 0) return;
        _all = _all.Select(r =>
        {
            var u = updates.FirstOrDefault(x => x.DriverHardwareId is not null && r.HardwareId is not null
                                                && string.Equals(x.DriverHardwareId, r.HardwareId, StringComparison.OrdinalIgnoreCase));
            return u is null ? r with { Source = "installiert" }
                : r with { NewVersion = $"Update vom {u.DriverDate:dd.MM.yyyy}", Source = "Windows Update", State = DataState.Warning };
        }).ToList();
        var count = _all.Count(r => r.Source == "Windows Update");
        Summary += $" · {count} Treiber-Update(s) über Windows Update";
    }

    [ObservableProperty] private bool _isSearchingUpdates;

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsSearchingUpdates = true;
        try
        {
            var (items, error) = await AppServices.WindowsUpdate.SearchAsync(true);
            if (error is not null) { StatusMessage = error; return; }
            UpdatesViewModel.SetDriverUpdates(items);
            StatusMessage = items.Count == 0
                ? "Windows Update bietet aktuell keine Treiber-Updates für diesen PC an."
                : $"{items.Count} Treiber-Update(s) gefunden – Installation unter „Updates“.";
            await RefreshAsync();
        }
        finally { IsSearchingUpdates = false; }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var q = Filter.Trim();
        foreach (var r in _all)
        {
            if (ImportantOnly && !ImportantClasses.Contains(r.RawClass)) continue;
            if (q.Length > 0 && !(r.Device.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                  || r.Manufacturer.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                  || r.Class.Contains(q, StringComparison.CurrentCultureIgnoreCase))) continue;
            Rows.Add(r);
        }
    }

    [RelayCommand]
    private void OpenDeviceManager() => ShowResult(AppServices.Tools.Launch(SystemTool.DeviceManager));

    [RelayCommand]
    private void OpenWindowsUpdate() => ShowResult(AppServices.Tools.Launch(SystemTool.WindowsUpdate));
}
