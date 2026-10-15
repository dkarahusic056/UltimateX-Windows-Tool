using System.Globalization;

namespace UltimateX.Core.Backup;

/// <summary>Verhalten, wenn am Wiederherstellungsziel bereits eine Datei existiert. Standard: vorhandene behalten (nie stumm überschreiben).</summary>
public enum RestoreConflictMode
{
    /// <summary>Vorhandene Datei behalten, Sicherung nicht wiederherstellen.</summary>
    KeepExisting,
    /// <summary>Vorhandene Datei in den Papierkorb verschieben und durch die Sicherung ersetzen.</summary>
    Replace,
    /// <summary>Beide behalten: Sicherung unter neuem Namen „Datei (wiederhergestellt …).ext“ ablegen.</summary>
    KeepBoth,
}

public enum RestoreAction
{
    Write,
    Skip,
    ReplaceExisting,
    WriteAsCopy,
}

public sealed record RestoreDecision(RestoreAction Action, string DestinationPath);

/// <summary>Konfliktentscheidung bei der Wiederherstellung (ohne Dateisystemzugriff, testbar).</summary>
public static class RestoreConflicts
{
    public static RestoreDecision Decide(string destination, RestoreConflictMode mode, Func<string, bool> exists, DateTime stampLocal)
    {
        if (!exists(destination)) return new(RestoreAction.Write, destination);
        return mode switch
        {
            RestoreConflictMode.Replace => new(RestoreAction.ReplaceExisting, destination),
            RestoreConflictMode.KeepBoth => new(RestoreAction.WriteAsCopy, AlternateName(destination, stampLocal, exists)),
            _ => new(RestoreAction.Skip, destination),
        };
    }

    /// <summary>"Bericht.docx" → "Bericht (wiederhergestellt 2026-10-14 153000).docx", bei Bedarf mit Zähler.</summary>
    public static string AlternateName(string path, DateTime stampLocal, Func<string, bool> exists)
    {
        var dir = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        var stamp = stampLocal.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture);
        var candidate = Path.Combine(dir, $"{name} (wiederhergestellt {stamp}){ext}");
        for (var i = 2; exists(candidate) && i < 10_000; i++)
            candidate = Path.Combine(dir, $"{name} (wiederhergestellt {stamp} {i.ToString(CultureInfo.InvariantCulture)}){ext}");
        return candidate;
    }
}
