using System.Text;
using UltimateX.Core.Backup;
using UltimateX.Core.Security;
using UltimateX.Services.Backup;

namespace UltimateX.Core.Tests;

// ======================================================================== v1.3 Backup-Center

/// <summary>Temporärer Ordner unter %TEMP%, wird am Ende vollständig entfernt.</summary>
internal sealed class BackupTempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ux-backup-tests-" + Guid.NewGuid().ToString("N")[..10]);

    public BackupTempDir() => Directory.CreateDirectory(Path);

    public string Sub(params string[] parts)
    {
        var p = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(p);
        return p;
    }

    public static void Write(string file, string content, DateTime? mtimeUtc = null)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content, Encoding.UTF8);
        if (mtimeUtc is { } t) File.SetLastWriteTimeUtc(file, t);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

public class BackupPathTests
{
    [Theory]
    [InlineData(@"C:\Users\denni\Documents\a.txt", @"C\Users\denni\Documents\a.txt")]
    [InlineData(@"d:\Daten\Projekt", @"D\Daten\Projekt")]
    [InlineData(@"E:\", "E")]
    public void Store_paths_roundtrip(string original, string store)
    {
        Assert.Equal(store, BackupPaths.ToStoreRelative(original));
        Assert.Equal(original.Length == 3 ? "E:\\" : char.ToUpperInvariant(original[0]) + original[1..], BackupPaths.ToOriginalPath(store));
    }

    [Theory]
    [InlineData(@"C\..\Windows\System32\x.dll")]
    [InlineData(@"C\a\.\b")]
    [InlineData(@"CC\a")]
    [InlineData(@"C:\a")]
    [InlineData(@"C\a\\b")]
    [InlineData(@"\\server\share")]
    [InlineData(@"C/a/b")]
    [InlineData(@"c\a")]
    [InlineData("")]
    public void Invalid_store_paths_from_manifests_are_rejected(string rel)
    {
        Assert.False(BackupPaths.IsValidStoreRelative(rel));
        Assert.Throws<ArgumentException>(() => BackupPaths.ToOriginalPath(rel));
    }

    [Theory]
    [InlineData(@"\\server\share\x")]
    [InlineData(@"relative\path")]
    [InlineData("")]
    public void Only_local_drive_paths_are_backed_up(string path)
        => Assert.Throws<ArgumentException>(() => BackupPaths.ToStoreRelative(path));

    [Fact]
    public void Folder_names_are_sanitized_unique_and_not_reserved()
    {
        var a = BackupPaths.MakeFolderName("Test/Profil: 1");
        Assert.True(BackupPaths.IsValidFolderName(a));
        Assert.DoesNotContain('/', a);
        Assert.DoesNotContain(':', a);
        Assert.Equal("Treiber-Profil", BackupPaths.MakeFolderName("Treiber"));
        Assert.Equal("Profil", BackupPaths.MakeFolderName("..."));
        Assert.Equal("Fotos-2", BackupPaths.MakeFolderName("Fotos", ["fotos"]));
        Assert.False(BackupPaths.IsValidFolderName(".."));
        Assert.False(BackupPaths.IsValidFolderName(@"a\b"));
        Assert.False(BackupPaths.IsValidFolderName("CON"));
        Assert.Throws<ArgumentException>(() => BackupPaths.ProfileRoot(@"E:\", @"..\..\Windows"));
    }

    [Fact]
    public void Version_names_are_strict()
    {
        Assert.True(BackupPaths.IsVersionName("2026-10-14_153000"));
        Assert.False(BackupPaths.IsVersionName("2026-13-14_153000"));
        Assert.False(BackupPaths.IsVersionName("2026-10-14_153000x"));
        Assert.False(BackupPaths.IsVersionName("..\\2026-10-14_153000"));
        Assert.Equal("2026-10-14_153000", BackupPaths.VersionName(new DateTime(2026, 10, 14, 15, 30, 0)));
    }

    [Fact]
    public void Restore_to_folder_keeps_selected_name_and_cannot_escape()
    {
        Assert.Equal(@"D:\R\Projekt\src\x.cs", BackupPaths.MapToFolder(@"C\Users\a\Projekt\src\x.cs", @"C\Users\a\Projekt", @"D:\R"));
        Assert.Equal(@"D:\R\x.cs", BackupPaths.MapToFolder(@"C\Users\a\x.cs", @"C\Users\a\x.cs", @"D:\R"));
        Assert.Throws<ArgumentException>(() => BackupPaths.MapToFolder(@"C\Users\b\x.cs", @"C\Users\a", @"D:\R"));
        Assert.Throws<ArgumentException>(() => BackupPaths.MapToFolder(@"C\Users\a\..\..\x", @"C\Users\a", @"D:\R"));
        Assert.True(BackupPaths.IsUnderStorePrefix(@"C\a\b", @"C\a"));
        Assert.False(BackupPaths.IsUnderStorePrefix(@"C\ab\c", @"C\a"));
    }
}

public class BackupExclusionTests
{
    private static readonly ExclusionMatcher Programming = new(BackupTemplates.ProgrammingExclusions);

    [Theory]
    [InlineData(@"src\bin\Debug\app.dll", true)]
    [InlineData(@"bin", true)]
    [InlineData(@"obj\project.assets.json", true)]
    [InlineData(@"web\node_modules\react\index.js", true)]
    [InlineData(@".vs\config\applicationhost.config", true)]
    [InlineData(@"packages", true)]
    [InlineData(@"build\temp.tmp", true)]
    [InlineData(@"Bilder\Thumbs.db", true)]
    [InlineData(@"Docs\~$Bericht.docx", true)]
    [InlineData(@"THUMBS.DB", true)]
    [InlineData(@".git\objects\ab\cdef", false)]
    [InlineData(@".git\HEAD", false)]
    [InlineData(@"src\binary.txt", false)]
    [InlineData(@"robj\x.cs", false)]
    [InlineData(@"src\Program.cs", false)]
    public void Programming_template(string rel, bool excluded) => Assert.Equal(excluded, Programming.IsExcluded(rel));

    [Fact]
    public void Path_patterns_respect_folder_boundaries()
    {
        var m = new ExclusionMatcher([@"Cache\*", @"**\logs\*.log", "a?c.txt"]);
        Assert.True(m.IsExcluded(@"Cache\x.bin"));
        Assert.False(m.IsExcluded(@"Cache\sub\x.bin")); // "*" geht nicht über "\" hinweg
        Assert.False(m.IsExcluded(@"x\Cache\y.bin"));
        Assert.True(m.IsExcluded(@"logs\a.log"));
        Assert.True(m.IsExcluded(@"x\y\logs\a.log"));
        Assert.False(m.IsExcluded(@"x\y\logs\a.txt"));
        Assert.True(m.IsExcluded("abc.txt"));
        Assert.False(m.IsExcluded("abbc.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Windows")]
    [InlineData("a|b")]
    [InlineData("\"x\"")]
    public void Invalid_patterns_are_dropped(string pattern)
    {
        Assert.Null(ExclusionMatcher.Normalize(pattern));
        Assert.Empty(new ExclusionMatcher([pattern]).Patterns);
    }

    [Fact]
    public void Regex_metacharacters_are_literal() => Assert.False(new ExclusionMatcher(["a.b"]).IsExcluded("axb"));
}

public sealed class BackupTemplateTests : IDisposable
{
    private readonly BackupTempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private KnownFolders Folders(string? oneDrive = null) => new(
        _tmp.Sub("profile"), _tmp.Sub("profile", "Desktop"), _tmp.Sub("profile", "Documents"), _tmp.Sub("profile", "Pictures"),
        _tmp.Sub("profile", "Videos"), _tmp.Sub("profile", "Music"), _tmp.Sub("profile", "Downloads"), oneDrive,
        _tmp.Sub("local"), _tmp.Sub("roaming"));

    [Fact]
    public void Bookmarks_template_takes_only_bookmark_files()
    {
        var f = Folders();
        BackupTempDir.Write(Path.Combine(f.LocalAppData, "Microsoft", "Edge", "User Data", "Default", "Bookmarks"), "{}");
        BackupTempDir.Write(Path.Combine(f.LocalAppData, "Microsoft", "Edge", "User Data", "Default", "History"), "x");
        BackupTempDir.Write(Path.Combine(f.LocalAppData, "Google", "Chrome", "User Data", "Profile 2", "Bookmarks"), "{}");
        BackupTempDir.Write(Path.Combine(f.LocalAppData, "Google", "Chrome", "User Data", "System Profile", "Bookmarks"), "{}");
        BackupTempDir.Write(Path.Combine(f.RoamingAppData, "Mozilla", "Firefox", "Profiles", "abc.default-release", "places.sqlite"), "x");
        BackupTempDir.Write(Path.Combine(f.RoamingAppData, "Mozilla", "Firefox", "Profiles", "abc.default-release", "cookies.sqlite"), "x");

        var sources = BackupTemplates.Resolve(BackupTemplates.Bookmarks, f);
        Assert.Equal(3, sources.Count);
        Assert.All(sources, s => Assert.True(s.IsFile));
        Assert.All(sources, s => Assert.Contains(Path.GetFileName(s.Path), new[] { "Bookmarks", "places.sqlite" }));
        Assert.DoesNotContain(sources, s => s.Path.Contains("System Profile", StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_optional_folders_are_omitted_and_sources_deduplicated()
    {
        var f = Folders(oneDrive: null);
        Assert.Empty(BackupTemplates.Resolve(BackupTemplates.OneDrive, f));
        Assert.Empty(BackupTemplates.Resolve(BackupTemplates.SourceRepos, f));
        _tmp.Sub("profile", "source", "repos");
        Assert.Single(BackupTemplates.Resolve(BackupTemplates.SourceRepos, f));

        var p = new BackupProfile
        {
            Templates = [BackupTemplates.Documents, BackupTemplates.Documents, BackupTemplates.Desktop],
            Sources = [new BackupSource { Path = f.Documents }, new BackupSource { Path = _tmp.Sub("extra") }],
        };
        var all = BackupTemplates.ExpandSources(p, f);
        Assert.Equal(3, all.Count);
        Assert.Equal(BackupTemplates.Documents, all.First(s => s.Path == f.Documents).Template);
    }

    [Fact]
    public void Programming_exclusions_keep_git() => Assert.DoesNotContain(BackupTemplates.ProgrammingExclusions, e => e.Contains(".git", StringComparison.Ordinal));
}

public class BackupPlannerTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Unchanged_files_are_linked_changed_files_copied()
    {
        var prev = new Dictionary<string, BackupManifestEntry>(StringComparer.OrdinalIgnoreCase)
        {
            [@"C\a.txt"] = new() { Path = @"C\a.txt", Size = 10, LastWriteUtc = T0, Sha256 = "AA" },
            [@"C\b.txt"] = new() { Path = @"C\b.txt", Size = 10, LastWriteUtc = T0 },
        };
        Assert.Equal(BackupFileAction.Link, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 10, T0.AddSeconds(1)), prev, BackupCompareMode.SizeAndTime).Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 11, T0), prev, BackupCompareMode.SizeAndTime).Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 10, T0.AddSeconds(5)), prev, BackupCompareMode.SizeAndTime).Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\n.txt", @"C\n.txt", 10, T0), prev, BackupCompareMode.SizeAndTime).Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 10, T0), null, BackupCompareMode.SizeAndTime).Action);
        // Hash-Modus
        Assert.Equal(BackupFileAction.Link, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 10, T0), prev, BackupCompareMode.SizeTimeAndHash, _ => "aa").Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\a.txt", @"C\a.txt", 10, T0), prev, BackupCompareMode.SizeTimeAndHash, _ => "BB").Action);
        Assert.Equal(BackupFileAction.Copy, BackupPlanner.Decide(new(@"C:\b.txt", @"C\b.txt", 10, T0), prev, BackupCompareMode.SizeTimeAndHash, _ => "AA").Action);
    }

    [Fact]
    public void Retention_keeps_newest_complete_versions_and_ignores_foreign_names()
    {
        var versions = new (string, bool)[]
        {
            ("2026-10-01_120000", true), ("2026-10-02_120000", false), ("2026-10-03_120000", true),
            ("2026-10-04_120000", true), ("2026-10-05_120000", false), ("Notizen", true), ("..", true),
        };
        var del = BackupPlanner.SelectVersionsToDelete(versions, 2);
        Assert.Equal(new[] { "2026-10-01_120000", "2026-10-02_120000" }, del.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Empty(BackupPlanner.SelectVersionsToDelete(versions, 10).Where(v => v.StartsWith("2026-10-03", StringComparison.Ordinal)));
        Assert.Empty(BackupPlanner.SelectVersionsToDelete([("2026-10-05_120000", false)], 1)); // ohne vollständige Version nie löschen
        Assert.DoesNotContain("Notizen", BackupPlanner.SelectVersionsToDelete(versions, 1));
        Assert.Equal(3, BackupPlanner.SelectVersionsToDelete(versions, 0).Count); // keep < 1 → 1: alles älter als 10-04
    }

    [Fact]
    public void Daily_and_weekly_schedules_catch_up_missed_slots()
    {
        var local = (int d, int h, int m) => new DateTime(2026, 10, d, h, m, 0, DateTimeKind.Local);
        var p = new BackupProfile
        {
            CreatedUtc = local(10, 8, 0).ToUniversalTime(),
            Schedule = new BackupScheduleSettings { Kind = BackupScheduleKind.Daily, TimeOfDay = new TimeSpan(19, 0, 0) },
        };
        Assert.Equal(local(13, 19, 0), BackupPlanner.MostRecentSlot(p.Schedule, local(14, 18, 0)));
        Assert.True(BackupPlanner.IsDue(p, local(14, 18, 0))); // verpasst → nachholen
        p.LastAttemptUtc = local(13, 20, 0).ToUniversalTime();
        Assert.False(BackupPlanner.IsDue(p, local(14, 18, 0)));
        Assert.True(BackupPlanner.IsDue(p, local(14, 19, 30)));
        p.Enabled = false;
        Assert.False(BackupPlanner.IsDue(p, local(14, 19, 30)));

        // 14.10.2026 ist ein Mittwoch → letzter Sonntag 11.10.
        var weekly = new BackupScheduleSettings { Kind = BackupScheduleKind.Weekly, DayOfWeek = DayOfWeek.Sunday, TimeOfDay = new TimeSpan(10, 0, 0) };
        Assert.Equal(local(11, 10, 0), BackupPlanner.MostRecentSlot(weekly, local(14, 12, 0)));
        Assert.Equal(local(4, 10, 0), BackupPlanner.MostRecentSlot(weekly, local(11, 9, 0)));
        Assert.Null(BackupPlanner.MostRecentSlot(new BackupScheduleSettings { Kind = BackupScheduleKind.Manual }, local(14, 12, 0)));

        // Neues Profil startet nicht rückwirkend
        var fresh = new BackupProfile { CreatedUtc = local(14, 20, 0).ToUniversalTime(), Schedule = p.Schedule };
        Assert.False(BackupPlanner.IsDue(fresh, local(14, 21, 0)));
    }

    [Fact]
    public void Connect_schedule_and_overdue_warning()
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var p = new BackupProfile { Schedule = new BackupScheduleSettings { Kind = BackupScheduleKind.OnTargetConnected }, CreatedUtc = now.AddDays(-30) };
        Assert.True(BackupPlanner.ShouldRunOnConnect(p, now));
        p.LastSuccessUtc = now.AddHours(-3);
        Assert.False(BackupPlanner.ShouldRunOnConnect(p, now));
        p.LastSuccessUtc = now.AddDays(-2);
        Assert.True(BackupPlanner.ShouldRunOnConnect(p, now));

        Assert.Null(BackupPlanner.CheckOverdue(p, 7, now));
        p.LastSuccessUtc = now.AddDays(-9);
        var o = BackupPlanner.CheckOverdue(p, 7, now);
        Assert.NotNull(o);
        Assert.Equal(9, o!.DaysSinceSuccess);
        p.LastSuccessUtc = null;
        Assert.NotNull(BackupPlanner.CheckOverdue(p, 7, now)); // nie gesichert, Profil älter als 7 Tage
        Assert.Null(BackupPlanner.CheckOverdue(new BackupProfile { CreatedUtc = now.AddDays(-1) }, 7, now));
    }

    [Fact]
    public void Sample_is_spread_and_bounded()
    {
        var items = Enumerable.Range(0, 100).ToList();
        var s = BackupPlanner.Sample(items, 10);
        Assert.Equal(10, s.Count);
        Assert.Equal(0, s[0]);
        Assert.Equal(90, s[^1]);
        Assert.Equal(3, BackupPlanner.Sample(new[] { 1, 2, 3 }, 10).Count);
        Assert.Empty(BackupPlanner.Sample(items, 0));
    }
}

public class WbadminParserTests
{
    private const string English = """
        wbadmin 1.0 - Backup command-line tool
        (C) Copyright Microsoft Corporation. All rights reserved.

        Retrieving volume information...
        This will back up (EFI System Partition) (100.00 MB),Windows(C:) to E:.
        The backup operation to E: is starting.
        Creating a shadow copy of the volumes specified for backup...
        Creating a backup of volume (EFI System Partition) (100.00 MB), copied (100%).
        The backup of volume (EFI System Partition) (100.00 MB) completed successfully.
        Creating a backup of volume Windows(C:), copied (0%).
        Creating a backup of volume Windows(C:), copied (37%).
        Creating a backup of volume Windows(C:), copied (99%).
        The backup of volume Windows(C:) completed successfully.
        Summary of the backup operation:
        ------------------

        The backup operation successfully completed.
        The backup of volume (EFI System Partition) (100.00 MB) completed successfully.
        The backup of volume Windows(C:) completed successfully.
        """;

    private const string German = """
        Volumeinformationen werden abgerufen...
        Hierbei wird (EFI-Systempartition) (100.00 MB),Windows(C:) auf E: gesichert.
        Der Sicherungsvorgang auf E: wird gestartet.
        Schattenkopie der für die Sicherung angegebenen Volumes wird erstellt...
        Sicherung von Volume Windows(C:) wird erstellt, 12 % kopiert.
        Sicherung von Volume Windows(C:) wird erstellt, 64,5 % kopiert.
        Die Sicherung von Volume Windows(C:) wurde erfolgreich abgeschlossen.
        Zusammenfassung des Sicherungsvorgangs:
        Der Sicherungsvorgang wurde erfolgreich abgeschlossen.
        Die Sicherung von Volume Windows(C:) wurde erfolgreich abgeschlossen.
        """;

    [Fact]
    public void English_progress_lines()
    {
        var p = WbadminProgressParser.ParseLine("Creating a backup of volume Windows(C:), copied (37%).");
        Assert.NotNull(p);
        Assert.Equal(WbadminPhase.Copying, p!.Phase);
        Assert.Equal(37, p.Percent);
        Assert.Equal("Windows(C:)", p.Volume);
        var efi = WbadminProgressParser.ParseLine("Creating a backup of volume (EFI System Partition) (100.00 MB), copied (100%).");
        Assert.Equal(100, efi!.Percent);
        Assert.Equal("(EFI System Partition) (100.00 MB)", efi.Volume);
        Assert.Equal(WbadminPhase.ShadowCopy, WbadminProgressParser.ParseLine("Creating a shadow copy of the volumes specified for backup...")!.Phase);
        Assert.Null(WbadminProgressParser.ParseLine("Retrieving volume information..."));
        Assert.Null(WbadminProgressParser.ParseLine("This will back up (EFI System Partition) (100.00 MB),Windows(C:) to E:."));
    }

    [Fact]
    public void German_progress_lines()
    {
        var p = WbadminProgressParser.ParseLine("Sicherung von Volume Windows(C:) wird erstellt, 12 % kopiert.");
        Assert.Equal(WbadminPhase.Copying, p!.Phase);
        Assert.Equal(12, p.Percent);
        Assert.Equal("Windows(C:)", p.Volume);
        Assert.Equal(64.5, WbadminProgressParser.ParseLine("Sicherung von Volume Windows(C:) wird erstellt, 64,5 % kopiert.")!.Percent);
        Assert.Equal(WbadminPhase.ShadowCopy, WbadminProgressParser.ParseLine("Schattenkopie der für die Sicherung angegebenen Volumes wird erstellt...")!.Phase);
        Assert.Equal(WbadminPhase.Completed, WbadminProgressParser.ParseLine("Der Sicherungsvorgang wurde erfolgreich abgeschlossen.")!.Phase);
    }

    [Theory]
    [InlineData(English, 3)]
    [InlineData(German, 5)]
    [InlineData(English, 64)]
    [InlineData(German, 1)]
    public void Chunked_output_ends_completed(string output, int chunkSize)
    {
        var parser = new WbadminProgressParser();
        double? maxSeen = null;
        var text = output.Replace("\n", "\r\n", StringComparison.Ordinal);
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            var s = parser.Feed(text.Substring(i, Math.Min(chunkSize, text.Length - i)));
            if (s.Phase == WbadminPhase.Copying && s.Percent is { } pc) maxSeen = Math.Max(maxSeen ?? 0, pc);
        }
        var final = parser.Flush();
        Assert.Equal(WbadminPhase.Completed, final.Phase); // bleibt trotz nachfolgender Volume-Zeilen „abgeschlossen“
        Assert.Equal(100, final.Percent);
        Assert.NotNull(maxSeen);
    }

    [Fact]
    public void Intermediate_state_and_failure()
    {
        var parser = new WbadminProgressParser();
        parser.Feed("Creating a shadow copy of the volumes specified for backup...\r\n");
        Assert.Equal(WbadminPhase.ShadowCopy, parser.Current.Phase);
        parser.Feed("Creating a backup of volume Windows(C:), copied (41%).\r\n");
        Assert.Equal(41, parser.Current.Percent);
        parser.Feed("ERROR - A Volume Shadow Copy Service operation failed.\r\n");
        Assert.Equal(WbadminPhase.Failed, parser.Current.Phase);
        parser.Feed("The backup operation successfully completed.\r\n");
        Assert.Equal(WbadminPhase.Failed, parser.Current.Phase); // Fehler bleibt stehen

        var de = new WbadminProgressParser();
        de.Feed("Der Sicherungsvorgang ist fehlgeschlagen.\n");
        Assert.Equal(WbadminPhase.Failed, de.Current.Phase);
    }

    [Fact]
    public void Version_times_are_listed()
    {
        const string output = "Backup time: 10/14/2026 3:30 PM\r\nBackup target: 1394/USB Disk labeled E:\r\nVersion identifier: 10/14/2026-13:30\r\n\r\nSicherungszeit: 07.10.2026 18:00\r\n";
        Assert.Equal(new[] { "10/14/2026 3:30 PM", "07.10.2026 18:00" }, WbadminProgressParser.ParseVersionTimes(output).ToArray());
    }
}

public class SystemImageTargetTests
{
    private const long Gb = 1024L * 1024 * 1024;

    [Fact]
    public void Target_rules()
    {
        var used = 100 * Gb;
        Assert.Equal(110 * Gb, SystemImageTargetCheck.RequiredBytes(used));
        Assert.False(SystemImageTargetCheck.Evaluate(null, 'C', used).Ok);
        Assert.False(SystemImageTargetCheck.Evaluate(new('C', "", DriveType.Fixed, "NTFS", true, 500 * Gb, 400 * Gb), 'C', used).Ok);
        Assert.False(SystemImageTargetCheck.Evaluate(new('E', "", DriveType.Fixed, "NTFS", false, 0, 0), 'C', used).Ok);
        Assert.False(SystemImageTargetCheck.Evaluate(new('E', "", DriveType.Network, "NTFS", true, 500 * Gb, 400 * Gb), 'C', used).Ok);
        Assert.False(SystemImageTargetCheck.Evaluate(new('E', "", DriveType.Removable, "exFAT", true, 500 * Gb, 400 * Gb), 'C', used).Ok);
        Assert.False(SystemImageTargetCheck.Evaluate(new('E', "", DriveType.Fixed, "NTFS", true, 500 * Gb, 109 * Gb), 'C', used).Ok);
        Assert.True(SystemImageTargetCheck.Evaluate(new('E', "Backup", DriveType.Fixed, "NTFS", true, 500 * Gb, 110 * Gb), 'C', used).Ok);
        Assert.True(SystemImageTargetCheck.Evaluate(new('e', "Backup", DriveType.Removable, "ntfs", true, 500 * Gb, 300 * Gb), 'C', used).Ok);
    }
}

public class RestoreConflictTests
{
    private static readonly DateTime Stamp = new(2026, 10, 14, 15, 30, 0);

    [Fact]
    public void Existing_files_are_never_overwritten_silently()
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\a.txt", @"D:\a (wiederhergestellt 2026-10-14 153000).txt" };
        bool Exists(string p) => existing.Contains(p);

        Assert.Equal(RestoreAction.Write, RestoreConflicts.Decide(@"D:\new.txt", RestoreConflictMode.Replace, Exists, Stamp).Action);
        Assert.Equal(RestoreAction.Skip, RestoreConflicts.Decide(@"D:\a.txt", RestoreConflictMode.KeepExisting, Exists, Stamp).Action);
        Assert.Equal(RestoreAction.ReplaceExisting, RestoreConflicts.Decide(@"D:\a.txt", RestoreConflictMode.Replace, Exists, Stamp).Action);
        var both = RestoreConflicts.Decide(@"D:\a.txt", RestoreConflictMode.KeepBoth, Exists, Stamp);
        Assert.Equal(RestoreAction.WriteAsCopy, both.Action);
        Assert.Equal(@"D:\a (wiederhergestellt 2026-10-14 153000 2).txt", both.DestinationPath);
    }
}

public class BackupCommandTests
{
    private static string Sys => CommandCatalog.SystemDriveLetter.ToString();

    [Fact]
    public void System_image_command_is_fixed_and_targets_other_drive()
    {
        var target = CommandSamples.NonSystemDrive();
        var spec = CommandCatalog.Build(AllowedCommand.BackupSystemImage, target);
        Assert.True(spec.RequiresElevation);
        Assert.False(spec.IsInteractive);
        Assert.EndsWith("wbadmin.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "start", "backup", $"-backupTarget:{target}:", $"-include:{Sys}:", "-allCritical", "-quiet" }, spec.Arguments.ToArray());
        Assert.Equal($"-backupTarget:{target}:", CommandCatalog.Build(AllowedCommand.BackupSystemImage, target.ToLowerInvariant() + ":\\").Arguments[2]);
    }

    [Fact]
    public void System_drive_is_rejected_as_image_target()
    {
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupSystemImage, Sys));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupSystemImage, Sys.ToLowerInvariant() + ":"));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupSystemImage, Sys + ":\\"));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupSystemImage, null));
    }

    [Theory]
    [InlineData("Z & calc")]
    [InlineData("Z:\\..\\")]
    [InlineData("-quiet")]
    [InlineData("Z Z")]
    [InlineData("ZZ")]
    [InlineData("1")]
    [InlineData("Z;")]
    [InlineData("\\\\server\\share")]
    [InlineData("")]
    public void Injection_attempts_are_rejected(string parameter)
    {
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupSystemImage, parameter));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.BackupImageVersions, parameter));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.DriverExport, parameter));
    }

    [Fact]
    public void Image_versions_with_and_without_target()
    {
        Assert.Equal(new[] { "get", "versions" }, CommandCatalog.Build(AllowedCommand.BackupImageVersions).Arguments.ToArray());
        Assert.Equal(new[] { "get", "versions", "-backupTarget:Z:" }, CommandCatalog.Build(AllowedCommand.BackupImageVersions, "z").Arguments.ToArray());
    }

    [Fact]
    public void Recovery_drive_is_interactive_elevated_and_takes_no_parameter()
    {
        var spec = CommandCatalog.Build(AllowedCommand.RecoveryDriveCreate);
        Assert.True(spec.IsInteractive);
        Assert.True(spec.RequiresElevation);
        Assert.Empty(spec.Arguments);
        Assert.EndsWith("RecoveryDrive.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.RecoveryDriveCreate, "E"));
    }

    [Fact]
    public void Driver_export_uses_fixed_existing_folder_from_token()
    {
        var root = CommandSamples.EnsureDriverExportTestRoot();
        var token = CommandSamples.DriverSampleToken;
        Assert.True(CommandCatalog.IsSafeToken(token));
        var spec = CommandCatalog.Build(AllowedCommand.DriverExport, token);
        var expected = Path.Combine(root, "UltimateX-Backup", "Treiber", CommandSamples.DriverSampleStamp);
        Assert.Equal(new[] { "/export-driver", "*", expected }, spec.Arguments.ToArray());
        Assert.EndsWith("pnputil.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(spec.RequiresElevation);

        // Ordner fehlt → abgelehnt (pnputil legt ihn nicht an; der Helper legt keine Ordner an)
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.DriverExport, "Z-20250101-120000"));
    }

    [Theory]
    [InlineData("Z-2026-01-01")]
    [InlineData("Z-20261301-120000")]
    [InlineData("Z-20260101-250000")]
    [InlineData("ZZ-20260101-120000")]
    [InlineData("Z-20260101-120000;calc")]
    [InlineData("Z-20260101-120000\\..\\..\\Windows")]
    [InlineData("1-20260101-120000")]
    public void Invalid_driver_tokens_are_rejected(string token)
        => Assert.Throws<ArgumentException>(() => CommandCatalog.ResolveDriverExportFolder(token));

    [Fact]
    public void Driver_token_format()
    {
        Assert.Equal("E-20261014-153000", CommandCatalog.MakeDriverExportToken('e', new DateTime(2026, 10, 14, 15, 30, 0)));
        Assert.Throws<ArgumentException>(() => CommandCatalog.MakeDriverExportToken('1', DateTime.Now));
    }

    [Fact]
    public void Reparse_free_chain_requires_path_inside_root()
    {
        using var tmp = new BackupTempDir();
        var inner = tmp.Sub("a", "b");
        Assert.True(CommandCatalog.IsReparseFreeChain(tmp.Path, inner));
        Assert.True(CommandCatalog.IsReparseFreeChain(tmp.Path, Path.Combine(inner, "noch-nicht-da")));
        Assert.False(CommandCatalog.IsReparseFreeChain(inner, tmp.Path));
        Assert.False(CommandCatalog.IsReparseFreeChain(tmp.Sub("x"), Path.Combine(tmp.Path, "xy")));
    }
}

public sealed class FileBackupEngineTests : IDisposable
{
    private readonly BackupTempDir _tmp = new();
    private readonly string _src;
    private readonly string _target;
    private readonly string _bin;
    private int _tick;

    public FileBackupEngineTests()
    {
        _src = _tmp.Sub("src");
        _target = _tmp.Sub("disk");
        _bin = _tmp.Sub("papierkorb");
        var t = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        BackupTempDir.Write(Path.Combine(_src, "a.txt"), "Version 1", t);
        BackupTempDir.Write(Path.Combine(_src, "sub", "b.txt"), "Bravo", t);
        BackupTempDir.Write(Path.Combine(_src, ".git", "objects", "ab", "cdef"), "git object", t);
        BackupTempDir.Write(Path.Combine(_src, "bin", "Debug", "app.dll"), "build output", t);
        BackupTempDir.Write(Path.Combine(_src, "web", "node_modules", "x", "index.js"), "npm", t);
        BackupTempDir.Write(Path.Combine(_src, "~$Bericht.docx"), "office lock", t);
    }

    public void Dispose() => _tmp.Dispose();

    private FileBackupEngine Engine(Func<string, string, int>? linker = null)
        => new(linker, p => File.Move(p, Path.Combine(_bin, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(p))))
        {
            Clock = () => new DateTime(2026, 10, 14, 12, 0, 0).AddMinutes(_tick++),
        };

    private BackupProfile Profile(int keep = 2) => new()
    {
        Name = "Test",
        FolderName = "Test",
        Sources = [new BackupSource { Path = _src }],
        Exclusions = [.. BackupTemplates.ProgrammingExclusions],
        KeepVersions = keep,
    };

    private string ProfileRoot => BackupPaths.ProfileRoot(_target, "Test");

    private static string Stored(BackupRunResult r, string sourceFile) => Path.Combine(r.VersionPath!, BackupPaths.ToStoreRelative(sourceFile));

    [Fact]
    public void Incremental_runs_link_unchanged_files_and_apply_retention()
    {
        var engine = Engine();
        Directory.CreateDirectory(Path.Combine(ProfileRoot, "Notizen"));
        Directory.CreateDirectory(Path.Combine(ProfileRoot, "2020-01-01_000000")); // gültiger Name, aber kein Manifest → nie löschen

        var r1 = engine.Run(Profile(), _target, new BackupRunOptions());
        Assert.True(r1.Success, r1.Message);
        Assert.Equal(BackupVersionStatus.Completed, r1.Status);
        Assert.Equal(3, r1.Files); // a.txt, sub\b.txt, .git\objects\ab\cdef – bin, node_modules, ~$ ausgeschlossen
        Assert.Equal(3, r1.Copied);
        Assert.Equal(0, r1.Linked);
        Assert.True(File.Exists(Stored(r1, Path.Combine(_src, ".git", "objects", "ab", "cdef"))));
        Assert.False(File.Exists(Stored(r1, Path.Combine(_src, "bin", "Debug", "app.dll"))));
        Assert.Equal("Version 1", File.ReadAllText(Stored(r1, Path.Combine(_src, "a.txt"))));
        var m1 = FileBackupEngine.LoadManifest(r1.VersionPath!);
        Assert.NotNull(m1);
        Assert.All(m1!.Files, f => Assert.Equal(64, f.Sha256?.Length));
        Assert.Contains("3 von 3", r1.VerifyResult, StringComparison.Ordinal);

        var r2 = engine.Run(Profile(), _target, new BackupRunOptions());
        Assert.True(r2.Success, r2.Message);
        Assert.Equal(3, r2.Linked + r2.LinkFallbacks);
        Assert.Equal("Bravo", File.ReadAllText(Stored(r2, Path.Combine(_src, "sub", "b.txt"))));

        BackupTempDir.Write(Path.Combine(_src, "a.txt"), "Version 2 – geändert", new DateTime(2026, 10, 2, 8, 0, 0, DateTimeKind.Utc));
        var r3 = engine.Run(Profile(), _target, new BackupRunOptions());
        Assert.True(r3.Success, r3.Message);
        Assert.Equal(1, r3.Copied - r3.LinkFallbacks);
        Assert.Equal("Version 2 – geändert", File.ReadAllText(Stored(r3, Path.Combine(_src, "a.txt"))));

        // Aufbewahrung 2: erste Version gelöscht, Hardlink-Daten der übrigen Versionen intakt
        Assert.Contains(Path.GetFileName(r1.VersionPath!), r3.DeletedVersions);
        Assert.False(Directory.Exists(r1.VersionPath));
        Assert.Equal("Version 1", File.ReadAllText(Stored(r2, Path.Combine(_src, "a.txt"))));
        Assert.Equal("Bravo", File.ReadAllText(Stored(r3, Path.Combine(_src, "sub", "b.txt"))));
        // Fremde Ordner bleiben
        Assert.True(Directory.Exists(Path.Combine(ProfileRoot, "Notizen")));
        Assert.True(Directory.Exists(Path.Combine(ProfileRoot, "2020-01-01_000000")));
        // Quelle unverändert
        Assert.True(File.Exists(Path.Combine(_src, "bin", "Debug", "app.dll")));
        Assert.Equal(2, FileBackupEngine.ListVersions(ProfileRoot).Count(v => v.IsComplete));
    }

    [Fact]
    public void Hardlink_failure_falls_back_to_copy()
    {
        var engine = Engine(linker: (_, _) => 17); // ERROR_NOT_SAME_DEVICE
        Assert.True(engine.Run(Profile(5), _target, new BackupRunOptions()).Success);
        var r = engine.Run(Profile(5), _target, new BackupRunOptions());
        Assert.True(r.Success, r.Message);
        Assert.Equal(0, r.Linked);
        Assert.Equal(3, r.LinkFallbacks);
        Assert.Equal("Bravo", File.ReadAllText(Stored(r, Path.Combine(_src, "sub", "b.txt"))));
        Assert.All(FileBackupEngine.LoadManifest(r.VersionPath!)!.Files, f => Assert.False(f.Linked));
    }

    [Fact]
    public void Locked_files_are_skipped_and_reported()
    {
        var locked = Path.Combine(_src, "outlook.pst");
        BackupTempDir.Write(locked, "pst");
        BackupRunResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            r = Engine().Run(Profile(), _target, new BackupRunOptions());
        Assert.True(r.Success, r.Message);
        Assert.Equal(BackupVersionStatus.CompletedWithWarnings, r.Status);
        var skipped = Assert.Single(r.Skipped);
        Assert.Equal(locked, skipped.Path);
        Assert.Contains("Gesperrt", skipped.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_inside_source_and_cancellation_are_handled()
    {
        var bad = Engine().Run(Profile(), Path.Combine(_src, "sub"), new BackupRunOptions());
        Assert.False(bad.Success);
        Assert.Contains("innerhalb der Quelle", bad.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_src, "sub", BackupPaths.RootFolderName)));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var cancelled = Engine().Run(Profile(), _target, new BackupRunOptions(), null, cts.Token);
        Assert.False(cancelled.Success);
        Assert.Equal(BackupVersionStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public void Delete_version_refuses_anything_outside_own_backup_folders()
    {
        var foreign = _tmp.Sub("Dokumente", "2026-10-14_120000");
        File.WriteAllText(Path.Combine(foreign, BackupPaths.ManifestFileName), "{}");
        Assert.False(FileBackupEngine.DeleteVersion(Path.GetDirectoryName(foreign)!, "2026-10-14_120000").Success);
        Assert.True(Directory.Exists(foreign));
        Assert.False(FileBackupEngine.DeleteVersion(ProfileRoot, @"..\..\src").Success);
        Assert.False(FileBackupEngine.DeleteVersion(ProfileRoot, "Notizen").Success);
        Assert.True(Directory.Exists(_src));
    }

    [Fact]
    public void Restore_handles_conflicts_keep_replace_both_and_original_location()
    {
        var engine = Engine();
        var run = engine.Run(Profile(), _target, new BackupRunOptions());
        Assert.True(run.Success, run.Message);
        var manifest = FileBackupEngine.LoadManifest(run.VersionPath!)!;
        var prefix = BackupPaths.ToStoreRelative(_src);
        var entries = FileBackupEngine.SelectEntries(manifest, prefix);
        Assert.Equal(3, entries.Count);
        var dest = _tmp.Sub("restore");

        // 1) Erstmalig: alles geschrieben
        var first = engine.Restore(new RestoreRequest(run.VersionPath!, entries, prefix, dest, RestoreConflictMode.KeepExisting));
        Assert.Equal(3, first.Restored);
        var restoredA = Path.Combine(dest, "src", "a.txt");
        Assert.Equal("Version 1", File.ReadAllText(restoredA));

        // 2) Vorschau zählt Konflikte; „behalten“ überschreibt nichts
        File.WriteAllText(restoredA, "lokal geändert");
        var preview = FileBackupEngine.Preview(new RestoreRequest(run.VersionPath!, entries, prefix, dest, RestoreConflictMode.KeepExisting));
        Assert.Equal(3, preview.Conflicts);
        var keep = engine.Restore(new RestoreRequest(run.VersionPath!, entries, prefix, dest, RestoreConflictMode.KeepExisting));
        Assert.Equal(3, keep.SkippedExisting);
        Assert.Equal(0, keep.Restored);
        Assert.Equal("lokal geändert", File.ReadAllText(restoredA));

        // 3) „beide behalten“: Sicherung unter neuem Namen
        var both = engine.Restore(new RestoreRequest(run.VersionPath!, entries, prefix, dest, RestoreConflictMode.KeepBoth));
        Assert.Equal(3, both.WrittenAsCopy);
        Assert.Equal("lokal geändert", File.ReadAllText(restoredA));
        var copy = Assert.Single(Directory.GetFiles(Path.Combine(dest, "src"), "a (wiederhergestellt *).txt"));
        Assert.Equal("Version 1", File.ReadAllText(copy));

        // 4) „ersetzen“: vorhandene Datei landet im (Test-)Papierkorb
        var replace = engine.Restore(new RestoreRequest(run.VersionPath!, entries, prefix, dest, RestoreConflictMode.Replace));
        Assert.Equal(3, replace.Replaced);
        Assert.Equal("Version 1", File.ReadAllText(restoredA));
        Assert.Contains(Directory.GetFiles(_bin), f => File.ReadAllText(f) == "lokal geändert");

        // 5) Originalort: gelöschte Quelldatei zurückholen
        var srcA = Path.Combine(_src, "a.txt");
        File.Delete(srcA);
        var single = BackupPaths.ToStoreRelative(srcA);
        var orig = engine.Restore(new RestoreRequest(run.VersionPath!, FileBackupEngine.SelectEntries(manifest, single), single, null, RestoreConflictMode.KeepExisting));
        Assert.Equal(1, orig.Restored);
        Assert.Equal("Version 1", File.ReadAllText(srcA));
    }

    [Fact]
    public void Restore_blocks_protected_system_folders()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var store = BackupPaths.ToStoreRelative(Path.Combine(windows, "System32", "evil.dll"));
        var entry = new BackupManifestEntry { Path = store, Size = 1, LastWriteUtc = DateTime.UtcNow };
        var preview = FileBackupEngine.Preview(new RestoreRequest(_tmp.Path, [entry], store, null, RestoreConflictMode.Replace));
        Assert.Equal(0, preview.Files);
        Assert.Equal(1, preview.Blocked);
    }
}

public sealed class BackupServiceTests : IDisposable
{
    private readonly BackupTempDir _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private BackupService NewService(string name = "a")
        => new(Path.Combine(_tmp.Path, name + "-profiles.json"), Path.Combine(_tmp.Path, name + "-history.json"),
            new FileBackupEngine(null, _ => { }));

    [Fact]
    public void Profiles_persist_validate_and_export_import()
    {
        var src = _tmp.Sub("src");
        BackupTempDir.Write(Path.Combine(src, "x.txt"), "x");
        var target = _tmp.Sub("disk");
        var svc = NewService();
        var p = new BackupProfile { Name = "Test/Profil: 1", Sources = [new BackupSource { Path = src }], TargetPath = target, KeepVersions = 3 };
        Assert.True(svc.SaveProfile(p).Success);

        var loaded = Assert.Single(NewService().GetProfiles());
        Assert.Equal("Test/Profil: 1", loaded.Name);
        Assert.True(BackupPaths.IsValidFolderName(loaded.FolderName));

        // Ziel innerhalb der Quelle, ungültige Aufbewahrung, Netzwerkpfade → abgelehnt
        Assert.False(svc.SaveProfile(new BackupProfile { Name = "x", Sources = [new BackupSource { Path = src }], TargetPath = Path.Combine(src, "bk") }).Success);
        Assert.False(svc.SaveProfile(new BackupProfile { Name = "x", Sources = [new BackupSource { Path = src }], TargetPath = target, KeepVersions = 0 }).Success);
        Assert.False(svc.SaveProfile(new BackupProfile { Name = "x", Sources = [new BackupSource { Path = @"\\server\share" }], TargetPath = target }).Success);
        Assert.False(svc.SaveProfile(new BackupProfile { Name = "x", Templates = ["unbekannt"], TargetPath = target }).Success);
        // Ziel darf beim Speichern fehlen (Platte nicht angesteckt)
        Assert.True(svc.SaveProfile(new BackupProfile { Name = "Später", Sources = [new BackupSource { Path = src }], TargetPath = Path.Combine(_tmp.Path, "fehlt") }).Success);

        var export = Path.Combine(_tmp.Path, "export.json");
        Assert.True(svc.Export(export).Success);
        var other = NewService("b");
        Assert.True(other.Import(export).Success);
        Assert.Equal(2, other.GetProfiles().Count);
        Assert.All(other.GetProfiles(), x => Assert.Null(x.LastSuccessUtc));
    }

    [Fact]
    public async Task Run_marks_target_records_history_and_detects_moved_drive()
    {
        var src = _tmp.Sub("src");
        BackupTempDir.Write(Path.Combine(src, "x.txt"), "x");
        var target = _tmp.Sub("diskA");
        var svc = NewService();
        var p = new BackupProfile { Name = "Daten", Sources = [new BackupSource { Path = src }], TargetPath = target };
        Assert.True(svc.SaveProfile(p).Success);

        var r = await svc.RunProfileAsync(p.Id, null, CancellationToken.None);
        Assert.True(r.Success, r.Message);
        var marker = BackupService.ReadMarker(target);
        Assert.NotNull(marker);
        var stored = svc.GetProfile(p.Id)!;
        Assert.Equal(marker!.Id, stored.TargetId);
        Assert.NotNull(stored.LastSuccessUtc);
        Assert.Contains(svc.GetHistory(), h => h.Kind == "Datei-Backup" && h.Success);
        Assert.Empty(svc.GetOverdueProfiles());

        // Platte unter anderem Laufwerksbuchstaben: Markierung unter gleichem relativen Pfad auf einer anderen „Wurzel“
        var movedRoot = _tmp.Sub("moved");
        var relative = target[3..];
        var movedTarget = Path.Combine(movedRoot, relative);
        Directory.CreateDirectory(BackupPaths.BackupRoot(movedTarget));
        File.Copy(BackupPaths.MarkerPath(target), BackupPaths.MarkerPath(movedTarget));
        Directory.Delete(target, true);
        var resolved = svc.ResolveTarget(stored, [movedRoot]);
        Assert.True(resolved.Available);
        Assert.Equal(movedTarget, resolved.CurrentPath);

        // Andere Platte (fremde Markierung) am ursprünglichen Pfad → nicht verwenden
        Directory.CreateDirectory(target);
        BackupService.EnsureMarker(target);
        var foreign = svc.ResolveTarget(stored, []);
        Assert.False(foreign.Available);
    }

    [Fact]
    public void Corrupt_marker_is_ignored()
    {
        var target = _tmp.Sub("disk");
        Directory.CreateDirectory(BackupPaths.BackupRoot(target));
        File.WriteAllText(BackupPaths.MarkerPath(target), "{\"Id\":\"..\\\\..\\\\evil\"}");
        Assert.Null(BackupService.ReadMarker(target));
        File.WriteAllText(BackupPaths.MarkerPath(target), "kein json");
        Assert.Null(BackupService.ReadMarker(target));
    }
}
