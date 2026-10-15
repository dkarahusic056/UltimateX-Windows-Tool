using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.NetSec;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.NetSec;

public sealed record PrivacyStatus(PrivacySetting Setting, IReadOnlyDictionary<string, int?> Values, PrivacyState State, bool HasBackup)
{
    public string ValuesText => string.Join(" · ", Setting.ValueNames.Select(n => $"{n} = {(Values.TryGetValue(n, out var v) && v is { } x ? x.ToString(System.Globalization.CultureInfo.InvariantCulture) : "nicht gesetzt")}"));
}

/// <summary>
/// v1.3 Datenschutz: einzeln schaltbare Windows-Einstellungen aus der festen Tabelle <see cref="PrivacyCatalog"/>.
/// HKCU wird direkt geschrieben, HKLM ausschließlich über AllowedCommand.PrivacySetMachine (UAC, reg.exe mit festen Argumenten).
/// Vor jeder Änderung wird der ursprüngliche Zustand in %LOCALAPPDATA%\UltimateX\privacy-backup.json gemerkt (Rückgängig).
/// Keine Windows-Komponenten werden entfernt, keine Hosts-Datei, keine Dienste.
/// </summary>
public sealed class PrivacyService
{
    private readonly object _lock = new();

    public static string BackupPath => Path.Combine(AppPaths.UserDataRoot, "privacy-backup.json");

    public IReadOnlyList<PrivacyStatus> GetStatus()
    {
        var backup = LoadBackup();
        return PrivacyCatalog.Settings.Select(s =>
        {
            var values = Read(s);
            return new PrivacyStatus(s, values, PrivacyCatalog.Evaluate(s, values), backup.Has(s.Id));
        }).ToList();
    }

    /// <summary>Aktuelle Werte (null = nicht vorhanden bzw. kein DWORD).</summary>
    public static IReadOnlyDictionary<string, int?> Read(PrivacySetting s)
    {
        var result = new Dictionary<string, int?>(StringComparer.Ordinal);
        try
        {
            using var hive = s.Scope == PrivacyScope.Machine
                ? RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                : RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default);
            using var key = hive.OpenSubKey(s.KeyPath);
            foreach (var n in s.ValueNames) result[n] = key?.GetValue(n) is int i ? i : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            foreach (var n in s.ValueNames) result.TryAdd(n, null);
        }
        return result;
    }

    /// <summary>Datenschutzfreundlichen Wert setzen. Der Ausgangszustand wird vorher gesichert (nur beim ersten Mal).</summary>
    public async Task<OperationResult> ProtectAsync(PrivacySetting s, CancellationToken ct)
    {
        var current = Read(s);
        try { RememberOriginal(s, current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Sicherung konnte nicht geschrieben werden – Änderung abgebrochen.", ex.Message);
        }

        var target = s.ValueNames.ToDictionary(n => n, _ => (int?)s.PrivateValue, StringComparer.Ordinal);
        var r = await WriteAsync(s, target, ct).ConfigureAwait(false);
        if (r.Success) AppLog.Info("Privacy", $"Datenschutz aktiviert: {s.Id}");
        return r;
    }

    /// <summary>Ursprünglichen Wert wiederherstellen (aus der Sicherung). Ohne Sicherung: Windows-Standard.</summary>
    public async Task<OperationResult> RestoreAsync(PrivacySetting s, CancellationToken ct)
    {
        var backup = LoadBackup();
        Dictionary<string, int?> target;
        if (backup.Original.TryGetValue(s.Id, out var original))
            target = s.ValueNames.ToDictionary(n => n, n => original.TryGetValue(n, out var v) ? v : null, StringComparer.Ordinal);
        else
            target = s.ValueNames.ToDictionary(n => n, _ => s.DefaultValue, StringComparer.Ordinal);

        var r = await WriteAsync(s, target, ct).ConfigureAwait(false);
        if (!r.Success) return r;
        try
        {
            lock (_lock)
            {
                var b = LoadBackup();
                if (b.Forget(s.Id)) SaveBackup(b);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Privacy", "Sicherung konnte nicht aktualisiert werden", ex.Message);
        }
        AppLog.Info("Privacy", $"Ursprungszustand wiederhergestellt: {s.Id}");
        return OperationResult.Ok(backup.Has(s.Id) ? "Ursprünglicher Zustand wiederhergestellt." : "Windows-Standard wiederhergestellt (keine Sicherung vorhanden).", r.TechnicalDetails);
    }

    private async Task<OperationResult> WriteAsync(PrivacySetting s, IReadOnlyDictionary<string, int?> target, CancellationToken ct)
    {
        if (s.Scope == PrivacyScope.CurrentUser)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(s.KeyPath, writable: true);
                foreach (var (name, value) in target)
                {
                    if (!s.ValueNames.Contains(name, StringComparer.Ordinal)) continue; // nur Werte aus der festen Tabelle
                    if (value is { } v) key.SetValue(name, v, RegistryValueKind.DWord);
                    else key.DeleteValue(name, throwOnMissingValue: false);
                }
                return OperationResult.Ok("Gespeichert. Manche Änderungen wirken erst nach erneuter Anmeldung.");
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return OperationResult.Fail("Einstellung konnte nicht geschrieben werden.", ex.Message);
            }
        }

        // HKLM: genau ein Wert pro Einstellung, über den Admin-Helper.
        var name0 = s.ValueNames[0];
        target.TryGetValue(name0, out var desired);
        if (Read(s).TryGetValue(name0, out var now) && now == desired)
            return OperationResult.Ok("Bereits eingestellt.");
        string parameter;
        if (desired is null) parameter = PrivacyCatalog.MachineParameter(s.Id, null);
        else if (s.AllowedMachineValues.Contains(desired.Value)) parameter = PrivacyCatalog.MachineParameter(s.Id, desired);
        else return OperationResult.Fail($"Der ursprüngliche Wert {desired} kann nicht automatisch gesetzt werden – bitte manuell prüfen ({s.RegistryPath}).");

        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.PrivacySetMachine, parameter, null, ct).ConfigureAwait(false);
        if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
        return r.ExitCode == 0
            ? OperationResult.Ok("Gespeichert (gilt für alle Benutzer). Wirkt nach Neustart bzw. Gruppenrichtlinien-Aktualisierung.", r.Output)
            : OperationResult.Fail($"Änderung fehlgeschlagen (Exit {r.ExitCode}).", r.Output);
    }

    private void RememberOriginal(PrivacySetting s, IReadOnlyDictionary<string, int?> current)
    {
        lock (_lock)
        {
            var b = LoadBackup();
            if (b.Remember(s.Id, current)) SaveBackup(b);
        }
    }

    public static PrivacyBackup LoadBackup()
    {
        try
        {
            return File.Exists(BackupPath) ? PrivacyBackup.Deserialize(File.ReadAllText(BackupPath)) : new PrivacyBackup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Privacy", "Sicherung nicht lesbar", ex.Message);
            return new PrivacyBackup();
        }
    }

    private static void SaveBackup(PrivacyBackup b)
    {
        Directory.CreateDirectory(AppPaths.UserDataRoot);
        var tmp = BackupPath + ".tmp";
        File.WriteAllText(tmp, b.Serialize());
        File.Move(tmp, BackupPath, overwrite: true);
    }
}
