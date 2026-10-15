using System.Text.Json;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Setup;

/// <summary>Ein Eintrag im Installationsprotokoll (v1.2 §32). Enthält keine Benutzerdaten.</summary>
public sealed record InstallLogEntry(
    DateTime Time,
    string Software,
    string? Version,
    string? Publisher,
    string Source,
    string? DownloadUrl,
    string SignatureStatus,
    string? Sha256,
    int? ExitCode,
    bool Success,
    string Message);

/// <summary>Lokales Protokoll aller Installationen (%LOCALAPPDATA%\UltimateX\install-log.jsonl, JSON Lines).</summary>
public static class InstallLogService
{
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static string LogPath => Path.Combine(AppPaths.UserDataRoot, "install-log.jsonl");

    public static void Add(InstallLogEntry entry)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.UserDataRoot);
                File.AppendAllText(LogPath, JsonSerializer.Serialize(entry, Options) + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("InstallLog", "Installationsprotokoll konnte nicht geschrieben werden", ex.Message);
            }
        }
        AppLog.Info("Setup", $"Installation {(entry.Success ? "erfolgreich" : "fehlgeschlagen")}: {entry.Software}",
            $"Version={entry.Version} Quelle={entry.Source} Signatur={entry.SignatureStatus} Exit={entry.ExitCode}");
    }

    public static IReadOnlyList<InstallLogEntry> Read(int max = 200)
    {
        lock (Gate)
        {
            if (!File.Exists(LogPath)) return [];
            var list = new List<InstallLogEntry>();
            try
            {
                foreach (var line in File.ReadLines(LogPath).Reverse().Take(max))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<InstallLogEntry>(line, Options) is { } e) list.Add(e);
                    }
                    catch (JsonException) { }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn("InstallLog", "Installationsprotokoll nicht lesbar", ex.Message);
            }
            return list;
        }
    }
}
