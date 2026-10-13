using System.Text.Json;
using UltimateX.Core.Common;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Hardware;

public sealed record SmartDisk(
    string Name,
    string Health,
    DataState HealthState,
    int? TemperatureC,
    int? TemperatureMaxC,
    long? PowerOnHours,
    int? WearPercent,
    long? ReadErrorsUncorrected,
    long? WriteErrorsUncorrected,
    long? ReadErrorsTotal,
    long? StartStopCycles)
{
    /// <summary>Verbleibende Lebensdauer laut Laufwerk (100 − Wear), nur wenn gemeldet.</summary>
    public int? RemainingLifePercent => WearPercent is { } w ? Math.Clamp(100 - w, 0, 100) : null;
}

public sealed record SmartSnapshot(DateTime Time, IReadOnlyList<SmartDisk> Disks);

/// <summary>
/// SMART / Laufwerkszuverlässigkeit (§44) über MSFT_StorageReliabilityCounter.
/// Die Abfrage erfordert Adminrechte und läuft daher im Admin-Helper (UAC).
/// Es werden nur Werte angezeigt, die das Laufwerk tatsächlich meldet – keine Prognosen (§107).
/// Das letzte Ergebnis wird lokal zwischengespeichert, damit Dashboard/Temperaturen ohne erneute UAC-Abfrage Werte zeigen.
/// </summary>
public sealed class SmartService
{
    private static string CacheFile => Path.Combine(AppPaths.UserDataRoot, "smart-cache.json");

    public async Task<(SmartSnapshot? Snapshot, string? Error)> ReadAsync(CancellationToken ct = default)
    {
        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.ReadSmart, null, null, ct).ConfigureAwait(false);
        if (r.Cancelled) return (null, "Die Administratorfreigabe wurde abgelehnt.");
        var line = r.Output.Split('\n').FirstOrDefault(l => l.StartsWith("##RESULT##", StringComparison.Ordinal));
        if (line is null) return (null, "Keine SMART-Daten erhalten. " + r.Output.Trim());

        try
        {
            using var doc = JsonDocument.Parse(line["##RESULT##".Length..]);
            var disks = new List<SmartDisk>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var (health, state) = Int(e, "HealthStatus") switch
                {
                    0 => ("Fehlerfrei", DataState.Ok),
                    1 => ("Warnung", DataState.Warning),
                    2 => ("Fehlerhaft", DataState.Problem),
                    _ => ("Unbekannt", DataState.NotAvailable),
                };
                disks.Add(new SmartDisk(
                    Str(e, "FriendlyName") ?? "Datenträger", health, state,
                    (int?)Int(e, "Temperature") is { } t and > 0 ? t : null,
                    (int?)Int(e, "TemperatureMax") is { } tm and > 0 ? tm : null,
                    Int(e, "PowerOnHours"),
                    (int?)Int(e, "Wear"),
                    Int(e, "ReadErrorsUncorrected"),
                    Int(e, "WriteErrorsUncorrected"),
                    Int(e, "ReadErrorsTotal"),
                    Int(e, "StartStopCycleCount")));
            }
            var snap = new SmartSnapshot(DateTime.Now, disks);
            try { File.WriteAllText(CacheFile, JsonSerializer.Serialize(snap)); } catch (IOException) { }
            lock (CacheGate) { _cached = snap; _cachedAt = DateTime.UtcNow; }
            return (snap, null);
        }
        catch (JsonException ex)
        {
            return (null, "SMART-Daten konnten nicht gelesen werden: " + ex.Message);
        }
    }

    private static readonly object CacheGate = new();
    private static SmartSnapshot? _cached;
    private static DateTime _cachedAt = DateTime.MinValue;

    /// <summary>Letztes gespeichertes Ergebnis (oder null). Liest die Datei höchstens einmal pro Minute (§89).</summary>
    public static SmartSnapshot? LoadCached()
    {
        lock (CacheGate)
        {
            if (DateTime.UtcNow - _cachedAt < TimeSpan.FromMinutes(1)) return _cached;
            _cachedAt = DateTime.UtcNow;
            try
            {
                _cached = File.Exists(CacheFile) ? JsonSerializer.Deserialize<SmartSnapshot>(File.ReadAllText(CacheFile)) : null;
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                _cached = null;
            }
            return _cached;
        }
    }

    private static long? Int(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : null;

    private static string? Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
}
