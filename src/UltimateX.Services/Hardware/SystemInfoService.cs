using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;

namespace UltimateX.Services.Hardware;

public sealed record PcieLink(int? CurrentWidth, int? MaxWidth, int? CurrentGen, int? MaxGen);

public sealed record PowerPlanInfo(Guid Id, string Name)
{
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public bool IsSaver => Id == PowerSaver;
}

/// <summary>BIOS/UEFI-, Virtualisierungs- und Sicherheitsstatus (v1.2 §35) – nur lesend.</summary>
public sealed record FirmwareInfo(
    string? BiosVendor,
    string? BiosVersion,
    DateTime? BiosDate,
    string FirmwareType,
    string SecureBoot,
    DataState SecureBootState,
    string Tpm,
    DataState TpmState,
    bool? VirtualizationFirmwareEnabled,
    bool? HypervisorPresent,
    int? VbsStatus,
    bool? MemoryIntegrityEnabled,
    bool? MemoryIntegrityRunning,
    bool? HyperVFeatureEnabled);

/// <summary>
/// Zusätzliche System-Abfragen für v1.2 (RAM-Module, PCIe-Anbindung, Energieprofil, Spieleeinstellungen, BIOS/UEFI).
/// Alles nur lesend und ohne Adminrechte; nicht ermittelbare Werte → null.
/// </summary>
public sealed class SystemInfoService
{
    // ------------------------------------------------------------------ RAM

    public IReadOnlyList<RamModuleData> GetRamModules()
    {
        var r = Wmi.Query("SELECT DeviceLocator, BankLabel, Capacity, Speed, ConfiguredClockSpeed, Manufacturer, PartNumber, SerialNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        return r.Rows.Select(row => new RamModuleData(
            row.Str("DeviceLocator") ?? "Slot ?",
            row.Str("BankLabel"),
            row.U64("Capacity"),
            row.U64("Speed") is { } s and > 0 ? (int)s : null,
            row.U64("ConfiguredClockSpeed") is { } c and > 0 ? (int)c : null,
            row.Str("Manufacturer"),
            row.Str("PartNumber"),
            row.Str("SerialNumber"),
            row.U64("SMBIOSMemoryType") is { } t ? (int)t : null)).ToList();
    }

    /// <summary>Anzahl der RAM-Steckplätze laut SMBIOS.</summary>
    public int? GetMemorySlotCount()
    {
        var arr = Wmi.Query("SELECT MemoryDevices FROM Win32_PhysicalMemoryArray");
        return arr.Ok && arr.Rows.Count > 0 && arr.Rows.All(x => x.U64("MemoryDevices") is not null)
            ? (int)arr.Rows.Sum(x => (long)x.U64("MemoryDevices")!.Value) : null;
    }

    // ------------------------------------------------------------------ Datenträger

    /// <summary>Ist das Windows-Systemlaufwerk eine SSD? (MSFT_Partition → MSFT_PhysicalDisk). Null = nicht ermittelbar.</summary>
    public bool? SystemDriveIsSsd()
    {
        var sysLetter = char.ToUpperInvariant((Path.GetPathRoot(Environment.SystemDirectory) ?? "C")[0]);
        var parts = Wmi.Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", Wmi.Storage);
        var disk = parts.Rows.FirstOrDefault(p => p["DriveLetter"]?.ToString() is { Length: > 0 } l && char.ToUpperInvariant(l[0]) == sysLetter);
        if (disk?.U64("DiskNumber") is not { } number) return null;
        var disks = Wmi.Query("SELECT DeviceId, MediaType, BusType FROM MSFT_PhysicalDisk", Wmi.Storage);
        var d = disks.Rows.FirstOrDefault(x => x.Str("DeviceId") == number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (d is null) return null;
        if (d.U64("BusType") == 17) return true; // NVMe
        return d.U64("MediaType") switch { 4 or 5 => true, 3 => false, _ => null };
    }

    // ------------------------------------------------------------------ GPU / PCIe

    /// <summary>
    /// PCIe-Anbindung der (ersten) Grafikkarte über die Geräteeigenschaften (DEVPKEY_PciDevice_*).
    /// Hinweis: Die aktuelle PCIe-Generation sinkt im Leerlauf zum Stromsparen – aussagekräftig ist vor allem die Lane-Anzahl.
    /// </summary>
    public PcieLink? GetGpuPcieLink(string? gpuName = null)
    {
        try
        {
            // SELECT * – nur vollständige Instanzen haben einen __PATH; InvokeMethod schlägt bei Teilprojektionen fehl.
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE PNPClass = 'Display'");
            using var results = searcher.Get();
            foreach (ManagementObject dev in results)
            {
                using (dev)
                {
                    var name = dev["Name"]?.ToString() ?? "";
                    var pnp = dev["PNPDeviceID"]?.ToString() ?? "";
                    if (!pnp.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (gpuName is not null && !name.Equals(gpuName, StringComparison.OrdinalIgnoreCase)) continue;
                    var outParams = dev.InvokeMethod("GetDeviceProperties", null, null);
                    if (outParams?["deviceProperties"] is not ManagementBaseObject[] props) continue;
                    int? cw = null, mw = null, cs = null, ms = null;
                    foreach (var p in props)
                    {
                        using (p)
                        {
                            var key = p["KeyName"]?.ToString() ?? "";
                            var data = p["Data"];
                            int? v = data switch { uint u => (int)u, int i => i, ushort us => us, byte b => b, _ => null };
                            if (v is null) continue;
                            // Kanonische Namen oder {3AB22E31-8264-4B4E-9AF5-A8D2D8E33E62} 9/10/11/12
                            if (key.Contains("CurrentLinkWidth", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 11", StringComparison.OrdinalIgnoreCase)) cw = v;
                            else if (key.Contains("MaxLinkWidth", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 12", StringComparison.OrdinalIgnoreCase)) mw = v;
                            else if (key.Contains("CurrentLinkSpeed", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 9", StringComparison.OrdinalIgnoreCase)) cs = v;
                            else if (key.Contains("MaxLinkSpeed", StringComparison.OrdinalIgnoreCase) || key.EndsWith("E62} 10", StringComparison.OrdinalIgnoreCase)) ms = v;
                        }
                    }
                    if (cw is null && mw is null) continue;
                    return new PcieLink(cw, mw, cs, ms);
                }
            }
        }
        catch (Exception ex) when (ex is ManagementException or COMException or UnauthorizedAccessException or InvalidCastException or InvalidOperationException)
        {
            AppLog.Warn("SystemInfo", "PCIe-Anbindung nicht lesbar", ex.Message);
        }
        return null;
    }

    // ------------------------------------------------------------------ Energieprofil

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr userRootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr rootPowerKey, ref Guid schemeGuid, IntPtr subGroup, IntPtr setting, IntPtr buffer, ref uint bufferSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    public PowerPlanInfo? GetActivePowerPlan()
    {
        try
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0 || ptr == IntPtr.Zero) return null;
            Guid id;
            try { id = Marshal.PtrToStructure<Guid>(ptr); }
            finally { LocalFree(ptr); }

            uint size = 0;
            PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size);
            var name = id == PowerPlanInfo.PowerSaver ? "Energiespar-Profil" : id == PowerPlanInfo.HighPerformance ? "Höchstleistung" : id == PowerPlanInfo.Balanced ? "Ausbalanciert" : "Benutzerdefiniert";
            if (size is > 0 and < 4096)
            {
                var buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buf, ref size) == 0)
                        name = Marshal.PtrToStringUni(buf) ?? name;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            return new PowerPlanInfo(id, name);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ Spieleeinstellungen

    /// <summary>Windows-Spielmodus (HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled; fehlt der Wert, ist er standardmäßig an).</summary>
    public bool? GameModeEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            return k?.GetValue("AutoGameModeEnabled") switch { int v => v != 0, _ => true };
        }
        catch (System.Security.SecurityException) { return null; }
    }

    /// <summary>Hardwarebeschleunigte GPU-Planung (HwSchMode: 2 = an, 1 = aus). Fehlt der Wert → null.</summary>
    public bool? HardwareGpuSchedulingEnabled()
    {
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            return k?.GetValue("HwSchMode") switch { int v => v == 2, _ => null };
        }
        catch (System.Security.SecurityException) { return null; }
    }

    /// <summary>Prozesse mit hoher CPU-Last (Messung über ~1,5 s). Eigene App und Leerlaufprozess ausgenommen.</summary>
    public IReadOnlyList<string> GetHeavyProcesses(double thresholdPercent = 10)
    {
        var ps = new SystemTools.ProcessService();
        ps.Snapshot(false);
        Thread.Sleep(1500);
        var self = Environment.ProcessId;
        return ps.Snapshot(false)
            .Where(p => p.Pid != self && p.Pid > 4 && p.CpuPercent >= thresholdPercent
                        && !p.Name.Equals("Idle", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.CpuPercent)
            .Select(p => $"{p.Name} ({p.CpuPercent:0} %)")
            .Take(8).ToList();
    }

    // ------------------------------------------------------------------ BIOS / UEFI

    public FirmwareInfo GetFirmwareInfo(HardwareService hardware)
    {
        var board = hardware.GetBoard();

        bool? virt = null;
        var cpu = Wmi.Query("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor");
        if (cpu.Rows.Count > 0) virt = cpu.Rows[0].Bool("VirtualizationFirmwareEnabled");

        bool? hypervisor = null;
        var cs = Wmi.Query("SELECT HypervisorPresent FROM Win32_ComputerSystem");
        if (cs.Rows.Count > 0) hypervisor = cs.Rows[0].Bool("HypervisorPresent");

        int? vbs = null;
        bool? hvciRunning = null;
        var dg = Wmi.Query("SELECT VirtualizationBasedSecurityStatus, SecurityServicesRunning FROM Win32_DeviceGuard", @"root\Microsoft\Windows\DeviceGuard");
        if (dg.Rows.Count > 0)
        {
            vbs = dg.Rows[0].U64("VirtualizationBasedSecurityStatus") is { } v ? (int)v : null;
            if (dg.Rows[0]["SecurityServicesRunning"] is Array running)
                hvciRunning = running.Cast<object>().Any(o => Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture) == 2);
        }

        bool? hvciEnabled = null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
            hvciEnabled = k?.GetValue("Enabled") switch { int e => e != 0, _ => false };
        }
        catch (System.Security.SecurityException) { }

        bool? hyperV = null;
        var feature = Wmi.Query("SELECT InstallState FROM Win32_OptionalFeature WHERE Name = 'Microsoft-Hyper-V-Hypervisor'");
        if (feature.Ok) hyperV = feature.Rows.Count > 0 && feature.Rows[0].U64("InstallState") == 1;

        return new FirmwareInfo(board.BiosVendor, board.BiosVersion, board.BiosDate, board.Firmware, board.SecureBoot, board.SecureBootState,
            board.Tpm, board.TpmState, virt, hypervisor, vbs, hvciEnabled, hvciRunning, hyperV);
    }
}
