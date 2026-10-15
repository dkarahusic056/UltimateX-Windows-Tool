using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Common;

namespace UltimateX.Services.Hardware;

/// <summary>Zustand eines physischen Laufwerks (v1.2 §7). Nicht gemeldete Werte sind null und werden als N/A angezeigt.</summary>
public sealed record DriveHealthInfo(
    string Name,
    string? Model,
    string? Manufacturer,
    string? Firmware,
    string? SerialNumber,
    string MediaType,
    string BusType,
    ulong? SizeBytes,
    string WindowsHealth,
    DataState WindowsHealthState,
    int? TemperatureC,
    int? TemperatureMaxC,
    long? PowerOnHours,
    long? PowerCycles,
    int? WearPercent,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? ReadErrorsTotal,
    DateTime? SmartTime,
    DriveHealthResult Verdict);

/// <summary>
/// Verbindet die (ohne Adminrechte lesbaren) Laufwerksdaten aus MSFT_PhysicalDisk mit den SMART-/Zuverlässigkeitszählern
/// (MSFT_StorageReliabilityCounter, über den Admin-Helper gelesen und zwischengespeichert).
/// Windows stellt TBW/geschriebene Datenmenge sowie neu zugewiesene/ausstehende Sektoren nicht herstellerübergreifend bereit –
/// diese Werte werden deshalb als N/A geführt statt geschätzt.
/// </summary>
public sealed class DriveHealthService
{
    public IReadOnlyList<DriveHealthInfo> GetDrives(SmartSnapshot? smart)
    {
        var r = Wmi.Query("SELECT FriendlyName, Model, Manufacturer, FirmwareVersion, SerialNumber, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage);
        var list = new List<DriveHealthInfo>();
        foreach (var row in r.Rows)
        {
            var name = row.Str("FriendlyName") ?? "Datenträger";
            var bus = row.U64("BusType") switch
            {
                17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", 12 => "SD", 13 => "MMC", 15 => "Virtuell",
                _ => "Sonstige",
            };
            var media = row.U64("MediaType") switch
            {
                3 => "HDD",
                4 => bus == "NVMe" ? "NVMe SSD" : "SSD",
                5 => "SCM",
                _ => bus == "NVMe" ? "NVMe SSD" : "Unbekannt",
            };
            var (health, state) = row.U64("HealthStatus") switch
            {
                0 => ("Fehlerfrei", DataState.Ok),
                1 => ("Warnung", DataState.Warning),
                2 => ("Fehlerhaft", DataState.Problem),
                _ => ("Unbekannt", DataState.NotAvailable),
            };
            var s = smart?.Disks.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
            var input = new DriveHealthInput(name, media, state, s?.TemperatureC, s?.TemperatureMaxC, s?.PowerOnHours, s?.StartStopCycles,
                s?.WearPercent, s?.ReadErrorsUncorrected, s?.WriteErrorsUncorrected);
            list.Add(new DriveHealthInfo(name, row.Str("Model"), row.Str("Manufacturer"), row.Str("FirmwareVersion"), row.Str("SerialNumber"),
                media, bus, row.U64("Size"), health, state, s?.TemperatureC, s?.TemperatureMaxC, s?.PowerOnHours, s?.StartStopCycles,
                s?.WearPercent, s?.ReadErrorsUncorrected, s?.WriteErrorsUncorrected, s?.ReadErrorsTotal, smart?.Time,
                DriveHealthEvaluator.Evaluate(input)));
        }
        return list;
    }
}
