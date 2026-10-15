using System.Text.RegularExpressions;

namespace UltimateX.Core.Analysis;

public enum SetupProfile { Basic, Office, Gaming, Developer, Custom }

public enum PackageSource { Winget, MicrosoftStore }

/// <summary>
/// Eintrag des NEW-PC-SETUP-Katalogs (v1.2 §19–§29). Installiert wird ausschließlich über den Windows-Paket-Manager
/// (winget-Quelle bzw. Microsoft Store). Die Installer-URL aus dem Paketmanifest muss zu einer der offiziellen
/// Herstellerdomains passen – sonst wird die Installation verweigert (§31).
/// </summary>
public sealed record SoftwarePackage(
    string Key,
    string Name,
    string Publisher,
    string Category,
    string PackageId,
    PackageSource Source,
    IReadOnlyList<string> OfficialHosts,
    IReadOnlyList<string> DetectNames,
    IReadOnlyList<SetupProfile> Profiles,
    string Description);

public static partial class SoftwareCatalog
{
    private static SoftwarePackage P(string key, string name, string publisher, string category, string id, string[] hosts, string[] detect,
        SetupProfile[] profiles, string description, PackageSource source = PackageSource.Winget)
        => new(key, name, publisher, category, id, source, hosts, detect, profiles, description);

    /// <summary>Nichts wird automatisch ausgewählt; Profile sind nur Vorschläge (§28, §29).</summary>
    public static IReadOnlyList<SoftwarePackage> All { get; } =
    [
        P("firefox", "Mozilla Firefox", "Mozilla", "Browser", "Mozilla.Firefox",
            ["download.mozilla.org", "download-installer.cdn.mozilla.net", "ftp.mozilla.org", "archive.mozilla.org"], ["Mozilla Firefox"],
            [SetupProfile.Basic, SetupProfile.Office], "Freier Browser der Mozilla Foundation."),
        P("acrobat", "Adobe Acrobat Reader", "Adobe", "PDF", "Adobe.Acrobat.Reader.64-bit",
            ["adobe.com"], ["Adobe Acrobat"], [SetupProfile.Basic, SetupProfile.Office], "PDF-Dokumente anzeigen, ausfüllen und unterschreiben."),
        P("onedrive", "Microsoft OneDrive", "Microsoft", "Cloud", "Microsoft.OneDrive",
            ["oneclient.sfx.ms", "microsoft.com", "live.com"], ["Microsoft OneDrive"], [SetupProfile.Basic, SetupProfile.Office], "Cloud-Speicher von Microsoft (Dateien sichern und synchronisieren)."),
        P("winrar", "WinRAR", "win.rar GmbH", "Werkzeuge", "RARLab.WinRAR",
            ["rarlab.com", "win-rar.com"], ["WinRAR"], [], "Packprogramm für RAR/ZIP (Testversion, danach kostenpflichtige Lizenz). Kostenlose Alternative: 7-Zip."),
        P("7zip", "7-Zip", "Igor Pavlov", "Werkzeuge", "7zip.7zip",
            ["7-zip.org", "github.com/ip7z/7zip/"], ["7-Zip"], [SetupProfile.Basic], "Packprogramm für ZIP, 7z, RAR u. v. m."),
        P("vlc", "VLC media player", "VideoLAN", "Medien", "VideoLAN.VLC",
            ["videolan.org"], ["VLC media player"], [SetupProfile.Basic], "Spielt nahezu alle Video- und Audioformate ab."),
        P("powertoys", "Microsoft PowerToys", "Microsoft", "Werkzeuge", "Microsoft.PowerToys",
            ["github.com/microsoft/PowerToys/"], ["PowerToys"], [SetupProfile.Basic, SetupProfile.Developer], "Nützliche Windows-Erweiterungen von Microsoft."),
        P("bitwarden", "Bitwarden", "Bitwarden Inc.", "Sicherheit", "Bitwarden.Bitwarden",
            ["github.com/bitwarden/clients/", "bitwarden.com"], ["Bitwarden"], [], "Open-Source-Passwortmanager (optional). ULTIMATE X liest keine Passwörter."),
        P("chrome", "Google Chrome", "Google", "Browser", "Google.Chrome",
            ["dl.google.com", "google.com"], ["Google Chrome"], [SetupProfile.Basic], "Browser von Google."),
        P("brave", "Brave", "Brave Software", "Browser", "Brave.Brave",
            ["github.com/brave/brave-browser/", "brave.com"], ["Brave"], [], "Datenschutzorientierter Browser."),
        P("libreoffice", "LibreOffice", "The Document Foundation", "Office", "TheDocumentFoundation.LibreOffice",
            ["documentfoundation.org", "libreoffice.org"], ["LibreOffice"], [], "Freie Office-Suite."),
        P("teams", "Microsoft Teams", "Microsoft", "Kommunikation", "Microsoft.Teams",
            ["microsoft.com", "office.net", "aka.ms", "teams.microsoft.com"], ["Microsoft Teams"], [SetupProfile.Office], "Chat und Videokonferenzen."),
        P("zoom", "Zoom Workplace", "Zoom", "Kommunikation", "Zoom.Zoom",
            ["zoom.us", "zoom.com"], ["Zoom"], [], "Videokonferenzen."),
        P("whatsapp", "WhatsApp", "WhatsApp Inc.", "Kommunikation", "9NKSQGP7F2NH",
            [], ["WhatsApp"], [], "WhatsApp-Desktop-App aus dem Microsoft Store.", PackageSource.MicrosoftStore),
        P("discord", "Discord", "Discord Inc.", "Kommunikation", "Discord.Discord",
            ["discord.com", "discordapp.net", "discordapp.com"], ["Discord"], [SetupProfile.Gaming], "Sprach- und Textchat für Gamer."),
        P("steam", "Steam", "Valve", "Gaming", "Valve.Steam",
            ["steampowered.com", "steamstatic.com", "akamaihd.net"], ["Steam"], [SetupProfile.Gaming], "Spieleplattform von Valve."),
        P("epic", "Epic Games Launcher", "Epic Games", "Gaming", "EpicGames.EpicGamesLauncher",
            ["epicgames.com", "akamaized.net", "unrealengine.com"], ["Epic Games Launcher"], [SetupProfile.Gaming], "Spieleplattform von Epic Games."),
        P("obs", "OBS Studio", "OBS Project", "Medien", "OBSProject.OBSStudio",
            ["github.com/obsproject/obs-studio/", "obsproject.com"], ["OBS Studio"], [], "Aufnahme und Streaming."),
        P("vscode", "Visual Studio Code", "Microsoft", "Entwicklung", "Microsoft.VisualStudioCode",
            ["visualstudio.com", "microsoft.com", "msecnd.net"], ["Microsoft Visual Studio Code"], [SetupProfile.Developer], "Code-Editor von Microsoft."),
        P("git", "Git", "The Git Development Community", "Entwicklung", "Git.Git",
            ["github.com/git-for-windows/git/"], ["Git"], [SetupProfile.Developer], "Versionsverwaltung."),
        P("python", "Python 3.13", "Python Software Foundation", "Entwicklung", "Python.Python.3.13",
            ["python.org"], ["Python 3.13"], [SetupProfile.Developer], "Programmiersprache Python."),
        P("node", "Node.js LTS", "OpenJS Foundation", "Entwicklung", "OpenJS.NodeJS.LTS",
            ["nodejs.org"], ["Node.js"], [SetupProfile.Developer], "JavaScript-Laufzeitumgebung."),
        P("dotnetsdk", ".NET SDK 8", "Microsoft", "Entwicklung", "Microsoft.DotNet.SDK.8",
            ["microsoft.com", "dot.net", "azureedge.net"], ["Microsoft .NET SDK 8"], [SetupProfile.Developer], ".NET-Entwicklungswerkzeuge."),
        P("dotnetruntime", ".NET Desktop Runtime 8", "Microsoft", "Laufzeitumgebungen", "Microsoft.DotNet.DesktopRuntime.8",
            ["microsoft.com", "dot.net", "azureedge.net"], ["Microsoft Windows Desktop Runtime - 8"], [], "Wird von vielen .NET-Programmen benötigt."),
        P("vcredist", "Visual C++ Redistributable", "Microsoft", "Laufzeitumgebungen", "Microsoft.VCRedist.2015+.x64",
            ["microsoft.com", "aka.ms"], ["Microsoft Visual C++ 2015-2022 Redistributable (x64)", "Microsoft Visual C++ v14 Redistributable (x64)"], [SetupProfile.Gaming],
            "Laufzeitbibliotheken für viele Programme und Spiele."),
        P("java", "Java (Eclipse Temurin 21 JRE)", "Eclipse Adoptium", "Laufzeitumgebungen", "EclipseAdoptium.Temurin.21.JRE",
            ["github.com/adoptium/", "adoptium.net"], ["Eclipse Temurin JRE"], [], "Freie Java-Laufzeitumgebung."),
        P("terminal", "Windows Terminal", "Microsoft", "Entwicklung", "Microsoft.WindowsTerminal",
            ["github.com/microsoft/terminal/"], ["Windows Terminal"], [SetupProfile.Developer], "Moderne Konsole für PowerShell, CMD und WSL."),
        // ---- v1.3 Treiber & Hardware-Werkzeuge
        P("hwinfo", "HWiNFO", "REALiX", "Hardware-Tools", "REALiX.HWiNFO",
            ["hwinfo.com"], ["HWiNFO"], [SetupProfile.Gaming], "Sensoren und Hardware-Details – liefert ULTIMATE X CPU-Temperatur, Takt und Leistungsaufnahme (Sensorquellen)."),
        P("inteldsa", "Intel Driver & Support Assistant", "Intel", "Treiber", "Intel.IntelDriverAndSupportAssistant",
            ["intel.com"], ["Intel® Driver & Support Assistant", "Intel(R) Driver & Support Assistant"], [], "Hält Intel-Treiber (Grafik, WLAN, Bluetooth, Chipsatz) aktuell."),
        P("cpuz", "CPU-Z", "CPUID", "Hardware-Tools", "CPUID.CPU-Z",
            ["cpuid.com"], ["CPUID CPU-Z", "CPU-Z"], [], "Detaillierte Angaben zu Prozessor, Mainboard und Arbeitsspeicher."),
        P("gpuz", "GPU-Z", "TechPowerUp", "Hardware-Tools", "TechPowerUp.GPU-Z",
            ["techpowerup.com"], ["TechPowerUp GPU-Z", "GPU-Z"], [], "Detaillierte Angaben zur Grafikkarte."),
        P("crystaldiskinfo", "CrystalDiskInfo", "Crystal Dew World", "Hardware-Tools", "CrystalDewWorld.CrystalDiskInfo",
            ["sourceforge.net/projects/crystaldiskinfo/", "crystalmark.info"], ["CrystalDiskInfo"], [], "SMART-Werte und Zustand von SSDs/Festplatten."),
        P("crystaldiskmark", "CrystalDiskMark", "Crystal Dew World", "Hardware-Tools", "CrystalDewWorld.CrystalDiskMark",
            ["sourceforge.net/projects/crystaldiskmark/", "crystalmark.info"], ["CrystalDiskMark"], [], "Geschwindigkeitstest für Laufwerke."),
        P("ghub", "Logitech G HUB", "Logitech", "Gaming", "Logitech.GHUB",
            ["logi.com", "logitech.com"], ["Logitech G HUB"], [], "Software für Logitech-Mäuse, -Tastaturen und -Headsets."),
        P("icue", "Corsair iCUE", "Corsair", "Gaming", "Corsair.iCUE.5",
            ["corsair.com"], ["iCUE", "Corsair iCUE"], [], "Software für Corsair-Peripherie, Lüfter und RGB."),
        // ---- v1.3 Spiele-Plattformen
        P("gog", "GOG GALAXY", "GOG.com", "Gaming", "GOG.Galaxy",
            ["gog.com"], ["GOG GALAXY"], [], "Spieleplattform von GOG (DRM-freie Spiele)."),
        P("ea", "EA app", "Electronic Arts", "Gaming", "ElectronicArts.EADesktop",
            ["origin-a.akamaihd.net/EA-Desktop-Client-Download/", "ea.com"], ["EA app"], [], "Spieleplattform von Electronic Arts."),
        P("ubisoft", "Ubisoft Connect", "Ubisoft", "Gaming", "Ubisoft.Connect",
            ["ubi.com", "ubisoft.com"], ["Ubisoft Connect"], [], "Spieleplattform von Ubisoft."),
        P("battlenet", "Battle.net", "Blizzard Entertainment", "Gaming", "Blizzard.BattleNet",
            ["battle.net", "blizzard.com"], ["Battle.net"], [], "Spieleplattform von Blizzard."),
        // ---- v1.3 Alltag & Werkzeuge
        P("notepadpp", "Notepad++", "Notepad++ Team", "Werkzeuge", "Notepad++.Notepad++",
            ["github.com/notepad-plus-plus/notepad-plus-plus/", "notepad-plus-plus.org"], ["Notepad++"], [], "Texteditor mit Syntaxhervorhebung."),
        P("everything", "Everything", "voidtools", "Werkzeuge", "voidtools.Everything",
            ["voidtools.com"], ["Everything"], [], "Findet Dateien auf allen Laufwerken in Sekunden."),
        P("sharex", "ShareX", "ShareX Team", "Werkzeuge", "ShareX.ShareX",
            ["github.com/ShareX/ShareX/"], ["ShareX"], [], "Bildschirmfotos und -aufnahmen."),
        P("pwsh", "PowerShell 7", "Microsoft", "Entwicklung", "Microsoft.PowerShell",
            ["github.com/PowerShell/PowerShell/"], ["PowerShell 7"], [SetupProfile.Developer], "Aktuelle PowerShell (zusätzlich zu Windows PowerShell 5.1)."),
        P("thunderbird", "Mozilla Thunderbird", "Mozilla", "Kommunikation", "Mozilla.Thunderbird",
            ["download.mozilla.org", "download-installer.cdn.mozilla.net", "ftp.mozilla.org", "archive.mozilla.org"], ["Mozilla Thunderbird"], [], "E-Mail-Programm von Mozilla."),
        P("spotify", "Spotify", "Spotify AB", "Medien", "Spotify.Spotify",
            ["download.scdn.co", "spotify.com"], ["Spotify"], [], "Musik-Streaming."),
        P("gimp", "GIMP", "The GIMP Team", "Medien", "GIMP.GIMP.3",
            ["gimp.org"], ["GIMP"], [], "Freie Bildbearbeitung."),
        P("paintnet", "Paint.NET", "dotPDN", "Medien", "dotPDN.PaintDotNet",
            ["github.com/paintdotnet/release/", "getpaint.net"], ["paint.net"], [], "Einfache Bildbearbeitung."),
        P("audacity", "Audacity", "Audacity", "Medien", "Audacity.Audacity",
            ["github.com/audacity/audacity/"], ["Audacity"], [], "Audio aufnehmen und schneiden."),
        P("handbrake", "HandBrake", "The HandBrake Team", "Medien", "HandBrake.HandBrake",
            ["github.com/HandBrake/HandBrake/"], ["HandBrake"], [], "Videos umwandeln und verkleinern."),
        P("keepassxc", "KeePassXC", "KeePassXC Team", "Sicherheit", "KeePassXCTeam.KeePassXC",
            ["github.com/keepassxreboot/keepassxc/"], ["KeePassXC"], [], "Offline-Passwortmanager (Open Source). ULTIMATE X liest keine Passwörter."),
        P("malwarebytes", "Malwarebytes", "Malwarebytes", "Sicherheit", "Malwarebytes.Malwarebytes",
            ["mbamupdates.com", "malwarebytes.com"], ["Malwarebytes"], [], "Zusätzlicher Malware-Scanner (Defender bleibt aktiv)."),
        P("teamviewer", "TeamViewer", "TeamViewer", "Fernwartung", "TeamViewer.TeamViewer",
            ["teamviewer.com"], ["TeamViewer"], [], "Fernhilfe und Fernzugriff (privat kostenlos)."),
    ];

    /// <summary>ClamAV (Open Source Security, §27) – nur nach der Online-Prüfung des Projekts installierbar, daher nicht in <see cref="All"/>.</summary>
    public static SoftwarePackage ClamAv { get; } = P("clamav", "ClamAV", "Cisco Systems", "Sicherheit", "Cisco.ClamAV",
        ["clamav.net", "github.com/Cisco-Talos/clamav/"], ["ClamAV"], [], "Open-Source-Virenscanner als Zusatz zu Microsoft Defender (kein Ersatz).");

    /// <summary>Microsoft 365 Apps werden nicht über winget, sondern mit dem Office Deployment Tool installiert (§20).</summary>
    public const string OfficeKey = "office";

    public static SoftwarePackage? Get(string key) => All.FirstOrDefault(p => p.Key == key);

    /// <summary>Pakete eines Profils (ohne Office – das wird separat behandelt).</summary>
    public static IReadOnlyList<SoftwarePackage> ForProfile(SetupProfile profile)
    {
        if (profile == SetupProfile.Custom) return [];
        var basic = All.Where(p => p.Profiles.Contains(SetupProfile.Basic));
        var extra = All.Where(p => profile != SetupProfile.Basic && p.Profiles.Contains(profile));
        return basic.Concat(extra).Distinct().ToList();
    }

    public static bool ProfileIncludesOffice(SetupProfile profile) => profile == SetupProfile.Office;

    /// <summary>„Recommended for this PC“ (§30) – nur Empfehlungen, keine automatische Auswahl.</summary>
    public static (string Kind, IReadOnlyList<string> Keys, IReadOnlyList<string> Notes) Recommend(SystemFacts f, IReadOnlyCollection<string> installedKeys)
    {
        var gpu = HardwareDatabase.FindGpu(f.GpuName);
        var gaming = gpu is { GameIndex: >= 45 } || installedKeys.Contains("steam") || installedKeys.Contains("epic");
        var developer = installedKeys.Contains("vscode") || installedKeys.Contains("git") || installedKeys.Contains("python");
        var keys = new List<string>();
        var notes = new List<string>();
        string kind;
        if (gaming)
        {
            kind = "Gaming PC detected";
            keys.AddRange(["steam", "discord", "vcredist", "hwinfo"]);
            notes.Add("Grafiktreiber: NVIDIA wird automatisch gesucht (Karte „GRAFIKTREIBER“); danach Gaming Check ausführen.");
        }
        else if (developer)
        {
            kind = "Developer PC detected";
            keys.AddRange(["git", "vscode", "dotnetsdk", "python"]);
        }
        else
        {
            kind = "Office PC detected";
            keys.AddRange(["acrobat", "firefox", "teams"]);
            notes.Add("Microsoft 365 Apps über das offizielle Office Deployment Tool.");
        }
        keys.AddRange(["chrome", "7zip", "vlc"]);
        return (kind, keys.Distinct().Where(k => !installedKeys.Contains(k)).ToList(), notes);
    }

    // ------------------------------------------------------------------ Sicherheit der Quellen (§31)

    [GeneratedRegex(@"\b[0-9A-Fa-f]{64}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();

    /// <summary>Bekannte Filehoster, Download-Portale und URL-Kürzer – nie zulässig, selbst wenn ein Manifest sie nennt.</summary>
    private static readonly string[] BlockedHosts =
    [
        "mediafire.com", "mega.nz", "dropbox.com", "drive.google.com", "1drv.ms", "wetransfer.com", "sendspace.com", "rapidgator.net",
        "softonic.com", "chip.de", "filehippo.com", "uptodown.com", "softpedia.com", "download.cnet.com", "fosshub.com",
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "pastebin.com",
    ];

    /// <summary>Prüft, ob eine Download-URL HTTPS nutzt und zu einer offiziellen Herstellerdomain gehört.</summary>
    public static (bool Ok, string Reason) ValidateDownloadUrl(string? url, IReadOnlyList<string> officialHosts)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "Keine gültige Download-Adresse.");
        if (uri.Scheme != Uri.UriSchemeHttps) return (false, "Download nicht über HTTPS.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) return (false, "Adresse enthält Zugangsdaten.");
        var host = uri.IdnHost.ToLowerInvariant();
        if (BlockedHosts.Any(b => HostMatches(host, b))) return (false, $"{host} ist ein Filehoster/Download-Portal und nicht zulässig.");
        if (officialHosts.Count == 0) return (false, "Für dieses Paket ist keine offizielle Herstellerdomain hinterlegt.");
        return officialHosts.Any(h => IsOfficial(uri, host, h))
            ? (true, $"Offizielle Quelle ({host})")
            : (false, $"{host} gehört nicht zu den offiziellen Domains ({string.Join(", ", officialHosts)}).");
    }

    /// <summary>
    /// Eintrag ohne „/“: Host oder Subdomain davon. Eintrag mit „/“ (z. B. „github.com/microsoft/PowerToys/“): exakt dieser Host
    /// und der Pfad beginnt mit dem angegebenen Präfix – so ist bei GitHub nur das offizielle Repository zulässig, nicht jedes beliebige.
    /// </summary>
    public static bool IsOfficial(Uri uri, string host, string entry)
    {
        var slash = entry.IndexOf('/', StringComparison.Ordinal);
        if (slash < 0) return HostMatches(host, entry);
        return host.Equals(entry[..slash], StringComparison.OrdinalIgnoreCase)
               && uri.AbsolutePath.StartsWith(entry[slash..], StringComparison.OrdinalIgnoreCase)
               && !uri.AbsolutePath.Contains("/../", StringComparison.Ordinal);
    }

    /// <summary>Exakter Host oder Subdomain davon (kein Suffix-Trick wie „evil-adobe.com“).</summary>
    public static bool HostMatches(string host, string allowed)
        => host.Equals(allowed, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase);

    public static bool IsValidSha256(string? hash) => hash is { Length: 64 } && Sha256Regex().IsMatch(hash);

    /// <summary>Vergleicht zwei SHA-256-Werte (Groß-/Kleinschreibung egal).</summary>
    public static bool HashesMatch(string? expected, string? actual)
        => IsValidSha256(expected) && IsValidSha256(actual) && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    /// <summary>Herausgeber einer Signatur passt zum erwarteten Hersteller (Teilstring im Zertifikatsnamen, z. B. „Microsoft Corporation“).</summary>
    public static bool PublisherMatches(string? signerSubject, string expectedPublisher)
        => !string.IsNullOrWhiteSpace(signerSubject) && !string.IsNullOrWhiteSpace(expectedPublisher)
           && signerSubject.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase);

    /// <summary>Daten aus „winget show“: Version, Herausgeber, Installer-URL und SHA-256 (sprachunabhängig ausgewertet).</summary>
    public sealed record ManifestInfo(string? Version, string? Publisher, string? InstallerUrl, string? Sha256);

    public static ManifestInfo ParseWingetShow(string output)
    {
        string? version = null, publisher = null, url = null;
        foreach (var raw in output.Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim();
            var idx = line.IndexOf(':', StringComparison.Ordinal);
            if (idx <= 0) continue;
            var label = line[..idx].Trim().ToLowerInvariant();
            var value = line[(idx + 1)..].Trim();
            if (value.Length == 0) continue;
            if (version is null && label is "version") version = value;
            else if (publisher is null && label is "publisher" or "herausgeber") publisher = value;
            else if (url is null && (label.Contains("installer", StringComparison.Ordinal) || label.Contains("installationsprogramm", StringComparison.Ordinal))
                     && (label.Contains("url", StringComparison.Ordinal)) && UrlRegex().Match(value) is { Success: true } m) url = m.Value;
        }
        var sha = Sha256Regex().Matches(output).Select(m => m.Value).FirstOrDefault();
        return new ManifestInfo(version, publisher, url, sha);
    }

    /// <summary>Pakete, deren Anzeigename in der Liste installierter Programme vorkommt (§19 „vorhandene Programme“).</summary>
    public static IReadOnlyDictionary<string, string?> DetectInstalled(IEnumerable<(string Name, string? Version)> programs)
    {
        var list = programs.ToList();
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var p in All)
        {
            var hit = list.FirstOrDefault(x => p.DetectNames.Any(d => x.Name.StartsWith(d, StringComparison.OrdinalIgnoreCase)));
            if (hit.Name is not null) result[p.Key] = hit.Version;
        }
        return result;
    }
}

/// <summary>Erzeugt Suchanfragen für den Standardbrowser (§15, §16). Es wird nichts automatisch abgerufen oder ausgewertet.</summary>
public static class SearchQueries
{
    public const string SearchBase = "https://www.google.com/search?q=";

    public static string ShortCpu(string? cpu)
    {
        if (string.IsNullOrWhiteSpace(cpu)) return "";
        var s = cpu.Replace("(R)", "", StringComparison.OrdinalIgnoreCase).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("CPU", "", StringComparison.Ordinal).Replace("Processor", "", StringComparison.OrdinalIgnoreCase)
            .Replace("with Radeon Graphics", "", StringComparison.OrdinalIgnoreCase);
        var at = s.IndexOf('@', StringComparison.Ordinal);
        if (at > 0) s = s[..at];
        s = s.Replace("Intel Core", "", StringComparison.OrdinalIgnoreCase).Replace("AMD", "", StringComparison.Ordinal)
            .Replace("-Core", "", StringComparison.OrdinalIgnoreCase);
        s = Regex.Replace(s, @"\b(Six|Eight|Twelve|Sixteen|Quad)\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    public static string ShortGpu(string? gpu)
    {
        if (string.IsNullOrWhiteSpace(gpu)) return "";
        return Regex.Replace(gpu.Replace("NVIDIA", "", StringComparison.OrdinalIgnoreCase).Replace("GeForce", "", StringComparison.OrdinalIgnoreCase)
            .Replace("AMD", "", StringComparison.Ordinal).Replace("Radeon", "", StringComparison.Ordinal).Replace("(TM)", "", StringComparison.OrdinalIgnoreCase),
            @"\s+", " ").Trim();
    }

    public static string ResolutionText(Resolution r) => r switch { Resolution.P1080 => "1080p", Resolution.P1440 => "1440p", _ => "4K" };

    public static IReadOnlyList<string> GameQueries(string? cpu, string? gpu, double? ramGb, Resolution res)
    {
        var c = ShortCpu(cpu);
        var g = ShortGpu(gpu);
        var ram = ramGb is { } r ? $"{Math.Round(r):0}GB" : "";
        var rt = ResolutionText(res);
        return new[]
        {
            $"{c} {g} {ram} best games {rt}",
            $"{g} benchmark games {rt} ultra",
            $"best games for {g} {rt} 2026",
        }.Select(q => Regex.Replace(q, @"\s+", " ").Trim()).Where(q => q.Length > 0).Distinct().ToList();
    }

    public static IReadOnlyList<string> SoftwareCategories { get; } =
        ["Video Editing", "CAD", "Programming", "Streaming", "3D Rendering", "Office", "Photo Editing"];

    public static string SoftwareQuery(string category, string? cpu, string? gpu, double? ramGb, string? os)
    {
        var ram = ramGb is { } r ? $"{Math.Round(r):0}GB RAM" : "";
        return Regex.Replace($"best {category} software for {ShortCpu(cpu)} {ShortGpu(gpu)} {ram} {os}", @"\s+", " ").Trim();
    }

    public static string GameRequirementsQuery(string game) => $"{game} system requirements PC";

    /// <summary>Baut die HTTPS-Such-URL (Text wird vollständig URL-kodiert – keine Injektion möglich).</summary>
    public static Uri SearchUrl(string query) => new(SearchBase + Uri.EscapeDataString(query.Trim()));
}
