using UltimateX.Core.Common;
using UltimateX.Services.Common;

namespace UltimateX.Services.Hardware;

public sealed record PnpDevice(string Name, string Class, string? Manufacturer, string DeviceId, IReadOnlyList<string> HardwareIds, string Status, int? ErrorCode)
{
    public DataState State => ErrorCode is null or 0 ? DataState.Ok : DataState.Problem;

    /// <summary>Geräte-Manager-Fehlercode als Text (nur die häufigsten; sonst Code).</summary>
    public string ErrorText => ErrorCode switch
    {
        null or 0 => "Gerät funktioniert",
        1 => "Nicht richtig konfiguriert (Code 1)",
        10 => "Kann nicht gestartet werden (Code 10)",
        18 => "Treiber neu installieren (Code 18)",
        22 => "Deaktiviert (Code 22)",
        28 => "Kein Treiber installiert (Code 28)",
        31 => "Treiber konnte nicht geladen werden (Code 31)",
        43 => "Vom Gerät gemeldeter Fehler (Code 43)",
        45 => "Nicht angeschlossen (Code 45)",
        52 => "Treibersignatur nicht überprüfbar (Code 52)",
        _ => $"Code {ErrorCode}",
    };
}

public sealed record ServiceEntry(string Name, string DisplayName, string State, string StartMode, string? Account, string? Path);

public sealed record BatteryInfo(int? ChargePercent, string Status, int? DesignCapacity, int? FullChargeCapacity, int? RuntimeMinutes)
{
    /// <summary>Akkugesundheit = volle Ladekapazität / Designkapazität (nur wenn beide Werte gemeldet werden).</summary>
    public int? HealthPercent => DesignCapacity is > 0 && FullChargeCapacity is > 0
        ? (int)Math.Round(100.0 * FullChargeCapacity.Value / DesignCapacity.Value) : null;
}

/// <summary>Geräte-, Dienst- und Akku-Inventur (Technician/Admin Mode, §7, §10, §103 Battery Health). Nur lesend.</summary>
public sealed class InventoryService
{
    public (IReadOnlyList<PnpDevice> Devices, DataState State) GetDevices()
    {
        var r = Wmi.Query("SELECT Name, PNPClass, Manufacturer, DeviceID, HardwareID, Status, ConfigManagerErrorCode FROM Win32_PnPEntity", timeoutSeconds: 60);
        var list = r.Rows
            .Where(x => x.Str("DeviceID") is not null)
            .Select(x => new PnpDevice(
                x.Str("Name") ?? "(ohne Namen)",
                x.Str("PNPClass") ?? "Sonstige",
                x.Str("Manufacturer"),
                x.Str("DeviceID")!,
                x["HardwareID"] is string[] ids ? ids : [],
                x.Str("Status") ?? "?",
                (int?)x.U64("ConfigManagerErrorCode")))
            .OrderBy(d => d.Class, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return (list, r.State);
    }

    public IReadOnlyList<ServiceEntry> GetServices()
    {
        var r = Wmi.Query("SELECT Name, DisplayName, State, StartMode, StartName, PathName FROM Win32_Service");
        return r.Rows
            .Where(x => x.Str("Name") is not null)
            .Select(x => new ServiceEntry(x.Str("Name")!, x.Str("DisplayName") ?? x.Str("Name")!, x.Str("State") ?? "?", x.Str("StartMode") ?? "?", x.Str("StartName"), x.Str("PathName")))
            .OrderBy(s => s.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public BatteryInfo? GetBattery()
    {
        var b = Wmi.Query("SELECT EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime FROM Win32_Battery").Rows.FirstOrDefault();
        if (b is null) return null; // Desktop-PC ohne Akku

        var status = b.U64("BatteryStatus") switch
        {
            1 => "Entlädt",
            2 => "Netzbetrieb",
            3 => "Voll geladen",
            4 or 5 => "Niedrig/kritisch",
            6 or 7 or 8 or 9 => "Lädt",
            _ => "unbekannt",
        };
        var design = Wmi.Query("SELECT DesignedCapacity FROM BatteryStaticData", @"root\wmi").Rows.FirstOrDefault()?.U64("DesignedCapacity");
        var full = Wmi.Query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity", @"root\wmi").Rows.FirstOrDefault()?.U64("FullChargedCapacity");
        var runtime = b.U64("EstimatedRunTime");
        return new BatteryInfo((int?)b.U64("EstimatedChargeRemaining"), status, (int?)design, (int?)full,
            runtime is > 0 and < 71582788 ? (int?)runtime : null); // 71582788 = "unbekannt" laut WMI
    }
}
