using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
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
        if (state != DataState.Ok) StatusMessage = "Treiberliste unvollständig (WMI nicht vollständig verfügbar).";
        AnnotateUpdates();
        ApplyFilter();
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
