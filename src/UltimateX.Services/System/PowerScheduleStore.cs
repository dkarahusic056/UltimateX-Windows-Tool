using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Power;

namespace UltimateX.Services.SystemTools;

/// <summary>
/// Speichert die aktuell geplante Energieaktion in %LOCALAPPDATA%\UltimateX\power-schedule.json (höchstens eine Planung).
/// Nur zur Anzeige nach einem Neustart der App – die eigentliche Ausführung von Herunterfahren/Neustart übernimmt Windows (shutdown.exe /t).
/// </summary>
public sealed class PowerScheduleStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly object _gate = new();

    public string FilePath { get; } = filePath ?? Path.Combine(AppPaths.UserDataRoot, "power-schedule.json");

    public PowerScheduleEntry? Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var e = JsonSerializer.Deserialize<PowerScheduleEntry>(File.ReadAllText(FilePath), Json);
                if (e is null || !Enum.IsDefined(e.Action)) return null;
                return e with { DueUtc = DateTime.SpecifyKind(e.DueUtc, DateTimeKind.Utc), CreatedUtc = DateTime.SpecifyKind(e.CreatedUtc, DateTimeKind.Utc) };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                AppLog.Warn("PowerSchedule", "Geplante Energieaktion nicht lesbar", ex.Message);
                return null;
            }
        }
    }

    public void Save(PowerScheduleEntry entry)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(entry, Json));
                File.Move(tmp, FilePath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("PowerSchedule", "Geplante Energieaktion nicht gespeichert", ex.Message);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            try { if (File.Exists(FilePath)) File.Delete(FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("PowerSchedule", "Planung konnte nicht entfernt werden", ex.Message);
            }
        }
    }
}
