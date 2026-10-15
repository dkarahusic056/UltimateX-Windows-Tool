using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using UltimateX.Core.Analysis;
using UltimateX.Core.Logging;

namespace UltimateX.Services.Gaming;

public sealed record InstalledGame(string Name, string Launcher, string? InstallPath, ulong? SizeBytes, int? SteamAppId, GameInfo? Known);

/// <summary>
/// Erkennt installierte Spiele (v1.2 §17) anhand der lokalen Bibliotheksdateien von Steam, Epic Games, Xbox/Game Pass und GOG
/// sowie der Programmliste (EA app, Ubisoft Connect). Es werden ausschließlich Namen und Installationsorte gelesen –
/// keine Kontodaten, Anmeldeinformationen oder Passwörter.
/// </summary>
public static partial class InstalledGamesService
{
    [GeneratedRegex(@"""(\w+)""\s+""([^""]*)""", RegexOptions.CultureInvariant)]
    private static partial Regex VdfPairRegex();

    private static readonly HashSet<int> SteamTools = [228980, 1070560, 1391110, 1493710, 1628350, 250820, 1826330, 2180100, 1161040];

    public static IReadOnlyList<InstalledGame> Detect(IEnumerable<(string Name, string? Publisher, string? InstallLocation)>? programs = null)
    {
        var list = new List<InstalledGame>();
        Try("Steam", () => list.AddRange(Steam()));
        Try("Epic", () => list.AddRange(Epic()));
        Try("Xbox", () => list.AddRange(Xbox()));
        Try("GOG", () => list.AddRange(Gog()));
        if (programs is not null) Try("Programme", () => list.AddRange(FromPrograms(programs)));

        return list
            .GroupBy(g => GameCompatibility.Normalize(g.Name))
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void Try(string source, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException or ArgumentException)
        {
            AppLog.Warn("Games", $"{source}-Bibliothek nicht lesbar", ex.Message);
        }
    }

    /// <summary>
    /// Zuordnung zur Spieledatenbank: zuerst über die Steam-AppID, dann über den Namen. <see cref="GameCompatibility.Games"/>
    /// enthält auch die online gespeicherten Spiele (v1.3) – diese müssen vorher geladen sein (GameRequirementsOnlineService.LoadUserGames).
    /// </summary>
    private static GameInfo? Match(string name, int? steamId)
        => (steamId is > 0 ? GameCompatibility.Games.FirstOrDefault(g => g.SteamAppId == steamId) : null) ?? GameCompatibility.Find(name);

    /// <summary>Ordnet ein bereits erkanntes Spiel neu zu (z. B. nachdem Anforderungen online geladen wurden) – ohne erneutes Einlesen der Bibliotheken.</summary>
    public static InstalledGame Rematch(InstalledGame game) => game with { Known = Match(game.Name, game.SteamAppId) };

    private static IEnumerable<InstalledGame> Steam()
    {
        string? steamPath;
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            steamPath = k?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steamPath)) yield break;
        steamPath = steamPath.Replace('/', '\\');

        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match m in VdfPairRegex().Matches(File.ReadAllText(vdf)))
                if (m.Groups[1].Value == "path") libraries.Add(m.Groups[2].Value.Replace(@"\\", @"\", StringComparison.Ordinal));

        foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var pairs = VdfPairRegex().Matches(File.ReadAllText(acf)).GroupBy(m => m.Groups[1].Value, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Groups[2].Value, StringComparer.OrdinalIgnoreCase);
                if (!pairs.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name)) continue;
                int? id = pairs.TryGetValue("appid", out var a) && int.TryParse(a, out var ai) ? ai : null;
                if (id is { } x && SteamTools.Contains(x)) continue;
                if (name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)) continue;
                ulong? size = pairs.TryGetValue("SizeOnDisk", out var s) && ulong.TryParse(s, out var sz) ? sz : null;
                var dir = pairs.TryGetValue("installdir", out var d) ? Path.Combine(apps, "common", d) : null;
                yield return new InstalledGame(name, "Steam", dir, size, id, Match(name, id));
            }
        }
    }

    private static IEnumerable<InstalledGame> Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) yield break;
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (root.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array
                && !cats.EnumerateArray().Any(c => string.Equals(c.GetString(), "games", StringComparison.OrdinalIgnoreCase))) continue;
            var path = root.TryGetProperty("InstallLocation", out var p) ? p.GetString() : null;
            ulong? size = root.TryGetProperty("InstallSize", out var s) && s.TryGetUInt64(out var sz) ? sz : null;
            yield return new InstalledGame(name, "Epic Games", path, size, null, Match(name, null));
        }
    }

    private static IEnumerable<InstalledGame> Xbox()
    {
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        {
            var root = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(root)) continue;
            foreach (var game in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(game);
                if (name.Equals("GameSave", StringComparison.OrdinalIgnoreCase)) continue;
                yield return new InstalledGame(name, "Xbox / Game Pass", game, null, null, Match(name, null));
            }
        }
    }

    private static IEnumerable<InstalledGame> Gog()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key is null) yield break;
        foreach (var sub in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(sub);
            var name = g?.GetValue("gameName") as string;
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new InstalledGame(name, "GOG", g?.GetValue("path") as string, null, null, Match(name, null));
        }
    }

    private static IEnumerable<InstalledGame> FromPrograms(IEnumerable<(string Name, string? Publisher, string? InstallLocation)> programs)
    {
        foreach (var (name, publisher, location) in programs)
        {
            if (publisher is null) continue;
            string? launcher = publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase) ? "EA app"
                : publisher.Contains("Ubisoft", StringComparison.OrdinalIgnoreCase) ? "Ubisoft Connect"
                : publisher.Contains("Riot Games", StringComparison.OrdinalIgnoreCase) ? "Riot Games"
                : publisher.Contains("Blizzard", StringComparison.OrdinalIgnoreCase) ? "Battle.net"
                : null;
            if (launcher is null) continue;
            if (name.Contains("EA app", StringComparison.OrdinalIgnoreCase) || name.Contains("Connect", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Battle.net", StringComparison.OrdinalIgnoreCase) || name.Contains("Riot Client", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Vanguard", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new InstalledGame(name, launcher, location, null, null, Match(name, null));
        }
    }
}
