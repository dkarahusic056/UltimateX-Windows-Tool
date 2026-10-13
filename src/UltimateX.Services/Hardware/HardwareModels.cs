using UltimateX.Core.Common;

namespace UltimateX.Services.Hardware;

public sealed record CpuInfo(
    string Name, string Manufacturer, int? Cores, int? Threads, int? MaxClockMHz, ulong? L2CacheKB, ulong? L3CacheKB, string? Socket);

public sealed record GpuInfo(string Name, string Vendor, ulong? VramBytes, string? DriverVersion, DateTime? DriverDate,
    int? CurrentRefreshRate, string? CurrentResolution);

public sealed record MemoryModule(string Slot, ulong? CapacityBytes, int? SpeedMTs, string? Manufacturer, string? PartNumber);

public sealed record MemoryInfo(ulong? TotalBytes, ulong? AvailableBytes, int? SlotCount, IReadOnlyList<MemoryModule> Modules)
{
    public ulong? UsedBytes => TotalBytes is { } t && AvailableBytes is { } a ? t - a : null;
}

public sealed record BoardInfo(
    string? Manufacturer, string? Product, string? SerialNumber,
    string? BiosVendor, string? BiosVersion, DateTime? BiosDate,
    string Firmware, string SecureBoot, DataState SecureBootState, string Tpm, DataState TpmState);

public sealed record PhysicalDiskInfo(string Name, string MediaType, string BusType, ulong? SizeBytes, string Health, DataState HealthState);

public sealed record VolumeInfo(string Letter, string? Label, string? FileSystem, ulong? SizeBytes, ulong? FreeBytes)
{
    public ulong? UsedBytes => SizeBytes is { } s && FreeBytes is { } f ? s - f : null;
    public double? UsedPercent => SizeBytes is > 0 && UsedBytes is { } u ? 100.0 * u / SizeBytes.Value : null;
}

public sealed record OsInfo(
    string Caption, string? DisplayVersion, string Build, string Architecture,
    DateTime? InstallDate, string ComputerName, DateTime? LastBoot, bool IsElevated)
{
    public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);
}

public sealed record HardwareSnapshot(
    CpuInfo? Cpu,
    IReadOnlyList<GpuInfo> Gpus,
    MemoryInfo Memory,
    BoardInfo Board,
    IReadOnlyList<PhysicalDiskInfo> Disks,
    IReadOnlyList<VolumeInfo> Volumes,
    OsInfo Os,
    IReadOnlyList<string> Warnings);
