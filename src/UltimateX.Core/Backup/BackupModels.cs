using System.Text.Json.Serialization;

namespace UltimateX.Core.Backup;

/// <summary>Zeitplan eines Datei-Backup-Profils. Zeitpläne laufen nur, solange ULTIMATE X geöffnet ist (auch minimiert/Infobereich).</summary>
public enum BackupScheduleKind
{
    /// <summary>Nur manuell.</summary>
    Manual,
    Daily,
    Weekly,
    /// <summary>Sobald die Zielplatte angesteckt wird (Erkennung über die Markierungsdatei, nicht nur am Laufwerksbuchstaben).</summary>
    OnTargetConnected,
}

/// <summary>Wie unveränderte Dateien erkannt werden.</summary>
public enum BackupCompareMode
{
    /// <summary>Größe + Änderungszeit (schnell, Standard).</summary>
    SizeAndTime,
    /// <summary>Zusätzlich SHA-256 der Quelle (liest jede Datei; langsam, aber erkennt Änderungen ohne neue Änderungszeit).</summary>
    SizeTimeAndHash,
}

/// <summary>Eine Quelle: Ordner (rekursiv) oder einzelne Datei (z. B. Browser-Lesezeichen).</summary>
public sealed class BackupSource
{
    public string Path { get; set; } = string.Empty;
    public bool IsFile { get; set; }
    /// <summary>ID der Vorlage, aus der die Quelle stammt (null = manuell hinzugefügt).</summary>
    public string? Template { get; set; }
}

public sealed class BackupScheduleSettings
{
    public BackupScheduleKind Kind { get; set; } = BackupScheduleKind.Manual;
    /// <summary>Uhrzeit (lokal) für täglich/wöchentlich.</summary>
    public TimeSpan TimeOfDay { get; set; } = new(19, 0, 0);
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Sunday;
}

/// <summary>Backup-Profil (gespeichert in %LOCALAPPDATA%\UltimateX\backup-profiles.json).</summary>
public sealed class BackupProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Meine Dateien";
    /// <summary>Fester Ordnername unter UltimateX-Backup (beim Anlegen aus dem Namen gebildet, ändert sich bei Umbenennung nicht).</summary>
    public string FolderName { get; set; } = string.Empty;
    /// <summary>Angehakte Vorlagen (IDs aus <see cref="BackupTemplates"/>) – werden bei jedem Lauf neu aufgelöst (z. B. neues Browserprofil).</summary>
    public List<string> Templates { get; set; } = [];
    /// <summary>Zusätzlich gewählte Ordner/Dateien.</summary>
    public List<BackupSource> Sources { get; set; } = [];
    public List<string> Exclusions { get; set; } = [];
    /// <summary>Zielordner (Laufwerkswurzel oder Ordner), z. B. "E:\" – darunter entsteht "UltimateX-Backup\&lt;Profil&gt;".</summary>
    public string TargetPath { get; set; } = string.Empty;
    /// <summary>ID aus UltimateX-Backup\target.json – erkennt die Zielplatte auch unter anderem Laufwerksbuchstaben.</summary>
    public string? TargetId { get; set; }
    public BackupScheduleSettings Schedule { get; set; } = new();
    /// <summary>Anzahl aufbewahrter vollständiger Versionen (1–365).</summary>
    public int KeepVersions { get; set; } = 10;
    public BackupCompareMode CompareMode { get; set; } = BackupCompareMode.SizeAndTime;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAttemptUtc { get; set; }
    public DateTime? LastSuccessUtc { get; set; }
    public string? LastResult { get; set; }

    public const int MinKeep = 1;
    public const int MaxKeep = 365;
}

/// <summary>Globale Backup-Einstellungen.</summary>
public sealed class BackupSettings
{
    /// <summary>SHA-256 jeder kopierten Datei im Manifest speichern.</summary>
    public bool StoreHashes { get; set; } = true;
    /// <summary>Warnung, wenn die letzte erfolgreiche Sicherung eines Profils älter ist (Tage).</summary>
    public int OverdueDays { get; set; } = 7;
    /// <summary>Stichprobe für die Hash-Prüfung nach dem Backup (Anzahl Dateien).</summary>
    public int VerifySampleSize { get; set; } = 20;
}

/// <summary>Inhalt von backup-profiles.json.</summary>
public sealed class BackupStoreData
{
    public int Version { get; set; } = 1;
    public BackupSettings Settings { get; set; } = new();
    public List<BackupProfile> Profiles { get; set; } = [];
}

/// <summary>Markierungsdatei UltimateX-Backup\target.json auf der Zielplatte.</summary>
public sealed class BackupTargetMarker
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public string Computer { get; set; } = string.Empty;
    public string Note { get; set; } = "ULTIMATE X Backup-Ziel. Bitte nicht löschen – erkennt diese Platte auch unter anderem Laufwerksbuchstaben.";
}

public enum BackupVersionStatus
{
    Running,
    Completed,
    CompletedWithWarnings,
    Cancelled,
    Failed,
}

/// <summary>Ein Eintrag im Manifest einer Version.</summary>
public sealed class BackupManifestEntry
{
    /// <summary>Pfad relativ zum Versionsordner, z. B. "C\Users\denni\Documents\a.txt".</summary>
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public string? Sha256 { get; set; }
    /// <summary>true = per Hardlink aus der vorherigen Version übernommen (unverändert).</summary>
    public bool Linked { get; set; }
}

public sealed class BackupSkippedEntry
{
    public string Path { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Manifest (JSON) je Version: ultimatex-manifest.json im Versionsordner.</summary>
public sealed class BackupManifest
{
    public int FormatVersion { get; set; } = 1;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string VersionName { get; set; } = string.Empty;
    public string Computer { get; set; } = string.Empty;
    public DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public BackupVersionStatus Status { get; set; } = BackupVersionStatus.Running;
    public string? BaseVersion { get; set; }
    public List<string> Sources { get; set; } = [];
    public List<string> Exclusions { get; set; } = [];
    public List<BackupManifestEntry> Files { get; set; } = [];
    public List<BackupSkippedEntry> Skipped { get; set; } = [];
    public long TotalBytes { get; set; }
    public long CopiedBytes { get; set; }
    public int LinkedFiles { get; set; }
    public int CopiedFiles { get; set; }
    public string? VerifyResult { get; set; }

    [JsonIgnore]
    public bool IsComplete => Status is BackupVersionStatus.Completed or BackupVersionStatus.CompletedWithWarnings;
}

/// <summary>Fortschritt eines Datei-Backups.</summary>
public sealed record BackupProgress(string Phase, int FilesDone, int FilesTotal, long BytesDone, long BytesTotal, string? CurrentPath)
{
    public double Percent => BytesTotal > 0 ? Math.Min(100, BytesDone * 100.0 / BytesTotal)
        : FilesTotal > 0 ? Math.Min(100, FilesDone * 100.0 / FilesTotal) : 0;
}

/// <summary>Eintrag im Backup-Verlauf (backup-history.json).</summary>
public sealed class BackupHistoryEntry
{
    public DateTime TimeUtc { get; set; }
    /// <summary>"Datei-Backup", "System-Abbild", "Treiber".</summary>
    public string Kind { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>Profil mit überfälliger Sicherung (für die Tray-Überwachung).</summary>
public sealed record BackupOverdueInfo(string ProfileId, string ProfileName, DateTime? LastSuccessUtc, int? DaysSinceSuccess);
