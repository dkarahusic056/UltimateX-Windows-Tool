using UltimateX.Core.Common;
using UltimateX.Services.Common;

namespace UltimateX.Services.Drivers;

public sealed record DriverInfo(
    string DeviceName,
    string DeviceClass,
    string? Manufacturer,
    string? DriverVersion,
    DateTime? DriverDate,
    string? Provider,
    bool? IsSigned,
    string? Signer,
    string? InfName,
    string? HardwareId);

/// <summary>
/// Driver Manager – Phase 1: vollständige, schreibgeschützte Inventur aller Gerätetreiber (§30).
/// Treiber-Updates (§31/§32) folgen in Phase 3 ausschließlich über die Windows-Update-API
/// (nur von Microsoft signierte, für die Hardware-ID freigegebene Pakete). Es werden NIE Treiber
/// von Drittseiten heruntergeladen.
/// </summary>
public sealed class DriverService
{
    public (IReadOnlyList<DriverInfo> Drivers, DataState State) GetDrivers()
    {
        var r = Wmi.Query(
            "SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, DriverProviderName, IsSigned, Signer, InfName, HardWareID FROM Win32_PnPSignedDriver",
            timeoutSeconds: 60);

        var list = r.Rows
            .Where(x => x.Str("DeviceName") is not null)
            .Select(x => new DriverInfo(
                x.Str("DeviceName")!,
                x.Str("DeviceClass") ?? "Sonstige",
                x.Str("Manufacturer"),
                x.Str("DriverVersion"),
                x.Date("DriverDate"),
                x.Str("DriverProviderName"),
                x.Bool("IsSigned"),
                x.Str("Signer"),
                x.Str("InfName"),
                x.Str("HardWareID")))
            .OrderBy(d => ClassOrder(d.DeviceClass))
            .ThenBy(d => d.DeviceName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return (list, r.State);
    }

    /// <summary>Wichtige Klassen (GPU, Netzwerk, Audio …) zuerst anzeigen.</summary>
    private static int ClassOrder(string cls) => cls.ToUpperInvariant() switch
    {
        "DISPLAY" => 0,
        "NET" => 1,
        "MEDIA" => 2,
        "BLUETOOTH" => 3,
        "SYSTEM" => 4,
        "USB" => 5,
        "HDC" or "SCSIADAPTER" => 6,
        _ => 10,
    };

    public static string FriendlyClass(string cls) => cls.ToUpperInvariant() switch
    {
        "DISPLAY" => "Grafik",
        "NET" => "Netzwerk",
        "MEDIA" => "Audio/Medien",
        "BLUETOOTH" => "Bluetooth",
        "SYSTEM" => "System/Chipsatz",
        "USB" => "USB",
        "HDC" or "SCSIADAPTER" => "Speichercontroller",
        "DISKDRIVE" => "Laufwerke",
        "MONITOR" => "Monitore",
        "HIDCLASS" or "KEYBOARD" or "MOUSE" => "Eingabegeräte",
        "PRINTER" or "PRINTQUEUE" => "Drucker",
        "SOFTWAREDEVICE" or "SOFTWARECOMPONENT" => "Softwaregeräte",
        _ => cls,
    };
}
