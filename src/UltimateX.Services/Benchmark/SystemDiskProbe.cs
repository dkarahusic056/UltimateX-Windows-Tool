using System.Management;
using System.Runtime.InteropServices;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;

namespace UltimateX.Services.Benchmark;

/// <summary>
/// Windows-Systemlaufwerk mit physischem Datenträger (MSFT_Partition → MSFT_PhysicalDisk) und – nur wenn eindeutig –
/// PCIe-Generation/Lanes des NVMe-Controllers. Null-Werte = nicht ermittelbar (N/A).
/// </summary>
public sealed record SystemDiskInfo(
    string Root,
    string? DiskName,
    string? BusType,
    string? MediaType,
    int? PcieGen,
    int? PcieWidth,
    double? FreePercent)
{
    /// <summary>Kurzbeschreibung, z. B. „Samsung SSD 990 PRO · NVMe PCIe 4.0 x4“.</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (DiskName is not null) parts.Add(DiskName);
            if (BusType is not null)
                parts.Add(BusType + (PcieGen is { } g ? $" PCIe {g}.0" + (PcieWidth is { } w ? $" x{w}" : "") : ""));
            return parts.Count == 0 ? Root : string.Join(" · ", parts);
        }
    }
}

public static class SystemDiskProbe
{
    /// <summary>Wurzel des Windows-Laufwerks („C:\“).</summary>
    public static string SystemRoot => Path.GetPathRoot(Environment.SystemDirectory) is { Length: > 0 } r ? r : "C:\\";

    public static SystemDiskInfo Detect()
    {
        var root = SystemRoot;
        double? free = null;
        try
        {
            var d = new DriveInfo(root);
            if (d.IsReady && d.TotalSize > 0) free = 100.0 * d.AvailableFreeSpace / d.TotalSize;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }

        var letter = DiskRunIdentity.Letter(root);
        var parts = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", Wmi.Storage);
        var part = parts.Rows.FirstOrDefault(p => p["DriveLetter"] switch
        {
            char c => char.ToUpperInvariant(c) == letter,
            ushort u => char.ToUpperInvariant((char)u) == letter,
            string { Length: > 0 } s => char.ToUpperInvariant(s[0]) == letter,
            _ => false,
        });
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.Storage);
        var number = part?.U64("DiskNumber")?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var disk = number is null ? null : disks.Rows.FirstOrDefault(x => x.Str("DeviceId") == number);
        if (disk is null) return new SystemDiskInfo(root, null, null, null, null, null, free);

        var busCode = disk.U64("BusType");
        var bus = busCode switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", _ => null };
        var media = disk.U64("MediaType") switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => bus == "NVMe" ? "SSD" : null };

        int? gen = null, width = null;
        if (bus == "NVMe")
        {
            var nvmeCount = disks.Rows.Count(x => x.U64("BusType") == 17);
            (gen, width) = StorageExpectation.UnambiguousNvmeLink(ReadNvmeControllerLinks(), nvmeCount);
        }
        return new SystemDiskInfo(root, disk.Str("FriendlyName"), bus, media, gen, width, free);
    }

    /// <summary>PCIe-Verbindung aller NVMe-Controller (DEVPKEY_PciDevice_CurrentLinkSpeed/-Width).</summary>
    private static List<(int? Gen, int? Width)> ReadNvmeControllerLinks()
    {
        var list = new List<(int? Gen, int? Width)>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'SCSIAdapter'");
            using var results = searcher.Get();
            foreach (ManagementObject dev in results)
            {
                using (dev)
                {
                    var name = dev["Name"]?.ToString() ?? "";
                    var pnp = dev["PNPDeviceID"]?.ToString() ?? "";
                    if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!name.Contains("NVM", StringComparison.OrdinalIgnoreCase)) continue;
                    var outParams = dev.InvokeMethod("GetDeviceProperties", null, null);
                    int? cw = null, cs = null;
                    if (outParams?["deviceProperties"] is ManagementBaseObject[] props)
                    {
                        foreach (var p in props)
                        {
                            using (p)
                            {
                                var key = p["KeyName"]?.ToString() ?? "";
                                int? v = p["Data"] switch { uint u => (int)u, int i => i, ushort us => us, byte b => b, _ => null };
                                if (v is null) continue;
                                if (key.Contains("CurrentLinkWidth", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 11", StringComparison.OrdinalIgnoreCase)) cw = v;
                                else if (key.Contains("CurrentLinkSpeed", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 9", StringComparison.OrdinalIgnoreCase)) cs = v;
                            }
                        }
                    }
                    // Auch Controller ohne Linkangabe zählen – sonst wäre die Zuordnung nicht mehr eindeutig.
                    list.Add((StorageExpectation.PcieGenFromLinkSpeed(cs), cw is >= 1 and <= 32 ? cw : null));
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException or InvalidOperationException)
        {
            AppLog.Warn("Benchmark", "PCIe-Anbindung der NVMe-SSD nicht lesbar", ex.Message);
            return [];
        }
        return list;
    }
}
