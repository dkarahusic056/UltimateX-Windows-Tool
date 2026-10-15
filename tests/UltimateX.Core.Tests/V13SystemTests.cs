using UltimateX.Core.Lifetime;
using UltimateX.Core.Power;
using UltimateX.Core.Security;
using UltimateX.Core.SpaceAnalysis;
using UltimateX.Core.Timeline;
using UltimateX.Services.SystemTools;

namespace UltimateX.Core.Tests;

// ======================================================================== v1.3 Zeitgesteuerte Energieaktionen

public class V13PowerScheduleTests
{
    /// <summary>Mitteleuropäische Zeit mit Sommerzeit (letzter So im März 02:00 → 03:00, letzter So im Oktober 03:00 → 02:00).</summary>
    private static TimeZoneInfo Cet()
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2000, 1, 1), new DateTime(2099, 12, 31), TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("Test/CET", TimeSpan.FromHours(1), "Test CET", "CET", "CEST", [rule]);
    }

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("22:30", true, 22, 30)]
    [InlineData("7:05", true, 7, 5)]
    [InlineData("00:00", true, 0, 0)]
    [InlineData("23:59", true, 23, 59)]
    [InlineData("24:00", false, 0, 0)]
    [InlineData("22:3", false, 0, 0)]
    [InlineData(" 22:30", false, 0, 0)]
    [InlineData("22:30:00", false, 0, 0)]
    [InlineData("22.30", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void Clock_parsing_is_strict(string? text, bool ok, int h, int m)
    {
        Assert.Equal(ok, PowerScheduleMath.TryParseClock(text, out var hour, out var minute));
        if (ok) { Assert.Equal(h, hour); Assert.Equal(m, minute); }
    }

    [Theory]
    [InlineData(30, PowerDelayUnit.Minutes, true, 1800)]
    [InlineData(1, PowerDelayUnit.Minutes, true, 60)]
    [InlineData(10080, PowerDelayUnit.Minutes, true, 604800)]
    [InlineData(168, PowerDelayUnit.Hours, true, 604800)]
    [InlineData(2, PowerDelayUnit.Hours, true, 7200)]
    [InlineData(0, PowerDelayUnit.Minutes, false, 0)]
    [InlineData(-5, PowerDelayUnit.Minutes, false, 0)]
    [InlineData(10081, PowerDelayUnit.Minutes, false, 0)]
    [InlineData(169, PowerDelayUnit.Hours, false, 0)]
    public void Delay_is_limited_to_seven_days(int amount, PowerDelayUnit unit, bool ok, long seconds)
    {
        Assert.Equal(ok, PowerScheduleMath.TryDelaySeconds(amount, unit, out var s));
        if (ok) Assert.Equal(seconds, s);
    }

    [Fact]
    public void Clock_after_midnight_tomorrow_resolves_to_next_day()
    {
        // 23:00 Sommerzeit (21:00 UTC) → „morgen 00:30“ = 22:30 UTC, 1,5 h später.
        var now = Utc(2026, 6, 10, 21);
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 0, 30, true, out var due, out var err), err);
        Assert.Equal(Utc(2026, 6, 10, 22, 30), due);
        Assert.True(PowerScheduleMath.TrySecondsUntil(due, now, out var s));
        Assert.Equal(5400, s);
    }

    [Fact]
    public void Clock_today_in_the_past_is_rejected_not_shifted()
    {
        var now = Utc(2026, 6, 10, 21); // 23:00 lokal
        Assert.False(PowerScheduleMath.TryResolveClock(now, Cet(), 22, 0, false, out _, out var err));
        Assert.NotNull(err);
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 23, 30, false, out var due, out _));
        Assert.Equal(Utc(2026, 6, 10, 21, 30), due);
    }

    [Fact]
    public void Spring_forward_gap_uses_first_valid_time()
    {
        // 29.03.2026: 02:00 → 03:00. „morgen 02:30“ existiert nicht → 03:00 CEST = 01:00 UTC.
        var now = Utc(2026, 3, 28, 12);
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 2, 30, true, out var due, out var err), err);
        Assert.Equal(Utc(2026, 3, 29, 1), due);
    }

    [Fact]
    public void Spring_forward_night_is_one_hour_shorter()
    {
        // 23:00 CET am 28.03. → „morgen 08:00“ CEST: nur 8 statt 9 Stunden.
        var now = Utc(2026, 3, 28, 22);
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 8, 0, true, out var due, out _));
        Assert.True(PowerScheduleMath.TrySecondsUntil(due, now, out var s));
        Assert.Equal(8 * 3600, s);
    }

    [Fact]
    public void Fall_back_ambiguous_time_uses_first_occurrence()
    {
        // 25.10.2026: 03:00 → 02:00. „02:30“ gibt es zweimal → erstes Auftreten (CEST, UTC+2) = 00:30 UTC.
        var now = Utc(2026, 10, 24, 12);
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 2, 30, true, out var due, out _));
        Assert.Equal(Utc(2026, 10, 25, 0, 30), due);
    }

    [Fact]
    public void Fall_back_night_is_one_hour_longer()
    {
        var now = Utc(2026, 10, 24, 21); // 23:00 CEST
        Assert.True(PowerScheduleMath.TryResolveClock(now, Cet(), 8, 0, true, out var due, out _)); // 08:00 CET = 07:00 UTC
        Assert.Equal(Utc(2026, 10, 25, 7), due);
        Assert.True(PowerScheduleMath.TrySecondsUntil(due, now, out var s));
        Assert.Equal(10 * 3600, s);
    }

    [Fact]
    public void Seconds_until_rounds_up_and_rejects_more_than_seven_days()
    {
        var now = Utc(2026, 1, 1, 0);
        Assert.True(PowerScheduleMath.TrySecondsUntil(now.AddSeconds(10.2), now, out var s));
        Assert.Equal(11, s);
        Assert.True(PowerScheduleMath.TrySecondsUntil(now.AddSeconds(-5), now, out s));
        Assert.Equal(0, s);
        Assert.True(PowerScheduleMath.TrySecondsUntil(now.AddDays(7), now, out s));
        Assert.Equal(604800, s);
        Assert.False(PowerScheduleMath.TrySecondsUntil(now.AddDays(7).AddSeconds(1), now, out _));
    }

    [Fact]
    public void Postpone_adds_to_due_time_or_to_now_when_overdue()
    {
        var now = Utc(2026, 1, 1, 12);
        Assert.True(PowerScheduleMath.TryPostpone(now.AddMinutes(3), TimeSpan.FromMinutes(15), now, out var d1));
        Assert.Equal(now.AddMinutes(18), d1);
        Assert.True(PowerScheduleMath.TryPostpone(now.AddMinutes(-1), TimeSpan.FromHours(1), now, out var d2));
        Assert.Equal(now.AddHours(1), d2);
        Assert.False(PowerScheduleMath.TryPostpone(now.AddDays(7), TimeSpan.FromHours(1), now, out _));
        Assert.False(PowerScheduleMath.TryPostpone(now.AddHours(1), TimeSpan.Zero, now, out _));
    }

    [Fact]
    public void Warnings_fire_once_at_five_and_one_minute()
    {
        Assert.Equal(PowerWarningStage.None, PowerScheduleMath.WarningDue(TimeSpan.FromMinutes(6), false, false));
        Assert.Equal(PowerWarningStage.FiveMinutes, PowerScheduleMath.WarningDue(TimeSpan.FromMinutes(5), false, false));
        Assert.Equal(PowerWarningStage.None, PowerScheduleMath.WarningDue(TimeSpan.FromMinutes(4), true, false));
        Assert.Equal(PowerWarningStage.OneMinute, PowerScheduleMath.WarningDue(TimeSpan.FromSeconds(59), true, false));
        Assert.Equal(PowerWarningStage.OneMinute, PowerScheduleMath.WarningDue(TimeSpan.FromSeconds(30), false, false)); // 5-min-Warnung verpasst
        Assert.Equal(PowerWarningStage.None, PowerScheduleMath.WarningDue(TimeSpan.FromSeconds(30), true, true));
        Assert.Equal(PowerWarningStage.None, PowerScheduleMath.WarningDue(TimeSpan.Zero, false, false));

        Assert.Equal((true, false), PowerScheduleMath.InitialWarnings(TimeSpan.FromMinutes(3)));
        Assert.Equal((false, false), PowerScheduleMath.InitialWarnings(TimeSpan.FromMinutes(30)));
        Assert.Equal((true, true), PowerScheduleMath.InitialWarnings(TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void Remaining_time_format_counts_hours_beyond_a_day()
    {
        Assert.Equal("00:00:00", PowerScheduleMath.FormatRemaining(TimeSpan.FromSeconds(-3)));
        Assert.Equal("01:02:03", PowerScheduleMath.FormatRemaining(new TimeSpan(1, 2, 3)));
        Assert.Equal("49:05:00", PowerScheduleMath.FormatRemaining(new TimeSpan(2, 1, 5, 0)));
    }

    [Fact]
    public void Expired_entries_are_not_active()
    {
        var now = Utc(2026, 1, 1, 12);
        Assert.True(PowerScheduleMath.IsStillActive(new PowerScheduleEntry(Guid.NewGuid(), ScheduledPowerAction.Lock, now.AddMinutes(1), now, false), now));
        Assert.False(PowerScheduleMath.IsStillActive(new PowerScheduleEntry(Guid.NewGuid(), ScheduledPowerAction.Shutdown, now.AddMinutes(-1), now, true), now));
        Assert.True(PowerScheduleEntry.UsesShutdownExe(ScheduledPowerAction.Restart));
        Assert.False(PowerScheduleEntry.UsesShutdownExe(ScheduledPowerAction.Sleep));
    }

    [Theory]
    [InlineData(AllowedCommand.ShutdownAt, "/s")]
    [InlineData(AllowedCommand.RestartAt, "/r")]
    public void ShutdownAt_builds_fixed_arguments_without_comment(AllowedCommand cmd, string mode)
    {
        foreach (var p in new[] { "0", "60", "3600", "604800" })
        {
            var spec = CommandCatalog.Build(cmd, p);
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe"), spec.FilePath);
            Assert.Equal(new[] { mode, "/t", p }, spec.Arguments);
            Assert.DoesNotContain("/c", spec.Arguments);
            Assert.False(spec.RequiresElevation);
            Assert.All(spec.Arguments, a => Assert.True(CommandCatalog.IsSafeToken(a) || a.StartsWith('/'), a));
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+60")]
    [InlineData(" 60")]
    [InlineData("60 ")]
    [InlineData("604801")]
    [InlineData("1000000")]
    [InlineData("1e3")]
    [InlineData("0x10")]
    [InlineData("60 /c hallo")]
    [InlineData("٣٠")]
    [InlineData("60;calc")]
    public void ShutdownAt_rejects_invalid_seconds(string? parameter)
    {
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.ShutdownAt, parameter));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.RestartAt, parameter));
    }

    [Fact]
    public void Schedule_store_roundtrips_and_clears()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PowerScheduleStore(Path.Combine(dir, "power-schedule.json"));
            Assert.Null(store.Load());
            var e = new PowerScheduleEntry(Guid.NewGuid(), ScheduledPowerAction.Restart, Utc(2026, 5, 1, 20), Utc(2026, 5, 1, 19), true);
            store.Save(e);
            var back = store.Load();
            Assert.Equal(e, back);
            Assert.Equal(DateTimeKind.Utc, back!.DueUtc.Kind);
            store.Clear();
            Assert.Null(store.Load());
            File.WriteAllText(store.FilePath, "{kaputt");
            Assert.Null(store.Load()); // defekte Datei → keine Planung, kein Absturz
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

// ======================================================================== v1.3 PC Lifetime

public class V13LifetimeTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Own_log_uses_stop_or_last_heartbeat_and_current_session_until_now()
    {
        var current = Guid.NewGuid();
        var sessions = new[]
        {
            new UptimeSession(Guid.NewGuid(), T0, T0.AddHours(2), T0.AddHours(2)),                 // 2 h sauber
            new UptimeSession(Guid.NewGuid(), T0.AddHours(3), null, T0.AddHours(3).AddMinutes(45)), // Absturz → 45 min
            new UptimeSession(Guid.NewGuid(), T0.AddHours(5), null, null),                         // ohne Heartbeat → 0
            new UptimeSession(current, T0.AddHours(6), null, T0.AddHours(6)),                      // läuft → bis jetzt (1 h)
        };
        var s = RuntimeCalculator.Summarize(sessions, T0.AddHours(7), current);
        Assert.Equal(TimeSpan.FromMinutes(120 + 45 + 60), s.Total);
        Assert.Equal(4, s.Sessions);
        Assert.Equal(2, s.SessionsWithoutStop);
        Assert.Equal(T0, s.FirstStartUtc);
    }

    [Fact]
    public void Overlapping_sessions_are_counted_once()
    {
        var sessions = new[]
        {
            new UptimeSession(Guid.NewGuid(), T0, T0.AddHours(2), null),
            new UptimeSession(Guid.NewGuid(), T0.AddHours(1), T0.AddHours(3), null),
            new UptimeSession(Guid.NewGuid(), T0.AddHours(5), T0.AddHours(6), null),
        };
        Assert.Equal(TimeSpan.FromHours(4), RuntimeCalculator.Summarize(sessions, T0.AddDays(1)).Total);
    }

    [Fact]
    public void Boot_log_counts_starts_and_active_runtime_only()
    {
        var e = new List<BootLogEntry>
        {
            new(T0, BootLogKind.Start),
            new(T0.AddHours(2), BootLogKind.SleepEnter),                 // 2 h aktiv
            new(T0.AddHours(10), BootLogKind.Resume),
            new(T0.AddHours(11), BootLogKind.Stop),                      // +1 h
            new(T0.AddDays(1), BootLogKind.Start),
            new(T0.AddDays(1).AddHours(4), BootLogKind.Alive),           // Lebenszeichen nach 4 h
            new(T0.AddDays(1).AddHours(6), BootLogKind.Start),           // kein Stopp → nur bis Lebenszeichen (+4 h)
            new(T0.AddDays(1).AddHours(6).AddMinutes(1), BootLogKind.Unexpected),
            new(T0.AddDays(1).AddHours(8), BootLogKind.Stop),            // +2 h
        };
        var s = RuntimeCalculator.SummarizeBootLog(e, T0.AddDays(5));
        Assert.Equal(3, s.Starts);
        Assert.Equal(TimeSpan.FromHours(2 + 1 + 4 + 2), s.Runtime);
        Assert.Equal(1, s.UnexpectedShutdowns);
        Assert.Equal(1, s.SessionsWithoutStop);
        Assert.Equal(T0, s.EarliestUtc);
    }

    [Fact]
    public void Boot_log_open_session_counts_until_now_and_resume_without_sleep_is_conservative()
    {
        var e = new List<BootLogEntry>
        {
            new(T0, BootLogKind.Start),
            new(T0.AddHours(1), BootLogKind.Alive),
            new(T0.AddDays(2), BootLogKind.Resume),   // Schnellstart ohne protokollierten Schlafbeginn → alt nur bis 1 h
            new(T0.AddDays(2).AddSeconds(5), BootLogKind.Resume), // doppelte Meldung (107 + Power-Troubleshooter 1)
        };
        var s = RuntimeCalculator.SummarizeBootLog(e, T0.AddDays(2).AddHours(3), T0.AddHours(-5));
        Assert.Equal(1, s.Starts);
        Assert.Equal(TimeSpan.FromHours(1) + TimeSpan.FromHours(3), s.Runtime);
        Assert.Equal(T0.AddHours(-5), s.EarliestUtc);
    }

    [Fact]
    public void Boot_log_without_events_is_zero()
    {
        var s = RuntimeCalculator.SummarizeBootLog([], T0);
        Assert.Equal(0, s.Starts);
        Assert.Equal(TimeSpan.Zero, s.Runtime);
        Assert.Null(s.EarliestUtc);
    }

    [Fact]
    public void Recorder_heartbeat_gap_splits_session_and_stop_closes_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13-" + Guid.NewGuid().ToString("N"));
        var now = T0;
        try
        {
            using var rec = new UptimeRecorder(Path.Combine(dir, "uptime-log.json"), () => now);
            rec.Start(startTimer: false);
            now = T0.AddMinutes(5); rec.Heartbeat();
            now = T0.AddMinutes(10); rec.Heartbeat();
            now = T0.AddMinutes(70); rec.Heartbeat(); // 60 min Lücke (Energiesparen) → Abschnitt endet bei 10 min
            now = T0.AddMinutes(75); rec.Heartbeat();
            now = T0.AddMinutes(80); rec.Stop();

            var sessions = rec.Load();
            Assert.Equal(2, sessions.Count);
            Assert.Equal(T0.AddMinutes(10), sessions[0].StopUtc);
            Assert.Equal(T0.AddMinutes(80), sessions[1].StopUtc);
            Assert.Equal(TimeSpan.FromMinutes(10 + 10), RuntimeCalculator.Summarize(sessions, now).Total);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Recorder_crash_keeps_last_heartbeat()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13-" + Guid.NewGuid().ToString("N"));
        var now = T0;
        try
        {
            var path = Path.Combine(dir, "uptime-log.json");
            var rec = new UptimeRecorder(path, () => now);
            rec.Start(startTimer: false);
            now = T0.AddMinutes(5); rec.Heartbeat();
            // „Absturz“: kein Stop. Neue Instanz liest die Datei.
            now = T0.AddHours(3);
            var next = new UptimeRecorder(path, () => now);
            var s = next.Summarize();
            Assert.Equal(TimeSpan.FromMinutes(5), s.Total);
            Assert.Equal(1, s.SessionsWithoutStop);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Hours_text_is_culture_aware()
    {
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        Assert.Equal("1.234 h (51 T 10 h)", RuntimeCalculator.HoursText(TimeSpan.FromHours(1234), de));
        Assert.Equal("5 h 30 min", RuntimeCalculator.HoursText(TimeSpan.FromMinutes(330), de));
    }
}

// ======================================================================== v1.3 Speicherplatz-Analyse

public class V13SpaceAnalysisTests
{
    private static string NewDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13space-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteBytes(string path, int size, byte fill, byte? lastByte = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = Enumerable.Repeat(fill, size).ToArray();
        if (lastByte is { } b && size > 0) data[^1] = b;
        File.WriteAllBytes(path, data);
    }

    [Fact]
    public void Scan_sums_folders_bottom_up_and_sorts_by_size()
    {
        var dir = NewDir();
        try
        {
            WriteBytes(Path.Combine(dir, "a.txt"), 100, 1);
            WriteBytes(Path.Combine(dir, "big", "x.bin"), 5000, 2);
            WriteBytes(Path.Combine(dir, "big", "deep", "y.bin"), 3000, 3);
            WriteBytes(Path.Combine(dir, "small", "z.TXT"), 10, 4);
            var r = SpaceScanner.Scan(dir, new SpaceScanOptions { TopFiles = 2, DuplicateMinBytes = 1 });

            Assert.False(r.Cancelled);
            Assert.Equal(4, r.TotalFiles);
            Assert.Equal(8110, r.TotalBytes);
            Assert.Equal(8110, r.Root.TotalBytes);
            Assert.Equal(100, r.Root.OwnBytes);
            Assert.Equal("big", r.Root.Children[0].Name);          // größter Ordner zuerst
            Assert.Equal(8000, r.Root.Children[0].TotalBytes);
            Assert.Equal(3000, r.Root.Children[0].Children[0].TotalBytes);
            Assert.Equal(2, r.LargestFiles.Count);
            Assert.EndsWith("x.bin", r.LargestFiles[0].FullPath); // absteigend
            Assert.EndsWith("y.bin", r.LargestFiles[1].FullPath);
            var txt = Assert.Single(r.FileTypes, t => t.Extension == ".txt");  // Groß-/Kleinschreibung zusammengefasst
            Assert.Equal(2, txt.Files);
            Assert.Equal(110, txt.Bytes);
            Assert.Equal(0, r.AccessErrors);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scan_can_be_cancelled()
    {
        var dir = NewDir();
        try
        {
            WriteBytes(Path.Combine(dir, "a", "f.bin"), 10, 1);
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var r = SpaceScanner.Scan(dir, null, null, cts.Token);
            Assert.True(r.Cancelled);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Duplicates_require_identical_content_not_only_size_or_prefix()
    {
        var dir = NewDir();
        try
        {
            const int big = 200 * 1024; // > 64 KB → voller SHA-256 nötig
            WriteBytes(Path.Combine(dir, "1.bin"), big, 7);
            WriteBytes(Path.Combine(dir, "sub", "2.bin"), big, 7);             // identisch zu 1
            WriteBytes(Path.Combine(dir, "3.bin"), big, 7, lastByte: 9);       // gleiche Größe + gleiche ersten 64 KB, anderes Ende
            WriteBytes(Path.Combine(dir, "4.bin"), big, 8);                    // gleiche Größe, anderer Anfang
            WriteBytes(Path.Combine(dir, "s1.txt"), 300, 1);                   // klein, identisch
            WriteBytes(Path.Combine(dir, "s2.txt"), 300, 1);
            WriteBytes(Path.Combine(dir, "u.txt"), 301, 1);                    // Einzelgröße

            var scan = SpaceScanner.Scan(dir, new SpaceScanOptions { DuplicateMinBytes = 1 });
            Assert.DoesNotContain(301L, scan.SizeGroups.Keys);
            var r = DuplicateFinder.Find(scan.SizeGroups);

            Assert.False(r.Cancelled);
            Assert.Equal(2, r.Groups.Count);
            var g1 = Assert.Single(r.Groups, g => g.Size == big);
            Assert.Equal(2, g1.Paths.Count);
            Assert.Contains(g1.Paths, p => p.EndsWith("1.bin", StringComparison.Ordinal));
            Assert.Contains(g1.Paths, p => p.EndsWith("2.bin", StringComparison.Ordinal));
            Assert.Equal(64, g1.Sha256.Length);
            Assert.Equal(big, g1.WastedBytes);
            Assert.Equal(big + 300, r.WastedBytes);
        }
        finally { Directory.Delete(dir, true); }
    }

    private sealed class FakeHasher(Dictionary<string, string> partial, Dictionary<string, string> full) : IFileHasher
    {
        public int FullCalls { get; private set; }
        public string PartialHash(string path, int bytes, CancellationToken ct)
            => partial.TryGetValue(path, out var h) ? h : throw new IOException("gesperrt");
        public string FullHash(string path, CancellationToken ct) { FullCalls++; return full[path]; }
    }

    [Fact]
    public void Duplicate_finder_skips_unreadable_files_and_only_full_hashes_partial_matches()
    {
        var groups = new Dictionary<long, List<string>>
        {
            [1_000_000] = ["a", "b", "c", "locked"],
        };
        var hasher = new FakeHasher(
            new() { ["a"] = "P1", ["b"] = "P1", ["c"] = "P2" },
            new() { ["a"] = "F1", ["b"] = "F1", ["c"] = "F9" });
        var r = DuplicateFinder.Find(groups, hasher);
        Assert.Equal(1, r.UnreadableFiles);
        Assert.Equal(2, hasher.FullCalls); // c hat keinen Teil-Hash-Partner → kein voller Hash
        var g = Assert.Single(r.Groups);
        Assert.Equal(new[] { "a", "b" }, g.Paths);
    }

    [Fact]
    public void Percent_and_extension_helpers()
    {
        Assert.Equal(0, SpaceScanner.Percent(5, 0));
        Assert.Equal(50, SpaceScanner.Percent(5, 10));
        Assert.Equal(100, SpaceScanner.Percent(20, 10));
        Assert.Equal(".jpg", SpaceScanner.ExtensionKey("Foto.JPG"));
        Assert.Equal("(ohne)", SpaceScanner.ExtensionKey("Makefile"));
    }

    [Fact]
    public void Deletion_is_blocked_in_system_folders_and_drive_roots()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var extra = SpaceDeletionGuard.ExtraProtectedRoots;
        if (!string.IsNullOrEmpty(windows))
        {
            Assert.False(SpaceDeletionGuard.IsAllowedPath(Path.Combine(windows, "System32", "kernel32.dll"), extra, out _));
            Assert.False(SpaceDeletionGuard.IsAllowedPath(Path.GetPathRoot(windows)!, extra, out _)); // Laufwerkswurzel
        }
        if (!string.IsNullOrEmpty(programData))
            Assert.False(SpaceDeletionGuard.IsAllowedPath(Path.Combine(programData, "Vendor", "cache.bin"), extra, out _));
        if (!string.IsNullOrEmpty(programFiles))
            Assert.False(SpaceDeletionGuard.IsAllowedPath(Path.Combine(programFiles, "App", "app.exe"), extra, out _));
        Assert.False(SpaceDeletionGuard.IsAllowedPath("relative\\file.txt", extra, out _));
        Assert.False(SpaceDeletionGuard.IsAllowedPath(@"\\server\share\file.txt", extra, out _));

        var temp = Path.Combine(Path.GetTempPath(), "datei.iso");
        Assert.True(SpaceDeletionGuard.IsAllowedPath(temp, extra, out var reason), reason);
        // Zusätzlich übergebene Sperrwurzeln greifen ebenfalls
        Assert.False(SpaceDeletionGuard.IsAllowedPath(temp, [Path.GetTempPath()], out _));
    }

    [Fact]
    public void CanRecycle_requires_existing_regular_file()
    {
        var dir = NewDir();
        try
        {
            var f = Path.Combine(dir, "weg.bin");
            WriteBytes(f, 10, 1);
            Assert.True(SpaceDeletionGuard.CanRecycle(f, out var reason), reason);
            Assert.False(SpaceDeletionGuard.CanRecycle(Path.Combine(dir, "fehlt.bin"), out _));
            Assert.False(SpaceDeletionGuard.CanRecycle(dir, out _)); // Ordner
        }
        finally { Directory.Delete(dir, true); }
    }
}

// ======================================================================== v1.3 Zeitleiste

public class V13TimelineTests
{
    private static readonly DateTime T = new(2026, 9, 1, 12, 0, 0);

    private static TimelineEvent Ev(double hours, TimelineKind kind, string title = "x", bool dateOnly = false)
        => new(T.AddHours(hours), kind, title, "", "Test", dateOnly);

    [Fact]
    public void First_crash_within_48h_after_driver_install_is_marked()
    {
        var events = new[]
        {
            Ev(0, TimelineKind.DriverInstall, "nvlddmkm.inf 560.70"),
            Ev(10, TimelineKind.BlueScreen, "0x116"),
            Ev(20, TimelineKind.BlueScreen, "0x116"),   // nicht der erste
            Ev(30, TimelineKind.AppCrash, "game.exe"),  // erster Programmabsturz → eigene Gruppe
        };
        var hints = TimelineRules.Correlate(events);
        Assert.Equal(2, hints.Count);
        var sys = Assert.Single(hints, h => h.Crash.Kind == TimelineKind.BlueScreen);
        Assert.Equal(10, sys.HoursAfter, 3);
        Assert.Equal("nvlddmkm.inf 560.70", sys.Change.Title);
        Assert.Single(hints, h => h.Crash.Kind == TimelineKind.AppCrash);
    }

    [Fact]
    public void No_hint_after_48h_or_when_crashes_existed_before()
    {
        Assert.Empty(TimelineRules.Correlate([Ev(0, TimelineKind.WindowsUpdate), Ev(49, TimelineKind.BlueScreen)]));
        // Bereits 3 Tage vor dem Update abgestürzt → Problem bestand schon vorher.
        Assert.Empty(TimelineRules.Correlate([Ev(-72, TimelineKind.UnexpectedRestart), Ev(0, TimelineKind.WindowsUpdate), Ev(5, TimelineKind.BlueScreen)]));
        // Absturz VOR der Änderung zählt nicht.
        Assert.Empty(TimelineRules.Correlate([Ev(-1, TimelineKind.BlueScreen), Ev(0, TimelineKind.DriverInstall)]));
        // Programminstallationen sind keine Auslöser im Sinne der Regel.
        Assert.Empty(TimelineRules.Correlate([Ev(0, TimelineKind.ProgramInstall), Ev(2, TimelineKind.BlueScreen)]));
        // Nur-Datum-Ereignisse werden nicht korreliert.
        Assert.Empty(TimelineRules.Correlate([Ev(0, TimelineKind.DriverInstall, dateOnly: true), Ev(2, TimelineKind.BlueScreen)]));
    }

    [Fact]
    public void Closest_change_is_named_and_others_counted()
    {
        var hints = TimelineRules.Correlate([
            Ev(0, TimelineKind.WindowsUpdate, "KB1"),
            Ev(6, TimelineKind.DriverInstall, "audio.inf"),
            Ev(8, TimelineKind.UnexpectedRestart),
        ]);
        var h = Assert.Single(hints);
        Assert.Equal("audio.inf", h.Change.Title);
        Assert.Equal(1, h.OtherChangesInWindow);
        Assert.Equal(2, h.HoursAfter, 3);
    }

    [Fact]
    public void Filter_by_kind_and_search_and_group_by_day()
    {
        var events = new[]
        {
            Ev(0, TimelineKind.WindowsUpdate, "KB5050000"),
            Ev(1, TimelineKind.AppCrash, "explorer.exe"),
            Ev(30, TimelineKind.DriverInstall, "Realtek Audio"),
        };
        Assert.Single(TimelineRules.Filter(events, new HashSet<TimelineKind> { TimelineKind.AppCrash }, null));
        Assert.Single(TimelineRules.Filter(events, null, "realtek"));
        Assert.Single(TimelineRules.Filter(events, null, "Treiber")); // Suche auch in der Art-Bezeichnung
        Assert.Equal(3, TimelineRules.Filter(events, new HashSet<TimelineKind>(), "  ").Count());

        var days = TimelineRules.GroupByDay(events);
        Assert.Equal(2, days.Count);
        Assert.Equal(T.AddHours(30).Date, days[0].Day);        // neuester Tag zuerst
        Assert.Equal("explorer.exe", days[1].Events[0].Title); // innerhalb des Tages neueste zuerst
    }

    [Fact]
    public void Registry_install_date_is_dropped_when_msi_event_exists_same_day()
    {
        var list = TimelineRules.DeduplicateInstalls([
            new TimelineEvent(T, TimelineKind.ProgramInstall, "7-Zip 24.08", "", "MsiInstaller 11707"),
            new TimelineEvent(T.Date.AddHours(12), TimelineKind.ProgramInstall, "7-zip 24.08 ", "", "Uninstall-Registry", DateOnly: true),
            new TimelineEvent(T.Date.AddHours(12), TimelineKind.ProgramInstall, "Steam", "", "Uninstall-Registry", DateOnly: true),
        ]);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.Title == "Steam");
    }

    [Theory]
    [InlineData("Product: 7-Zip 24.08 (x64 edition) -- Installation completed successfully.", "7-Zip 24.08 (x64 edition)")]
    [InlineData("Produkt: Microsoft Edge -- Die Installation wurde erfolgreich abgeschlossen.", "Microsoft Edge")]
    [InlineData("Some Tool", "Some Tool")]
    public void Msi_product_name_is_extracted(string raw, string expected)
        => Assert.Equal(expected, TimelineRules.MsiProductName(raw));

    [Fact]
    public void Every_kind_has_label_and_glyph()
    {
        foreach (var k in Enum.GetValues<TimelineKind>())
        {
            Assert.False(string.IsNullOrWhiteSpace(TimelineRules.Label(k)));
            Assert.Single(TimelineRules.Glyph(k));
        }
        Assert.True(TimelineRules.IsProblem(TimelineKind.WindowsUpdateFailed));
        Assert.True(TimelineRules.IsCrash(TimelineKind.AppCrash));
        Assert.False(TimelineRules.IsSystemCrash(TimelineKind.AppCrash));
    }
}
