using System.Text.RegularExpressions;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Updates;

public sealed record SoftwareUpdate(string Name, string Id, string InstalledVersion, string AvailableVersion, string Source);

/// <summary>
/// Software Update Manager (§33) über den Windows-Paket-Manager winget (Quelle: Microsoft winget-Repository
/// bzw. Microsoft Store). Die App ruft winget ausschließlich über die Allowlist auf; Paket-IDs werden streng validiert.
/// Installer, die Adminrechte benötigen, fordern diese selbst per UAC an.
/// </summary>
public sealed partial class WingetService
{
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.+_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdRegex();

    public static bool IsAvailable => File.Exists(CommandCatalog.WingetPath);

    public async Task<(IReadOnlyList<SoftwareUpdate> Updates, string? Error)> ListUpgradesAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return ([], "winget ist nicht installiert (App-Installer aus dem Microsoft Store).");
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.WingetListUpgrades), null, ct).ConfigureAwait(false);
        if (!r.Started) return ([], r.Output);
        var updates = Parse(r.Output);
        AppLog.Info("SoftwareUpdates", "winget-Update-Suche", $"{updates.Count} Updates, ExitCode={r.ExitCode}");
        if (updates.Count == 0 && r.ExitCode != 0 && !r.Output.Contains("---", StringComparison.Ordinal))
            return (updates, "winget meldete einen Fehler: " + r.Output.Trim().Split('\n').LastOrDefault());
        return (updates, null);
    }

    public async Task<CommandResult> UpgradeAsync(string packageId, IProgress<string>? progress, CancellationToken ct = default)
    {
        AppLog.Info("SoftwareUpdates", $"Update gestartet: {packageId}");
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.WingetUpgrade, packageId), progress, ct).ConfigureAwait(false);
        AppLog.Info("SoftwareUpdates", $"Update beendet: {packageId}", $"ExitCode={r.ExitCode}");
        return r;
    }

    /// <summary>
    /// Liest die Tabellenausgabe von "winget upgrade". Spaltenpositionen werden aus der Kopfzeile ermittelt
    /// (sprachunabhängig, da nur die Positionen zählen). Zeilen mit ungültiger Paket-ID werden verworfen.
    /// </summary>
    internal static IReadOnlyList<SoftwareUpdate> Parse(string output)
    {
        var lines = output.Replace("\r", "", StringComparison.Ordinal).Split('\n');
        int dashIndex = Array.FindIndex(lines, l => l.Length >= 20 && l.Trim().Trim('-').Length == 0 && l.Contains("----", StringComparison.Ordinal));
        if (dashIndex < 1) return [];

        var header = lines[dashIndex - 1];
        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' ')) starts.Add(i);
        if (starts.Count < 4) return [];

        string Col(string line, int c)
        {
            if (starts[c] >= line.Length) return "";
            var end = c + 1 < starts.Count ? Math.Min(starts[c + 1], line.Length) : line.Length;
            return line[starts[c]..end].Trim();
        }

        var result = new List<SoftwareUpdate>();
        for (int i = dashIndex + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) break;
            if (line.Length < starts[^1]) break; // Fußzeile ("x Aktualisierungen verfügbar.")
            var id = Col(line, 1);
            if (!IdRegex().IsMatch(id)) continue;
            result.Add(new SoftwareUpdate(Col(line, 0), id, Col(line, 2), Col(line, 3), starts.Count > 4 ? Col(line, 4) : ""));
        }
        return result;
    }
}
