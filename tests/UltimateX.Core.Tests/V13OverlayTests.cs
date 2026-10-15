using System.Globalization;
using UltimateX.Core.Analysis;
using UltimateX.Core.Security;
using UltimateX.Core.Settings;

namespace UltimateX.Core.Tests;

// ======================================================================== FrameStats (v1.3 Overlay)

public class V13FrameStatsTests
{
    private static FrameStats Feed(double fps, double seconds, double start = 100)
    {
        var s = new FrameStats();
        var n = (int)Math.Round(fps * seconds);
        for (var i = 0; i <= n; i++) s.AddPresent(start + i / fps);
        return s;
    }

    [Fact]
    public void Steady_60fps_reports_60_fps_and_16_7ms()
    {
        var s = Feed(60, 3);
        var snap = s.Snapshot(103.0);
        Assert.True(snap.HasData);
        Assert.InRange(snap.Fps!.Value, 59, 61);
        Assert.InRange(snap.FrametimeMs!.Value, 16.5, 16.9);
        Assert.InRange(snap.OnePercentLowFps!.Value, 59, 61);
    }

    [Fact]
    public void Stutter_frames_lower_the_one_percent_low_but_not_the_average()
    {
        var s = new FrameStats();
        var t = 0.0;
        s.AddPresent(t);
        for (var i = 1; i <= 600; i++)
        {
            t += i % 100 == 0 ? 0.050 : 0.010; // 100 FPS mit 6 Rucklern à 50 ms
            s.AddPresent(t);
        }
        var snap = s.Snapshot(t);
        Assert.InRange(snap.Fps!.Value, 90, 101);
        // 1 % von ~600 Frames = 6 langsamste Frames = genau die Ruckler → 20 FPS
        Assert.InRange(snap.OnePercentLowFps!.Value, 19.5, 20.5);
    }

    [Fact]
    public void Stale_data_returns_empty_snapshot()
    {
        var s = Feed(60, 2);
        Assert.True(s.Snapshot(102.5).HasData);
        Assert.False(s.Snapshot(110).HasData); // 8 s ohne Frame → keine alte Zahl anzeigen
    }

    [Fact]
    public void Pause_gap_is_not_counted_as_frametime()
    {
        var s = new FrameStats();
        for (var i = 0; i <= 60; i++) s.AddPresent(i / 60.0);
        s.AddPresent(5.0); // 4 s Pause
        for (var i = 1; i <= 30; i++) s.AddPresent(5.0 + i / 60.0);
        var snap = s.Snapshot(5.5);
        Assert.True(snap.FrametimeMs < 20);
        Assert.True(snap.OnePercentLowFps > 50);
    }

    [Fact]
    public void Short_capture_uses_mean_frametime_and_reset_clears()
    {
        var s = new FrameStats();
        for (var i = 0; i <= 15; i++) s.AddPresent(10 + i / 144.0); // ~0,1 s bei 144 FPS
        var snap = s.Snapshot(10.2);
        Assert.InRange(snap.Fps!.Value, 140, 148);
        Assert.Equal(15, snap.FramesInWindow);
        s.Reset();
        Assert.False(s.Snapshot(10.2).HasData);
    }

    [Fact]
    public void Out_of_order_and_invalid_timestamps_are_ignored()
    {
        var s = new FrameStats();
        s.AddPresent(1.0);
        s.AddPresent(0.5);
        s.AddPresent(double.NaN);
        s.AddPresent(1.0);
        Assert.False(s.Snapshot(1.0).HasData); // noch kein gültiges Frame-Intervall
    }

    [Fact]
    public void Window_drops_frames_older_than_ten_seconds()
    {
        var s = Feed(30, 20, start: 0);
        var snap = s.Snapshot(20);
        Assert.InRange(snap.FramesInWindow, 295, 301);
    }
}

// ======================================================================== AlertRules (v1.3 Infobereich)

public class V13AlertRulesTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 12, 0, 0);

    [Fact]
    public void Cpu_temperature_fires_only_after_sustain_time()
    {
        var e = new AlertEngine(new AlertSettings());
        for (var i = 0; i < 30; i++)
            Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(i), 95, null));
        var fired = e.ObserveTemperatures(T0.AddSeconds(30), 95, null);
        var a = Assert.Single(fired);
        Assert.Equal(AlertRuleId.CpuTemperature, a.Rule);
    }

    [Fact]
    public void Dropping_below_limit_or_missing_value_resets_debounce()
    {
        var e = new AlertEngine(new AlertSettings());
        e.ObserveTemperatures(T0, 95, null);
        e.ObserveTemperatures(T0.AddSeconds(20), 80, null); // unter Grenzwert → neu zählen
        Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(35), 95, null));
        Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(50), null, null)); // keine Quelle → neu zählen
        Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(60), 95, null));
        Assert.Single(e.ObserveTemperatures(T0.AddSeconds(90), 95, null));
    }

    [Fact]
    public void Repeat_lock_suppresses_same_alert_for_30_minutes()
    {
        var e = new AlertEngine(new AlertSettings());
        var t = T0;
        var count = 0;
        for (var i = 0; i <= 40 * 60; i += 10) // 40 Minuten durchgehend 90 °C GPU (Grenzwert 85)
            count += e.ObserveTemperatures(t.AddSeconds(i), null, 90).Count;
        Assert.Equal(2, count); // nach 30 s und nach 30 min Sperre erneut
    }

    [Fact]
    public void Gpu_default_limit_is_85_and_value_at_limit_does_not_fire()
    {
        var e = new AlertEngine(new AlertSettings());
        for (var i = 0; i <= 120; i++)
            Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(i), 90, 85));
    }

    [Fact]
    public void Disabled_rule_does_not_fire()
    {
        var e = new AlertEngine(new AlertSettings { CpuTempEnabled = false });
        for (var i = 0; i <= 60; i++)
            Assert.Empty(e.ObserveTemperatures(T0.AddSeconds(i), 105, null));
    }

    [Fact]
    public void Disk_rules_distinguish_warning_and_critical_and_escalate()
    {
        var e = new AlertEngine(new AlertSettings());
        var warn = e.EvaluateChecks(T0, new SystemCheckInput(Drives: [new DriveFillInfo("C:", 91, 10), new DriveFillInfo("D:", 50, 10)]));
        Assert.Equal(AlertSeverity.Warning, Assert.Single(warn).Severity);
        Assert.Empty(e.EvaluateChecks(T0.AddMinutes(5), new SystemCheckInput(Drives: [new DriveFillInfo("C:", 92, 10)])));
        var crit = e.EvaluateChecks(T0.AddMinutes(10), new SystemCheckInput(Drives: [new DriveFillInfo("C:", 96, 10)]));
        Assert.Equal(AlertSeverity.Critical, Assert.Single(crit).Severity); // Eskalation trotz Sperre der Warnung
    }

    [Fact]
    public void Null_inputs_are_skipped_and_other_rules_fire_once()
    {
        var e = new AlertEngine(new AlertSettings());
        Assert.Empty(e.EvaluateChecks(T0, new SystemCheckInput()));
        var input = new SystemCheckInput(
            SmartWarnings: ["Samsung SSD: WARNING – Verschleiß 92 %"],
            NewBlueScreens: [T0.AddHours(-1)],
            DefenderOff: true,
            BackupOverdue: ["Dokumente: letzte Sicherung vor 9 Tagen", " "]);
        var alerts = e.EvaluateChecks(T0, input);
        Assert.Equal(4, alerts.Count);
        Assert.Contains(alerts, a => a.Rule == AlertRuleId.BlueScreen);
        Assert.Contains(alerts, a => a.Rule == AlertRuleId.DefenderOff);
        Assert.Empty(e.EvaluateChecks(T0.AddMinutes(5), input)); // Wiederholungssperre
        Assert.Equal(4, e.EvaluateChecks(T0.AddMinutes(31), input).Count);
    }

    [Fact]
    public void Defender_on_and_no_blue_screens_do_not_fire()
    {
        var e = new AlertEngine(new AlertSettings());
        Assert.Empty(e.EvaluateChecks(T0, new SystemCheckInput(NewBlueScreens: [], DefenderOff: false)));
    }

    [Fact]
    public void Settings_are_clamped()
    {
        var s = new AlertSettings { CpuTempLimitC = 500, GpuTempLimitC = 1, DiskWarnPercent = 96, DiskCriticalPercent = 50, CheckIntervalMinutes = 0, RepeatLockMinutes = -5 }.Normalize();
        Assert.Equal(110, s.CpuTempLimitC);
        Assert.Equal(50, s.GpuTempLimitC);
        Assert.Equal(96, s.DiskCriticalPercent); // nie unter der Warnschwelle
        Assert.Equal(1, s.CheckIntervalMinutes);
        Assert.Equal(1, s.RepeatLockMinutes);
    }
}

// ======================================================================== Overlay-Einstellungen + Tastenkürzel

public class V13OverlaySettingsTests
{
    private static string TempFile() => Path.Combine(Path.GetTempPath(), "ux-v13-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void Defaults_are_sane()
    {
        var s = new OverlaySettings().Normalize();
        Assert.Equal("Strg+Umschalt+O", s.Hotkey);
        Assert.Equal(OverlayCorner.TopLeft, s.Corner);
        Assert.Contains(OverlayElement.Fps, s.Elements);
        Assert.False(s.ShowOnStartup);
    }

    [Fact]
    public void Json_roundtrip_keeps_values_and_uses_enum_names()
    {
        var path = TempFile();
        try
        {
            var s = new OverlaySettings
            {
                Corner = OverlayCorner.BottomRight, FontSize = 18, Opacity = 0.8, BackgroundOpacity = 0.2, Hotkey = "alt+f10",
                Elements = [OverlayElement.Fps, OverlayElement.Clock], PingTarget = OverlayPingTarget.Cloudflare, MonitorDevice = @"\\.\DISPLAY2",
            };
            s.SaveProfile("Minimal");
            s.Save(path);
            Assert.Contains("\"BottomRight\"", File.ReadAllText(path));

            var l = OverlaySettings.Load(path);
            Assert.Equal(OverlayCorner.BottomRight, l.Corner);
            Assert.Equal(18, l.FontSize);
            Assert.Equal(0.8, l.Opacity, 3);
            Assert.Equal("Alt+F10", l.Hotkey);
            Assert.Equal(new[] { OverlayElement.Fps, OverlayElement.Clock }, l.Elements);
            Assert.Equal(OverlayPingTarget.Cloudflare, l.PingTarget);
            Assert.Equal(@"\\.\DISPLAY2", l.MonitorDevice);
            Assert.Equal("Minimal", Assert.Single(l.Profiles).Name);
            Assert.Equal("Minimal", l.ActiveProfile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_or_out_of_range_json_falls_back_safely()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ nicht json");
            Assert.Equal(14, OverlaySettings.Load(path).FontSize);

            File.WriteAllText(path, """{ "FontSize": 999, "Opacity": 0, "Hotkey": "O", "Elements": ["Fps","Fps","Ram"], "ActiveProfile": "gibt es nicht" }""");
            var s = OverlaySettings.Load(path);
            Assert.Equal(40, s.FontSize);
            Assert.Equal(0.3, s.Opacity, 3);
            Assert.Equal("Strg+Umschalt+O", s.Hotkey); // ungültig → Standard
            Assert.Equal(new[] { OverlayElement.Fps, OverlayElement.Ram }, s.Elements);
            Assert.Null(s.ActiveProfile);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        Assert.Equal(OverlaySettings.DefaultElements.Count, OverlaySettings.Load(TempFile()).Elements.Count);
    }

    [Fact]
    public void Profiles_apply_and_replace_by_name()
    {
        var s = new OverlaySettings { Elements = [OverlayElement.Fps], FontSize = 12 };
        s.SaveProfile("A");
        s.Elements = [OverlayElement.Ram];
        s.FontSize = 20;
        s.SaveProfile("a"); // gleicher Name (ohne Groß/Klein) ersetzt
        Assert.Single(s.Profiles);
        s.Elements = [];
        Assert.True(s.ApplyProfile("A"));
        Assert.Equal(new[] { OverlayElement.Ram }, s.Elements);
        Assert.Equal(20, s.FontSize);
        Assert.False(s.ApplyProfile("X"));
    }

    [Fact]
    public void Background_monitor_settings_roundtrip()
    {
        var path = TempFile();
        try
        {
            var s = new BackgroundMonitorSettings { MinimizeToTrayOnClose = true, Rules = new AlertSettings { CpuTempLimitC = 80, BackupEnabled = false } };
            s.Save(path);
            var l = BackgroundMonitorSettings.Load(path);
            Assert.True(l.MinimizeToTrayOnClose);
            Assert.Equal(80, l.Rules.CpuTempLimitC);
            Assert.False(l.Rules.BackupEnabled);
            Assert.Equal(85, l.Rules.GpuTempLimitC);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("Strg+Umschalt+O", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4F, "Strg+Umschalt+O")]
    [InlineData("ctrl + shift + o", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x4F, "Strg+Umschalt+O")]
    [InlineData("Alt+F10", HotkeyModifiers.Alt, 0x79, "Alt+F10")]
    [InlineData("Win+Alt+1", HotkeyModifiers.Windows | HotkeyModifiers.Alt, 0x31, "Alt+Win+1")]
    [InlineData("Strg+Alt+Pos1", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x24, "Strg+Alt+Pos1")]
    [InlineData("Ctrl+PageDown", HotkeyModifiers.Control, 0x22, "Strg+BildAb")]
    [InlineData("Strg+F24", HotkeyModifiers.Control, 0x87, "Strg+F24")]
    public void Hotkey_parses_valid_gestures(string text, HotkeyModifiers mods, int vk, string display)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var g));
        Assert.Equal(mods, g.Modifiers);
        Assert.Equal(vk, g.VirtualKey);
        Assert.Equal(display, g.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("O")]                 // ohne Modifizierer
    [InlineData("Umschalt+O")]        // nur Umschalt reicht nicht
    [InlineData("Strg+Umschalt")]     // keine Taste
    [InlineData("Strg+O+P")]          // zwei Tasten
    [InlineData("Strg+Strg+O")]       // doppelt
    [InlineData("Strg+F25")]
    [InlineData("Strg+Ä")]
    [InlineData("Strg++O")]
    [InlineData("Strg+Space")]
    public void Hotkey_rejects_invalid_gestures(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _));
    }
}

// ======================================================================== Overlay-Zeilen, Sitzungs-CSV, speedtest.ch

public class V13OverlayDataTests
{
    [Fact]
    public void Composer_hides_lines_without_source_and_keeps_order()
    {
        var v = new OverlayValues(Fps: 143.6, CpuLoad: 37, GpuTempC: null, RamPercent: 50, RamUsedBytes: 8UL * 1073741824);
        var lines = OverlayComposer.Compose([OverlayElement.GpuTemp, OverlayElement.Fps, OverlayElement.CpuLoad, OverlayElement.Ram, OverlayElement.Ping], v, CultureInfo.InvariantCulture);
        Assert.Equal(new[] { OverlayElement.Fps, OverlayElement.CpuLoad, OverlayElement.Ram }, lines.Select(l => l.Element));
        Assert.Equal("144", lines[0].Value);
        Assert.Equal("8.0 GB (50 %)", lines[2].Value);
    }

    [Fact]
    public void Composer_shows_na_for_fps_only_when_permission_missing()
    {
        Assert.Empty(OverlayComposer.Compose([OverlayElement.Fps, OverlayElement.Frametime], new OverlayValues()));
        var lines = OverlayComposer.Compose([OverlayElement.Fps, OverlayElement.Frametime], new OverlayValues(FpsUnavailable: true));
        Assert.Equal("N/A", Assert.Single(lines).Value);
    }

    [Fact]
    public void Composer_formats_clock_in_ghz()
    {
        var lines = OverlayComposer.Compose([OverlayElement.CpuClock, OverlayElement.GpuClock], new OverlayValues(CpuClockMHz: 4650, GpuClockMHz: 870), CultureInfo.InvariantCulture);
        Assert.Equal("4.65 GHz", lines[0].Value);
        Assert.Equal("870 MHz", lines[1].Value);
    }

    [Fact]
    public void Session_csv_roundtrip_and_summary()
    {
        var t = new DateTime(2026, 10, 14, 20, 0, 0);
        var rows = new[]
        {
            GameSessionCsv.Header,
            GameSessionCsv.FormatRow(new GameSessionSample(t, "game", 120, 8.33, 90, 40, 70, 95, 72)),
            GameSessionCsv.FormatRow(new GameSessionSample(t.AddSeconds(1), "game", 100, 10, 80, 45, 75, 97, 74)),
            GameSessionCsv.FormatRow(new GameSessionSample(t.AddSeconds(2), null, null, null, null, 10, null, 5, null)),
        };
        var parsed = GameSessionCsv.Parse(rows);
        Assert.Equal(3, parsed.Count);
        Assert.Equal(8.33, parsed[0].FrametimeMs);
        Assert.Null(parsed[2].Fps);
        var sum = GameSessionCsv.Summarize(parsed);
        Assert.Equal(110, sum.AverageFps);
        Assert.Equal(100, sum.MinFps);
        Assert.Equal(75, sum.MaxCpuTempC);
        Assert.Equal("game", sum.MainProcess);
        Assert.Equal(TimeSpan.FromSeconds(2), sum.Duration);
    }

    [Theory]
    [InlineData("=cmd|' /C calc'!A0", "'=cmd|' /C calc'!A0")]
    [InlineData("a;b\"c", "abc")]
    [InlineData("@SUM(1)", "'@SUM(1)")]
    [InlineData(null, "")]
    public void Session_csv_cells_are_sanitized(string? input, string expected)
    {
        Assert.Equal(expected, GameSessionCsv.SafeCell(input));
    }

    [Theory]
    [InlineData("250,5", "40", "12", "", 250.5, 40, 12, null)]
    [InlineData(" 1000 ", "100.25", "0", "3,2", 1000, 100.25, 0, 3.2)]
    public void Manual_speedtest_accepts_valid_values(string d, string u, string p, string j, double ed, double eu, double ep, double? ej)
    {
        Assert.True(ManualSpeedtestEntry.TryParse(d, u, p, j, out var m, out var err));
        Assert.Null(err);
        Assert.Equal(ed, m!.DownloadMbps, 3);
        Assert.Equal(eu, m.UploadMbps, 3);
        Assert.Equal(ep, m.PingMs, 3);
        Assert.Equal(ej, m.JitterMs);
    }

    [Theory]
    [InlineData("", "40", "12", "")]
    [InlineData("0", "40", "12", "")]
    [InlineData("abc", "40", "12", "")]
    [InlineData("-5", "40", "12", "")]
    [InlineData("250", "200000", "12", "")]
    [InlineData("250", "40", "1e3", "")]
    [InlineData("250", "40", "12", "x")]
    [InlineData("250", "40", "12", "NaN")]
    public void Manual_speedtest_rejects_invalid_values(string d, string u, string p, string j)
    {
        Assert.False(ManualSpeedtestEntry.TryParse(d, u, p, j, out var m, out var err));
        Assert.Null(m);
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Theory]
    [InlineData("https://www.speedtest.ch/", true)]
    [InlineData("https://speedtest.ch/de/", true)]
    [InlineData("https://cdn.speedtest.ch/x.js", true)]
    [InlineData("https://WWW.SPEEDTEST.CH/", true)]
    [InlineData("http://www.speedtest.ch/", false)]
    [InlineData("https://speedtest.ch.evil.com/", false)]
    [InlineData("https://evilspeedtest.ch/", false)]
    [InlineData("https://user:pw@www.speedtest.ch/", false)]
    [InlineData("https://www.speedtest.ch:8443/", false)]
    [InlineData("https://ads.example.com/?r=https://www.speedtest.ch", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("about:blank", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Embedded_browser_only_allows_https_speedtest_ch(string? uri, bool allowed)
    {
        Assert.Equal(allowed, EmbeddedBrowserPolicy.IsAllowedSpeedtestCh(uri));
    }

    [Fact]
    public void Manual_source_is_distinguishable()
    {
        Assert.True(ManualSpeedtestEntry.IsManualSource("speedtest.ch (manuell)"));
        Assert.False(ManualSpeedtestEntry.IsManualSource("speed.cloudflare.com"));
    }
}
