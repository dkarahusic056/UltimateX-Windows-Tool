using System.Globalization;
using Microsoft.Win32;
using UltimateX.Core.Security;

namespace UltimateX.Services.Software;

public sealed record InstalledProgram(
    string Name,
    string? Publisher,
    string? Version,
    ulong? SizeBytes,
    DateTime? InstallDate,
    string? InstallLocation,
    string Scope);

/// <summary>
/// Programm-Manager – Phase 1: Inventur aus den offiziellen Uninstall-Registry-Schlüsseln (§34).
/// Deinstallation wird bewusst an die Windows-Einstellungen übergeben (kein Ausführen fremder
/// UninstallString-Befehlszeilen). Software-Updates via winget folgen in Phase 3.
/// </summary>
public sealed class ProgramService
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public IReadOnlyList<InstalledProgram> GetPrograms()
    {
        var result = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        Read(RegistryHive.LocalMachine, RegistryView.Registry64, "Alle Benutzer (64-Bit)", result);
        Read(RegistryHive.LocalMachine, RegistryView.Registry32, "Alle Benutzer (32-Bit)", result);
        Read(RegistryHive.CurrentUser, RegistryView.Default, "Aktueller Benutzer", result);
        return result.Values.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void Read(RegistryHive hive, RegistryView view, string scope, Dictionary<string, InstalledProgram> into)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using var key = root.OpenSubKey(UninstallKey);
        if (key is null) return;

        foreach (var sub in key.GetSubKeyNames())
        {
            using var k = key.OpenSubKey(sub);
            if (k is null) continue;
            var name = k.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (k.GetValue("SystemComponent") is 1) continue;      // von Windows ausgeblendete Komponenten
            if (k.GetValue("ParentKeyName") is string) continue;       // Updates/Patches eines Hauptprogramms

            var version = k.GetValue("DisplayVersion") as string;
            var id = name + "|" + version;
            if (into.ContainsKey(id)) continue;

            ulong? size = k.GetValue("EstimatedSize") is int kb && kb > 0 ? (ulong)kb * 1024 : null;
            DateTime? date = k.GetValue("InstallDate") is string d &&
                             DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;

            var location = k.GetValue("InstallLocation") as string;
            location = string.IsNullOrWhiteSpace(location) ? null : location.Trim().Trim('"');

            into[id] = new InstalledProgram(name.Trim(), k.GetValue("Publisher") as string, version, size, date, location, scope);
        }
    }

    /// <summary>Öffnet den Installationsordner nur, wenn es ein existierender, lokaler, absoluter Ordner ist.</summary>
    public static bool TryOpenInstallLocation(InstalledProgram program)
    {
        var loc = program.InstallLocation;
        if (loc is null) return false;
        string normalized;
        try { normalized = PathGuard.Normalize(loc); }
        catch (ArgumentException) { return false; }
        if (!Directory.Exists(normalized)) return false;

        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            ArgumentList = { normalized },
            UseShellExecute = false,
        });
        return true;
    }
}
