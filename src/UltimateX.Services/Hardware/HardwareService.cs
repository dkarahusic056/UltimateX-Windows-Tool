using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Native;

namespace UltimateX.Services.Hardware;

/// <summary>
/// Hardware- und Systemerkennung über WMI, Registry und Win32 (§16).
/// Grundsatz: Was nicht zuverlässig ermittelt werden kann, wird als "nicht verfügbar" gemeldet –
/// es werden keine Werte geschätzt oder erfunden (§17, §110).
/// Alle Methoden sind blockierend und sollen vom Aufrufer auf einem Hintergrund-Thread laufen.
/// </summary>
public sealed class HardwareService
{
    private const string DisplayClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    public HardwareSnapshot Collect()
    {
        var warnings = new List<string>();
        var snapshot = new HardwareSnapshot(
            Safe(GetCpu, null, warnings, "CPU"),
            Safe(GetGpus, [], warnings, "GPU"),
            Safe(GetMemory, new MemoryInfo(null, null, null, []), warnings, "RAM"),
            Safe(GetBoard, new BoardInfo(null, null, null, null, null, null, "nicht verfügbar", "nicht verfügbar", DataState.NotAvailable, "nicht verfügbar", DataState.NotAvailable), warnings, "Mainboard"),
            Safe(GetPhysicalDisks, [], warnings, "Datenträger"),
            Safe(GetVolumes, [], warnings, "Laufwerke"),
            Safe(GetOs, new OsInfo("Windows", null, Environment.OSVersion.Version.ToString(), "x64", null, Environment.MachineName, null, Elevation.IsElevated), warnings, "Windows"),
            warnings);
        return snapshot;
    }

    private static T Safe<T>(Func<T> f, T fallback, List<string> warnings, string area)
    {
        try { return f(); }
        catch (Exception ex)
        {
            // Bewusst breit: Hardwareerkennung darf auf exotischen Systemen niemals die App beenden (§90).
            AppLog.Warn("HardwareDetection", $"{area}: Erkennung fehlgeschlagen", ex.Message);
            warnings.Add($"{area}: Daten konnten nicht vollständig ermittelt werden.");
            return fallback;
        }
    }

    public CpuInfo? GetCpu()
    {
        var r = Wmi.Query("SELECT Name, Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
        if (!r.Ok || r.Rows.Count == 0) return null;
        var p = r.Rows[0];
        // Mehrere Sockel: Kerne/Threads summieren.
        int? Sum(string key) => r.Rows.All(x => x.U64(key) is not null) ? (int)r.Rows.Sum(x => (long)x.U64(key)!.Value) : null;
        return new CpuInfo(
            p.Str("Name") ?? "Unbekannte CPU",
            p.Str("Manufacturer") ?? "nicht verfügbar",
            Sum("NumberOfCores"),
            Sum("NumberOfLogicalProcessors"),
            (int?)p.U64("MaxClockSpeed"),
            p.U64("L2CacheSize"),
            p.U64("L3CacheSize"),
            p.Str("SocketDesignation"));
    }

    public IReadOnlyList<GpuInfo> GetGpus()
    {
        var r = Wmi.Query("SELECT Name, AdapterCompatibility, AdapterRAM, DriverVersion, DriverDate, CurrentRefreshRate, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
        var list = new List<GpuInfo>();
        foreach (var row in r.Rows)
        {
            var name = row.Str("Name") ?? "Unbekannte GPU";
            // AdapterRAM ist ein UInt32 und bei > 4 GB falsch. Daher bevorzugt der exakte Wert aus der Registry.
            var vram = ReadVramFromRegistry(name);
            if (vram is null && row.U64("AdapterRAM") is { } ram && ram is > 0 and < uint.MaxValue) vram = ram;

            string? res = row.U64("CurrentHorizontalResolution") is { } w && row.U64("CurrentVerticalResolution") is { } h ? $"{w} × {h}" : null;
            list.Add(new GpuInfo(name, row.Str("AdapterCompatibility") ?? "nicht verfügbar", vram,
                row.Str("DriverVersion"), row.Date("DriverDate"), (int?)row.U64("CurrentRefreshRate"), res));
        }
        return list;
    }

    private static ulong? ReadVramFromRegistry(string adapterName)
    {
        using var cls = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
        if (cls is null) return null;
        foreach (var sub in cls.GetSubKeyNames())
        {
            if (sub.Length != 4 || !sub.All(char.IsAsciiDigit)) continue;
            try
            {
                using var k = cls.OpenSubKey(sub);
                if (k is null) continue;
                if (!string.Equals(k.GetValue("DriverDesc") as string, adapterName, StringComparison.OrdinalIgnoreCase)) continue;
                switch (k.GetValue("HardwareInformation.qwMemorySize"))
                {
                    case long l when l > 0: return (ulong)l;
                    case byte[] b when b.Length >= 8: return BitConverter.ToUInt64(b, 0);
                }
                switch (k.GetValue("HardwareInformation.MemorySize"))
                {
                    case int i when i > 0: return (ulong)(uint)i;
                    case byte[] b when b.Length >= 4: return BitConverter.ToUInt32(b, 0);
                }
            }
            catch (System.Security.SecurityException)
            {
                // Einzelne Treiberschlüssel sind teils nicht lesbar – überspringen.
            }
        }
        return null;
    }

    public MemoryInfo GetMemory()
    {
        var status = NativeMethods.GetMemoryStatus();
        var modules = new List<MemoryModule>();
        var r = Wmi.Query("SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
        foreach (var row in r.Rows)
        {
            var speed = row.U64("ConfiguredClockSpeed") is > 0 and var c ? c : row.U64("Speed");
            modules.Add(new MemoryModule(
                row.Str("DeviceLocator") ?? row.Str("BankLabel") ?? "Slot ?",
                row.U64("Capacity"),
                (int?)speed,
                row.Str("Manufacturer"),
                row.Str("PartNumber")));
        }

        int? slots = null;
        var arr = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
        if (arr.Ok && arr.Rows.Count > 0 && arr.Rows.All(x => x.U64("MemoryDevices") is not null))
            slots = (int)arr.Rows.Sum(x => (long)x.U64("MemoryDevices")!.Value);

        return new MemoryInfo(status?.ullTotalPhys, status?.ullAvailPhys, slots, modules);
    }

    public BoardInfo GetBoard()
    {
        var bb = Wmi.Query("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard");
        var bios = Wmi.Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        var b = bb.Rows.FirstOrDefault();
        var bi = bios.Rows.FirstOrDefault();

        var firmware = NativeMethods.GetFirmwareType(out var ft)
            ? ft switch { NativeMethods.FirmwareType.Uefi => "UEFI", NativeMethods.FirmwareType.Bios => "Legacy BIOS", _ => "nicht verfügbar" }
            : "nicht verfügbar";

        var (sbText, sbState) = GetSecureBoot(firmware);
        var (tpmText, tpmState) = GetTpm();

        return new BoardInfo(
            b?.Str("Manufacturer"), b?.Str("Product"), CleanSerial(b?.Str("SerialNumber")),
            bi?.Str("Manufacturer"), bi?.Str("SMBIOSBIOSVersion"), bi?.Date("ReleaseDate"),
            firmware, sbText, sbState, tpmText, tpmState);
    }

    /// <summary>Platzhalter-Seriennummern der Hersteller ("To be filled by O.E.M.", "Default string") werden nicht angezeigt.</summary>
    private static string? CleanSerial(string? s)
    {
        if (s is null) return null;
        string[] placeholders = ["To be filled by O.E.M.", "Default string", "None", "Not Applicable", "System Serial Number", "0"];
        return placeholders.Any(p => s.Equals(p, StringComparison.OrdinalIgnoreCase)) ? null : s;
    }

    public static (string Text, DataState State) GetSecureBoot(string firmware)
    {
        if (firmware == "Legacy BIOS") return ("Nicht unterstützt (Legacy BIOS)", DataState.NotAvailable);
        using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
        return k?.GetValue("UEFISecureBootEnabled") switch
        {
            1 => ("Aktiviert", DataState.Ok),
            0 => ("Deaktiviert", DataState.Warning),
            _ => ("Nicht unterstützt / nicht ermittelbar", DataState.NotAvailable),
        };
    }

    public static (string Text, DataState State) GetTpm()
    {
        try
        {
            var rc = NativeMethods.Tbsi_GetDeviceInfo((uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.TPM_DEVICE_INFO>(), out var info);
            if (rc != 0) return ("Kein TPM gefunden", DataState.Warning);
            return info.tpmVersion switch
            {
                2 => ("TPM 2.0 vorhanden", DataState.Ok),
                1 => ("TPM 1.2 vorhanden", DataState.Warning),
                _ => ("TPM vorhanden (Version unbekannt)", DataState.Ok),
            };
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return ("nicht verfügbar", DataState.NotAvailable);
        }
    }

    public IReadOnlyList<PhysicalDiskInfo> GetPhysicalDisks()
    {
        var list = new List<PhysicalDiskInfo>();
        var r = Wmi.Query("SELECT FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage);
        foreach (var row in r.Rows)
        {
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
            list.Add(new PhysicalDiskInfo(row.Str("FriendlyName") ?? "Datenträger", media, bus, row.U64("Size"), health, state));
        }
        return list;
    }

    public IReadOnlyList<VolumeInfo> GetVolumes()
    {
        var list = new List<VolumeInfo>();
        foreach (var d in DriveInfo.GetDrives())
        {
            if (d.DriveType != DriveType.Fixed) continue;
            try
            {
                if (!d.IsReady) continue;
                list.Add(new VolumeInfo(d.Name.TrimEnd('\\'), d.VolumeLabel, d.DriveFormat, (ulong)d.TotalSize, (ulong)d.AvailableFreeSpace));
            }
            catch (IOException)
            {
                list.Add(new VolumeInfo(d.Name.TrimEnd('\\'), null, null, null, null));
            }
        }
        return list;
    }

    public OsInfo GetOs()
    {
        var r = Wmi.Query("SELECT Caption, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");
        var row = r.Rows.FirstOrDefault();

        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var display = cv?.GetValue("DisplayVersion") as string;
        var ubr = cv?.GetValue("UBR") is int u ? u : (int?)null;
        var build = row?.Str("BuildNumber") ?? Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (ubr is not null) build += "." + ubr.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return new OsInfo(
            row?.Str("Caption") ?? "Microsoft Windows",
            display,
            build,
            row?.Str("OSArchitecture") ?? (Environment.Is64BitOperatingSystem ? "64-Bit" : "32-Bit"),
            row?.Date("InstallDate"),
            Environment.MachineName,
            row?.Date("LastBootUpTime"),
            Elevation.IsElevated);
    }
}
