using System.Text.Json;
using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Services.Startup;

public enum StartupSource { RegistryUser, RegistryMachine64, RegistryMachine32, FolderUser, FolderCommon }

public sealed record StartupEntry(
    string Name,
    string Command,
    string? ExecutablePath,
    StartupSource Source,
    bool Enabled,
    bool CanToggle,
    bool IsSystemRelevant,
    SignatureInfo? Signature)
{
    public string SourceText => Source switch
    {
        StartupSource.RegistryUser => "Registry (Benutzer)",
        StartupSource.RegistryMachine64 => "Registry (alle Benutzer)",
        StartupSource.RegistryMachine32 => "Registry (alle Benutzer, 32-Bit)",
        StartupSource.FolderUser => "Autostart-Ordner (Benutzer)",
        _ => "Autostart-Ordner (alle Benutzer)",
    };
}

/// <summary>
/// Startup Manager (§35). Einträge werden NIE gelöscht – nur aktiviert/deaktiviert, und zwar exakt
/// so wie der Windows Task-Manager es tut (Schlüssel "StartupApproved"). Dadurch bleibt jede Änderung
/// auch im Task-Manager sichtbar und dort rückgängig zu machen.
/// Vor jeder Änderung wird ein Backup der StartupApproved-Werte geschrieben (§85).
/// Benutzer-Einträge (HKCU) werden direkt umgeschaltet; Einträge für alle Benutzer (HKLM) über den
/// Admin-Helper mit UAC (der Helper prüft, dass der Eintrag existiert, und legt nie neue an).
/// </summary>
public sealed class StartupService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    /// <summary>Einträge, die für Sicherheit/Funktion von Windows wichtig sind.</summary>
    private static readonly string[] SystemRelevantNames = ["SecurityHealth", "Windows Defender", "WindowsDefender", "RtkAudUService", "ctfmon"];

    public IReadOnlyList<StartupEntry> GetEntries()
    {
        var list = new List<StartupEntry>();
        ReadRegistry(RegistryHive.CurrentUser, RegistryView.Registry64, StartupSource.RegistryUser, "Run", canToggle: true, list);
        ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry64, StartupSource.RegistryMachine64, "Run", canToggle: true, list);
        ReadRegistry(RegistryHive.LocalMachine, RegistryView.Registry32, StartupSource.RegistryMachine32, "Run32", canToggle: true, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), RegistryHive.CurrentUser, StartupSource.FolderUser, canToggle: true, list);
        ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), RegistryHive.LocalMachine, StartupSource.FolderCommon, canToggle: true, list);
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ReadRegistry(RegistryHive hive, RegistryView view, StartupSource source, string approvedSubKey, bool canToggle, List<StartupEntry> into)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var run = root.OpenSubKey(RunKey);
        if (run is null) return;
        foreach (var name in run.GetValueNames())
        {
            if (string.IsNullOrEmpty(name)) continue;
            var command = run.GetValue(name)?.ToString() ?? string.Empty;
            var exe = ExtractExecutable(command);
            into.Add(new StartupEntry(name, command, exe, source,
                IsApproved(hive, approvedSubKey, name), canToggle,
                IsSystemRelevant(name, exe),
                exe is not null && File.Exists(exe) ? SignatureVerifier.Verify(exe) : null));
        }
    }

    private void ReadFolder(string folder, RegistryHive hive, StartupSource source, bool canToggle, List<StartupEntry> into)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            into.Add(new StartupEntry(Path.GetFileNameWithoutExtension(file), file, file, source,
                IsApproved(hive, "StartupFolder", fileName), canToggle,
                false,
                file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? SignatureVerifier.Verify(file) : null));
        }
    }

    /// <summary>
    /// StartupApproved-Format: 12 Byte, erstes Byte gerade (0x02/0x06) = aktiviert, ungerade (0x03) = deaktiviert,
    /// Byte 4–11 = Zeitpunkt der Deaktivierung (FILETIME). Kein Eintrag = aktiviert.
    /// </summary>
    private static bool IsApproved(RegistryHive hive, string subKey, string valueName)
    {
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = root.OpenSubKey($@"{ApprovedBase}\{subKey}");
        return k?.GetValue(valueName) is not byte[] { Length: > 0 } data || (data[0] & 0x01) == 0;
    }

    private static bool IsSystemRelevant(string name, string? exe)
    {
        if (SystemRelevantNames.Any(n => name.Equals(n, StringComparison.OrdinalIgnoreCase))) return true;
        if (exe is null) return false;
        try { return PathGuard.IsWithin(exe, Environment.GetFolderPath(Environment.SpecialFolder.Windows)); }
        catch (ArgumentException) { return false; }
    }

    /// <summary>Extrahiert den Programmpfad aus einer Run-Befehlszeile (nur für Anzeige/Signaturprüfung, nie zur Ausführung).</summary>
    internal static string? ExtractExecutable(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var c = Environment.ExpandEnvironmentVariables(command.Trim());
        string candidate;
        if (c.StartsWith('"'))
        {
            var end = c.IndexOf('"', 1);
            if (end <= 1) return null;
            candidate = c[1..end];
        }
        else
        {
            var idx = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = idx > 0 ? c[..(idx + 4)] : c.Split(' ')[0];
        }
        try
        {
            if (!Path.IsPathFullyQualified(candidate))
            {
                var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), candidate);
                return File.Exists(sys) ? sys : null;
            }
            return Path.GetFullPath(candidate);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>True, wenn die Änderung Adminrechte (UAC) erfordert.</summary>
    public static bool RequiresAdmin(StartupEntry entry)
        => entry.Source is StartupSource.RegistryMachine64 or StartupSource.RegistryMachine32 or StartupSource.FolderCommon;

    /// <summary>Aktiviert/deaktiviert einen Eintrag. Erstellt vorher ein Backup der StartupApproved-Werte.</summary>
    public async Task<OperationResult> SetEnabledAsync(StartupEntry entry, bool enabled)
    {
        if (!entry.CanToggle) return OperationResult.Fail("Dieser Eintrag kann nicht umgeschaltet werden.");
        string backup;
        try { backup = BackupApproved(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Backup konnte nicht erstellt werden – Änderung abgebrochen.", ex.Message);
        }

        if (RequiresAdmin(entry))
        {
            var source = entry.Source switch { StartupSource.RegistryMachine64 => 1, StartupSource.RegistryMachine32 => 2, _ => 3 };
            var name = entry.Source == StartupSource.FolderCommon ? Path.GetFileName(entry.Command) : entry.Name;
            var token = CommandCatalog.EncodeStartupToken(enabled, source, name);
            Core.Processes.CommandResult r;
            try
            {
                r = await Core.Processes.ElevatedCommandRunner.RunAsync(AllowedCommand.SetMachineStartup, token).ConfigureAwait(false);
            }
            catch (ArgumentException)
            {
                return OperationResult.Fail("Der Name dieses Eintrags ist zu lang oder ungültig.");
            }
            if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
            AppLog.Info("Startup", $"Autostart (alle Benutzer) {(enabled ? "aktiviert" : "deaktiviert")}: {entry.Name}", $"Backup: {Path.GetFileName(backup)}, ExitCode={r.ExitCode}");
            return r.ExitCode == 0
                ? OperationResult.Ok(enabled ? "Aktiviert." : "Deaktiviert. Wirksam ab dem nächsten Start.")
                : OperationResult.Fail("Änderung fehlgeschlagen.", r.Output);
        }

        var subKey = entry.Source == StartupSource.FolderUser ? "StartupFolder" : "Run";
        var valueName = entry.Source == StartupSource.FolderUser ? Path.GetFileName(entry.Command) : entry.Name;
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey($@"{ApprovedBase}\{subKey}", writable: true);
            var data = new byte[12];
            if (enabled)
            {
                data[0] = 0x02;
            }
            else
            {
                data[0] = 0x03;
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
            }
            k.SetValue(valueName, data, RegistryValueKind.Binary);
            AppLog.Info("Startup", $"Autostart {(enabled ? "aktiviert" : "deaktiviert")}: {entry.Name}", $"Backup: {Path.GetFileName(backup)}");
            return OperationResult.Ok(enabled ? "Aktiviert." : "Deaktiviert. Wirksam ab der nächsten Anmeldung.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AppLog.Error("Startup", $"Änderung fehlgeschlagen: {entry.Name}", ex);
            return OperationResult.Fail("Änderung fehlgeschlagen: " + ex.Message);
        }
    }

    /// <summary>Stellt die StartupApproved-Werte des Benutzers (HKCU) aus einem Backup wieder her.</summary>
    public OperationResult RestoreUserBackup(string backupFile)
    {
        try
        {
            if (!PathGuard.IsWithin(backupFile, AppPaths.BackupDirectory)) return OperationResult.Fail("Ungültige Backup-Datei.");
            var snapshot = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>(File.ReadAllText(backupFile));
            if (snapshot is null || !snapshot.TryGetValue("HKCU", out var hkcu)) return OperationResult.Fail("Backup enthält keine Benutzer-Einträge.");
            foreach (var sub in new[] { "Run", "StartupFolder" })
            {
                if (!hkcu.TryGetValue(sub, out var values)) continue;
                using var k = Registry.CurrentUser.CreateSubKey($@"{ApprovedBase}\{sub}", writable: true);
                foreach (var (name, hex) in values) k.SetValue(name, Convert.FromHexString(hex), RegistryValueKind.Binary);
            }
            AppLog.Info("Startup", "Autostart-Backup wiederhergestellt", Path.GetFileName(backupFile));
            return OperationResult.Ok("Backup wiederhergestellt (Benutzer-Einträge).");
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or UnauthorizedAccessException or ArgumentException)
        {
            return OperationResult.Fail("Wiederherstellung fehlgeschlagen: " + ex.Message);
        }
    }

    public static IReadOnlyList<string> ListBackups()
        => Directory.GetFiles(AppPaths.BackupDirectory, "startup-*.json").OrderByDescending(f => f).ToList();

    private static string BackupApproved()
    {
        var snapshot = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>
        {
            ["HKCU"] = ReadApproved(RegistryHive.CurrentUser, ["Run", "StartupFolder"]),
            ["HKLM"] = ReadApproved(RegistryHive.LocalMachine, ["Run", "Run32", "StartupFolder"]),
        };
        var file = Path.Combine(AppPaths.BackupDirectory, $"startup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        return file;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadApproved(RegistryHive hive, string[] subs)
    {
        var result = new Dictionary<string, Dictionary<string, string>>();
        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        foreach (var sub in subs)
        {
            var values = new Dictionary<string, string>();
            using var k = root.OpenSubKey($@"{ApprovedBase}\{sub}");
            if (k is not null)
                foreach (var n in k.GetValueNames())
                    if (k.GetValue(n) is byte[] b) values[n] = Convert.ToHexString(b);
            result[sub] = values;
        }
        return result;
    }
}
