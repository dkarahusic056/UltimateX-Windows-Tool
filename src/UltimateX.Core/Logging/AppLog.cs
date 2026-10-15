using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Common;

namespace UltimateX.Core.Logging;

public enum LogCategory
{
    General,
    Security,
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    LogCategory Category,
    string Action,
    string Message,
    string? Details);

/// <summary>
/// Lokales Aktions- und Security-Log als JSON-Lines (§80, §81).
/// Regeln:
///  - Niemals Passwörter, PINs, Tokens oder vollständige sensible Werte loggen.
///  - Logs bleiben lokal; Export nur auf Benutzeraktion.
///  - Aufbewahrung: 30 Tage.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static event Action<LogEntry>? EntryWritten;

    public static void Info(string action, string message, string? details = null)
        => Write(LogLevel.Info, LogCategory.General, action, message, details);

    public static void Warn(string action, string message, string? details = null)
        => Write(LogLevel.Warning, LogCategory.General, action, message, details);

    public static void Error(string action, string message, Exception? ex = null)
        => Write(LogLevel.Error, LogCategory.General, action, message, ex is null ? null : ex.GetType().Name + ": " + ex.Message + Origin(ex));

    /// <summary>Die obersten Aufrufstellen (nur Methodennamen, max. 5 Zeilen) – damit Fehler im lokalen Protokoll auffindbar sind.</summary>
    private static string Origin(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException is not null && inner is AggregateException or System.Reflection.TargetInvocationException) inner = inner.InnerException;
        var frames = (inner.StackTrace ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.StartsWith("at ", StringComparison.Ordinal) ? l[3..] : l)
            .Select(l => l.IndexOf(" in ", StringComparison.Ordinal) is var i and > 0 ? l[..i] : l) // keine Quellpfade
            .Take(5).ToList();
        return frames.Count == 0 ? "" : " @ " + string.Join(" <- ", frames);
    }

    /// <summary>Security-Events: AuthFailure, AdminOperation, DriverInstall, IntegrityFailure, ReportExport …</summary>
    public static void Security(string action, string message, LogLevel level = LogLevel.Info, string? details = null)
        => Write(level, LogCategory.Security, action, message, details);

    private static string FileFor(LogCategory category, DateTimeOffset ts)
        => Path.Combine(AppPaths.LogDirectory, $"{(category == LogCategory.Security ? "security" : "app")}-{ts:yyyyMMdd}.jsonl");

    private static void Write(LogLevel level, LogCategory category, string action, string message, string? details)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, category, action, message, details);
        try
        {
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            lock (Gate)
            {
                File.AppendAllText(FileFor(category, entry.Timestamp), line, Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging darf die Anwendung niemals zum Absturz bringen (§90).
        }
        EntryWritten?.Invoke(entry);
    }

    /// <summary>Liest die Einträge der letzten <paramref name="days"/> Tage (neueste zuerst).</summary>
    public static IReadOnlyList<LogEntry> ReadRecent(int days = 7, LogCategory? category = null)
    {
        var result = new List<LogEntry>();
        var since = DateTimeOffset.Now.Date.AddDays(-days);
        string[] files;
        try { files = Directory.GetFiles(AppPaths.LogDirectory, "*.jsonl"); }
        catch (IOException) { return result; }

        foreach (var file in files)
        {
            if (File.GetLastWriteTime(file) < since) continue;
            string[] lines;
            lock (Gate)
            {
                try { lines = File.ReadAllLines(file, Encoding.UTF8); }
                catch (IOException) { continue; }
            }
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var e = JsonSerializer.Deserialize<LogEntry>(line, JsonOptions);
                    if (e is not null && (category is null || e.Category == category)) result.Add(e);
                }
                catch (JsonException)
                {
                    // Beschädigte Zeile überspringen (manipulierte/abgeschnittene Logs dürfen nicht crashen).
                }
            }
        }
        return result.OrderByDescending(e => e.Timestamp).ToList();
    }

    /// <summary>Löscht Logs älter als 30 Tage. Nur innerhalb des eigenen Log-Ordners.</summary>
    public static void ApplyRetention(int days = 30)
    {
        try
        {
            foreach (var file in Directory.GetFiles(AppPaths.LogDirectory, "*.jsonl"))
            {
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-days)) File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static string ExportAsText(IEnumerable<LogEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.Append(e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(e.Level).Append("] [")
              .Append(e.Category).Append("] ").Append(e.Action).Append(": ").Append(e.Message);
            if (!string.IsNullOrEmpty(e.Details)) sb.Append(" | ").Append(e.Details);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string ExportAsJson(IEnumerable<LogEntry> entries)
        => JsonSerializer.Serialize(entries, new JsonSerializerOptions(JsonOptions) { WriteIndented = true });
}
