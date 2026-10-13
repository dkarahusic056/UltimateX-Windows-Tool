using System.Text;
using System.Text.Json;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Network;
using UltimateX.Services.Security;
using UltimateX.Services.Startup;

namespace UltimateX.Services.Reports;

/// <summary>
/// System Report (§82). Sensible Daten (Seriennummern, MAC, IP, Hardware-IDs, Computername) werden
/// standardmäßig NICHT exportiert; nur auf ausdrücklichen Wunsch nach Authentifizierung.
/// </summary>
public static class SystemReport
{
    public sealed record Input(
        HardwareSnapshot? Hardware,
        IReadOnlyList<AdapterInfo>? Adapters,
        IReadOnlyList<SecurityItem>? Security,
        IReadOnlyList<StartupEntry>? Startup,
        RebootStatus? Reboot,
        IReadOnlyList<ServiceStatus>? Services,
        CrashSummary? Crashes,
        HealthScoreResult? Score,
        int? DriverCount);

    public static Dictionary<string, object?> Build(Input i, bool includeSensitive)
    {
        string S(string? v) => v is null ? "nicht verfügbar" : includeSensitive ? v : SensitiveMasker.MaskGeneric(v);

        var report = new Dictionary<string, object?>
        {
            ["Erstellt"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["Produkt"] = "ULTIMATE X Windows Control",
            ["SensibleDatenEnthalten"] = includeSensitive,
        };

        if (i.Hardware is { } hw)
        {
            report["Windows"] = new Dictionary<string, object?>
            {
                ["Edition"] = hw.Os.Caption,
                ["Version"] = hw.Os.DisplayVersion,
                ["Build"] = hw.Os.Build,
                ["Architektur"] = hw.Os.Architecture,
                ["Installiert"] = Format.DateText(hw.Os.InstallDate),
                ["Computername"] = includeSensitive ? hw.Os.ComputerName : "(ausgeblendet)",
                ["Uptime"] = Format.Duration(hw.Os.Uptime),
            };
            report["CPU"] = hw.Cpu is null ? null : new Dictionary<string, object?>
            {
                ["Modell"] = hw.Cpu.Name, ["Kerne"] = hw.Cpu.Cores, ["Threads"] = hw.Cpu.Threads, ["MaxTaktMHz"] = hw.Cpu.MaxClockMHz,
            };
            report["GPU"] = hw.Gpus.Select(g => new Dictionary<string, object?>
            {
                ["Modell"] = g.Name, ["VRAM"] = Format.Bytes(g.VramBytes), ["Treiber"] = g.DriverVersion, ["Treiberdatum"] = Format.DateText(g.DriverDate),
            }).ToList();
            report["RAM"] = new Dictionary<string, object?>
            {
                ["Gesamt"] = Format.Bytes(hw.Memory.TotalBytes),
                ["Slots"] = hw.Memory.SlotCount,
                ["Module"] = hw.Memory.Modules.Select(m => $"{m.Slot}: {Format.Bytes(m.CapacityBytes)} @ {m.SpeedMTs} MT/s {m.Manufacturer} {m.PartNumber}".Trim()).ToList(),
            };
            report["Mainboard"] = new Dictionary<string, object?>
            {
                ["Hersteller"] = hw.Board.Manufacturer, ["Modell"] = hw.Board.Product, ["Seriennummer"] = S(hw.Board.SerialNumber),
                ["BIOS"] = $"{hw.Board.BiosVendor} {hw.Board.BiosVersion} ({Format.DateText(hw.Board.BiosDate)})",
                ["Firmware"] = hw.Board.Firmware, ["SecureBoot"] = hw.Board.SecureBoot, ["TPM"] = hw.Board.Tpm,
            };
            report["Datenträger"] = hw.Disks.Select(d => $"{d.Name} ({d.MediaType}, {d.BusType}, {Format.Bytes(d.SizeBytes)}) – {d.Health}").ToList();
            report["Laufwerke"] = hw.Volumes.Select(v => $"{v.Letter} {v.FileSystem}: {Format.Bytes(v.UsedBytes)} von {Format.Bytes(v.SizeBytes)} belegt").ToList();
        }

        if (i.Adapters is not null)
        {
            report["Netzwerk"] = i.Adapters.Select(a => new Dictionary<string, object?>
            {
                ["Name"] = a.Name, ["Typ"] = a.Kind, ["Status"] = a.IsUp ? "Verbunden" : "Getrennt",
                ["Geschwindigkeit"] = a.LinkSpeedBps is { } s ? Format.BitsPerSecond(s) : null,
                ["MAC"] = a.MacAddress is null ? null : includeSensitive ? a.MacAddress : SensitiveMasker.MaskMac(a.MacAddress),
                ["IPv4"] = includeSensitive ? a.IPv4 : a.IPv4.Select(SensitiveMasker.MaskIp).ToList(),
            }).ToList();
        }

        if (i.DriverCount is not null) report["Treiber"] = $"{i.DriverCount} Gerätetreiber inventarisiert";
        if (i.Security is not null) report["Sicherheit"] = i.Security.Select(s => $"{s.Area}: {s.Status}").ToList();
        if (i.Startup is not null) report["Autostart"] = i.Startup.Select(s => $"{(s.Enabled ? "[an] " : "[aus]")} {s.Name} ({s.SourceText})").ToList();
        if (i.Reboot is not null) report["Neustart"] = i.Reboot.RebootRequired ? string.Join("; ", i.Reboot.Reasons) : "Nicht erforderlich";
        if (i.Services is not null) report["Dienste"] = i.Services.Select(s => $"{s.DisplayName}: {s.State} ({s.StartMode})").ToList();
        if (i.Crashes is not null)
            report["Stabilität (30 Tage)"] = $"{i.Crashes.BugChecks} Bluescreens, {i.Crashes.UnexpectedShutdowns} unerwartete Neustarts, {i.Crashes.AppCrashes} Programmabstürze";
        if (i.Score is not null)
            report["PC Health Score"] = new Dictionary<string, object?>
            {
                ["Score"] = i.Score.Score, ["Bewertung"] = i.Score.Rating,
                ["Abzüge"] = i.Score.Factors.Select(f => $"-{f.Deduction}: {f.Name} ({f.Reason})").ToList(),
            };
        return report;
    }

    public static string ToJson(Dictionary<string, object?> report)
        => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    public static string ToText(Dictionary<string, object?> report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ULTIMATE X Windows Control – Systembericht");
        sb.AppendLine(new string('=', 48));
        foreach (var (key, value) in report) Append(sb, key, value, 0);
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string key, object? value, int indent)
    {
        var pad = new string(' ', indent * 2);
        switch (value)
        {
            case Dictionary<string, object?> d:
                sb.AppendLine().Append(pad).AppendLine($"[{key}]");
                foreach (var (k, v) in d) Append(sb, k, v, indent + 1);
                break;
            case System.Collections.IEnumerable list when value is not string:
                sb.AppendLine().Append(pad).AppendLine($"[{key}]");
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object?> dd)
                    {
                        foreach (var (k, v) in dd) Append(sb, k, v, indent + 1);
                        sb.AppendLine();
                    }
                    else sb.Append(pad).Append("  - ").AppendLine(item?.ToString());
                }
                break;
            default:
                sb.Append(pad).Append(key).Append(": ").AppendLine(value?.ToString() ?? "nicht verfügbar");
                break;
        }
    }

    public static void Save(string path, string content, bool includeSensitive)
    {
        File.WriteAllText(path, content, new UTF8Encoding(true));
        AppLog.Security("ReportExport", "Systembericht exportiert", details: $"{Path.GetFileName(path)}, sensibel={includeSensitive}");
    }
}
