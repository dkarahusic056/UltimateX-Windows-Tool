using System.Globalization;
using System.Text.Json;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Drivers;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Network;
using UltimateX.Services.Software;
using UltimateX.Services.Startup;

namespace UltimateX.Services.SystemTools;

public sealed record SnapshotFile(string Path, DateTime Created, string Label);

/// <summary>
/// SYSTEM SNAPSHOT (v1.2 §38): Systemstand vor/nach Änderungen (Hardware, Treiber, Programme, Windows, Dienste, Autostart,
/// Netzwerk, Ereignisse, Updates) als JSON im Benutzerprofil. Keine IP-/MAC-Adressen, Seriennummern oder Benutzerdaten.
/// </summary>
public sealed class SnapshotService(HardwareService hardware, DriverService drivers, ProgramService programs, InventoryService inventory,
    StartupService startup, NetworkService network, WindowsHealthService health)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Directory => Path.Combine(AppPaths.UserDataRoot, "snapshots");

    public SystemSnapshotData Capture(string label, string appVersion, IProgress<string>? progress = null)
    {
        var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        void Section(string name, Func<Dictionary<string, string>> collect)
        {
            progress?.Report(name);
            try { sections[name] = collect(); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                AppLog.Warn("Snapshot", $"Abschnitt {name} nicht erfasst", ex.Message);
                sections[name] = new Dictionary<string, string> { ["Fehler"] = ex.Message };
            }
        }

        var hw = hardware.Collect();
        Section("Hardware", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CPU"] = hw.Cpu?.Name ?? "N/A",
                ["RAM gesamt"] = Format.Bytes(hw.Memory.TotalBytes),
                ["Mainboard"] = $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim(),
                ["BIOS"] = $"{hw.Board.BiosVersion} ({Format.DateText(hw.Board.BiosDate)})",
                ["Secure Boot"] = hw.Board.SecureBoot,
                ["TPM"] = hw.Board.Tpm,
            };
            for (var i = 0; i < hw.Gpus.Count; i++) d[$"GPU {i + 1}"] = $"{hw.Gpus[i].Name} · Treiber {hw.Gpus[i].DriverVersion}";
            foreach (var m in hw.Memory.Modules) d[$"RAM {m.Slot}"] = $"{Format.Bytes(m.CapacityBytes)} {m.SpeedMTs} MT/s {m.PartNumber}".Trim();
            foreach (var disk in hw.Disks) d[$"Datenträger {disk.Name}"] = $"{disk.MediaType} {Format.Bytes(disk.SizeBytes)} · {disk.Health}";
            return d;
        });
        Section("Windows", () => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Version"] = $"{hw.Os.Caption} {hw.Os.DisplayVersion}".Trim(),
            ["Build"] = hw.Os.Build,
            ["Architektur"] = hw.Os.Architecture,
        });
        Section("Treiber", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var x in drivers.GetDrivers().Drivers)
                d[$"{x.DeviceName} [{x.InfName}]"] = $"{x.DriverVersion} · {Format.DateText(x.DriverDate)} · {x.Provider}";
            return d;
        });
        Section("Programme", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in programs.GetPrograms()) d[$"{p.Name} ({p.Scope})"] = p.Version ?? "–";
            return d;
        });
        Section("Dienste", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in inventory.GetServices()) d[s.Name] = $"{s.State} · {s.StartMode}";
            return d;
        });
        Section("Autostart", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in startup.GetEntries()) d[$"{e.Name} ({e.SourceText})"] = e.Enabled ? "aktiv" : "deaktiviert";
            return d;
        });
        Section("Netzwerk", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in network.GetAdapters())
                d[a.Name] = $"{a.Kind} · {(a.IsUp ? "verbunden" : "getrennt")} · {(a.LinkSpeedBps is { } l ? Format.BitsPerSecond(l) : "–")} · DHCP {(a.DhcpEnabled ? "an" : "aus")}";
            return d;
        });
        Section("Ereignisse", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var e in health.GetTopProblems(7, 30)) d[$"{e.Log}/{e.Provider}/{e.EventId}"] = $"{e.Count}× ({e.Level})";
            return d;
        });
        Section("Updates", () =>
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in Wmi.Query("SELECT HotFixID, InstalledOn, Description FROM Win32_QuickFixEngineering").Rows)
                if (row.Str("HotFixID") is { } id) d[id] = $"{row.Str("Description")} · {row.Str("InstalledOn")}";
            return d;
        });

        return new SystemSnapshotData(DateTime.Now, label, appVersion, sections);
    }

    public string Save(SystemSnapshotData snapshot)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var path = Path.Combine(Directory, $"snapshot-{snapshot.Created.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Json));
        AppLog.Info("Snapshot", "System-Snapshot gespeichert", $"{snapshot.Label} · {snapshot.Sections.Sum(s => s.Value.Count)} Einträge");
        return path;
    }

    public static IReadOnlyList<SnapshotFile> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var list = new List<SnapshotFile>();
        foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "snapshot-*.json"))
        {
            var s = Load(f);
            if (s is not null) list.Add(new SnapshotFile(f, s.Created, s.Label));
        }
        return list.OrderByDescending(x => x.Created).ToList();
    }

    public static SystemSnapshotData? Load(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            var sections = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
            foreach (var sec in root.GetProperty("Sections").EnumerateObject())
                sections[sec.Name] = sec.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
            return new SystemSnapshotData(root.GetProperty("Created").GetDateTime(), root.GetProperty("Label").GetString() ?? "",
                root.GetProperty("AppVersion").GetString() ?? "", sections);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Warn("Snapshot", "Snapshot nicht lesbar", ex.Message);
            return null;
        }
    }
}
