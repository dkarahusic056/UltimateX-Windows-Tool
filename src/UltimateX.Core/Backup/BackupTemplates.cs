namespace UltimateX.Core.Backup;

/// <summary>Bekannte Ordner des Benutzers (für Tests frei setzbar).</summary>
public sealed record KnownFolders(
    string UserProfile,
    string Desktop,
    string Documents,
    string Pictures,
    string Videos,
    string Music,
    string Downloads,
    string? OneDrive,
    string LocalAppData,
    string RoamingAppData)
{
    /// <summary>Ordner des angemeldeten Benutzers.</summary>
    public static KnownFolders Current()
    {
        static string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var profile = F(Environment.SpecialFolder.UserProfile);
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        if (string.IsNullOrWhiteSpace(oneDrive)) oneDrive = Environment.GetEnvironmentVariable("OneDriveConsumer");
        if (string.IsNullOrWhiteSpace(oneDrive)) oneDrive = Environment.GetEnvironmentVariable("OneDriveCommercial");
        return new KnownFolders(profile, F(Environment.SpecialFolder.DesktopDirectory), F(Environment.SpecialFolder.MyDocuments),
            F(Environment.SpecialFolder.MyPictures), F(Environment.SpecialFolder.MyVideos), F(Environment.SpecialFolder.MyMusic),
            Path.Combine(profile, "Downloads"), string.IsNullOrWhiteSpace(oneDrive) ? null : oneDrive,
            F(Environment.SpecialFolder.LocalApplicationData), F(Environment.SpecialFolder.ApplicationData));
    }
}

/// <summary>Vorlage zum Anhaken im Profil-Editor.</summary>
public sealed record BackupSourceTemplate(string Id, string Label, string Description);

/// <summary>Vorlagen für Quellen und Ausschlüsse.</summary>
public static class BackupTemplates
{
    public const string Desktop = "desktop";
    public const string Documents = "documents";
    public const string Pictures = "pictures";
    public const string Videos = "videos";
    public const string Music = "music";
    public const string Downloads = "downloads";
    public const string OneDrive = "onedrive";
    public const string Bookmarks = "bookmarks";
    public const string SourceRepos = "repos";

    public static IReadOnlyList<BackupSourceTemplate> SourceTemplates { get; } =
    [
        new(Desktop, "Desktop", "Dateien auf dem Desktop"),
        new(Documents, "Dokumente", "Ordner Dokumente"),
        new(Pictures, "Bilder", "Ordner Bilder"),
        new(Videos, "Videos", "Ordner Videos"),
        new(Music, "Musik", "Ordner Musik"),
        new(Downloads, "Downloads", "Ordner Downloads"),
        new(OneDrive, "OneDrive", "Lokal vorhandene OneDrive-Dateien (nur online verfügbare Dateien werden nicht heruntergeladen)"),
        new(Bookmarks, "Browser-Lesezeichen", "Nur die Lesezeichen-Dateien von Edge, Chrome (Bookmarks) und Firefox (places.sqlite)"),
        new(SourceRepos, "Quellcode (source\\repos)", "Visual-Studio-Projekte unter %USERPROFILE%\\source\\repos"),
    ];

    /// <summary>Ausschluss-Vorlage „Programmierung“: Build-Ausgaben und Paket-Caches. .git bleibt bewusst erhalten.</summary>
    public static IReadOnlyList<string> ProgrammingExclusions { get; } =
        ["bin", "obj", "node_modules", ".vs", "packages", "*.tmp", "Thumbs.db", "~$*"];

    /// <summary>Allgemeine Standard-Ausschlüsse (temporäre Dateien von Windows/Office).</summary>
    public static IReadOnlyList<string> DefaultExclusions { get; } = ["*.tmp", "Thumbs.db", "~$*", "desktop.ini"];

    /// <summary>
    /// Löst eine Vorlage in Quellen auf. Nicht vorhandene Ordner/Dateien werden weggelassen (z. B. kein OneDrive, kein Firefox).
    /// Browser-Lesezeichen: ausschließlich die Dateien "Bookmarks" (Edge/Chrome, alle Profile) und "places.sqlite" (Firefox, alle Profile).
    /// </summary>
    public static IReadOnlyList<BackupSource> Resolve(string templateId, KnownFolders folders)
    {
        var list = new List<BackupSource>();
        void Dir(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) list.Add(new BackupSource { Path = path, IsFile = false, Template = templateId });
        }

        switch (templateId)
        {
            case Desktop: Dir(folders.Desktop); break;
            case Documents: Dir(folders.Documents); break;
            case Pictures: Dir(folders.Pictures); break;
            case Videos: Dir(folders.Videos); break;
            case Music: Dir(folders.Music); break;
            case Downloads: Dir(folders.Downloads); break;
            case OneDrive: Dir(folders.OneDrive); break;
            case SourceRepos: Dir(Path.Combine(folders.UserProfile, "source", "repos")); break;
            case Bookmarks:
                foreach (var f in FindBookmarkFiles(folders)) list.Add(new BackupSource { Path = f, IsFile = true, Template = templateId });
                break;
        }
        return list;
    }

    /// <summary>Alle Quellen eines Profils: aufgelöste Vorlagen + manuell gewählte Ordner/Dateien (ohne Duplikate).</summary>
    public static List<BackupSource> ExpandSources(BackupProfile profile, KnownFolders folders)
    {
        var result = new List<BackupSource>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in profile.Templates.Distinct(StringComparer.Ordinal))
            foreach (var s in Resolve(id, folders))
                if (seen.Add(s.Path)) result.Add(s);
        foreach (var s in profile.Sources)
            if (!string.IsNullOrWhiteSpace(s.Path) && seen.Add(s.Path)) result.Add(new BackupSource { Path = s.Path, IsFile = s.IsFile, Template = null });
        return result;
    }

    /// <summary>Lesezeichen-Dateien aller Browserprofile (Edge, Chrome, Firefox).</summary>
    public static IReadOnlyList<string> FindBookmarkFiles(KnownFolders folders)
    {
        var result = new List<string>();
        foreach (var userData in new[]
                 {
                     Path.Combine(folders.LocalAppData, "Microsoft", "Edge", "User Data"),
                     Path.Combine(folders.LocalAppData, "Google", "Chrome", "User Data"),
                 })
        {
            foreach (var dir in SafeDirs(userData))
            {
                var name = Path.GetFileName(dir);
                if (!(name.Equals("Default", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase))) continue;
                var file = Path.Combine(dir, "Bookmarks");
                if (File.Exists(file)) result.Add(file);
            }
        }
        foreach (var dir in SafeDirs(Path.Combine(folders.RoamingAppData, "Mozilla", "Firefox", "Profiles")))
        {
            var file = Path.Combine(dir, "places.sqlite");
            if (File.Exists(file)) result.Add(file);
        }
        return result;
    }

    private static IEnumerable<string> SafeDirs(string root)
    {
        try
        {
            return Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
