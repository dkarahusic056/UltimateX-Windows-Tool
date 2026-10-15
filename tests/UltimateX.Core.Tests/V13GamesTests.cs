using System.Text.Json;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Gaming;

namespace UltimateX.Core.Tests;

// ======================================================================== v1.3: Spiele-Anforderungen online (Steam-Shop)

internal static class SteamSamples
{
    // Aufbau wie in der öffentlichen appdetails-Antwort (data.pc_requirements, l=english).
    public const string ModernMin =
        "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li>Requires a 64-bit processor and operating system<br></li>" +
        "<li><strong>OS:</strong> 64-bit Windows 10<br></li><li><strong>Processor:</strong> Intel Core i5-6600 (3.3Ghz) or AMD Ryzen 5 1400 (3.2Ghz)<br></li>" +
        "<li><strong>Memory:</strong> 16 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA GeForce GTX 960 4GB or AMD Radeon RX 470 4GB<br></li>" +
        "<li><strong>DirectX:</strong> Version 12<br></li><li><strong>Storage:</strong> 85 GB available space<br></li>" +
        "<li><strong>Additional Notes:</strong> SSD (Preferred), HDD (Supported), 720p/30 fps</li></ul>";

    public const string ModernRec =
        "<strong>Recommended:</strong><br><ul class=\"bb_ul\"><li>Requires a 64-bit processor and operating system<br></li>" +
        "<li><strong>OS:</strong> 64-bit Windows 10<br></li><li><strong>Processor:</strong> Intel Core i7-8700 (3.2Ghz) or AMD Ryzen 5 3600 (3.6 Ghz)<br></li>" +
        "<li><strong>Memory:</strong> 16 GB RAM<br></li><li><strong>Graphics:</strong> NVIDIA GeForce 1080 Ti or AMD Radeon RX 5700 XT or INTEL Arc A770<br></li>" +
        "<li><strong>DirectX:</strong> Version 12<br></li><li><strong>Storage:</strong> 85 GB available space<br></li>" +
        "<li><strong>Additional Notes:</strong> SSD, 1080p/60 fps</li></ul>";

    // Älteres Spiel: MB-Angaben, „Hard Drive“, DirectX 9.0c, kein recommended.
    public const string OldMin =
        "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS *:</strong> Windows XP / Vista / 7<br></li>" +
        "<li><strong>Processor:</strong> Intel Core 2 Duo 2.4 GHz or AMD Athlon X2 2.8 GHz<br></li><li><strong>Memory:</strong> 2048 MB RAM<br></li>" +
        "<li><strong>Graphics:</strong> ATI Radeon HD 4870 512MB / NVIDIA GeForce 9800 GT 512MB<br></li>" +
        "<li><strong>DirectX:</strong> Version 9.0c<br></li><li><strong>Hard Drive:</strong> 1500 MB available space</li></ul>";

    // HTML-Entities, „CPU/RAM/Video Card/VRAM/Hard Disk Space“, „or better“, SSD ausdrücklich verlangt.
    public const string EntityMin =
        "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>OS:</strong> Windows&reg; 10 64-bit<br></li>" +
        "<li><strong>CPU:</strong> Intel&reg; Core&trade; i7-4790 @ 3.6&nbsp;GHz or better<br></li><li><strong>RAM:</strong> 8 GB<br></li>" +
        "<li><strong>Video Card:</strong> NVIDIA&reg; GeForce&reg; GTX 1070 or AMD Radeon&trade; RX Vega 56 &amp; better<br></li>" +
        "<li><strong>VRAM:</strong> 6 GB<br></li><li><strong>Hard Disk Space:</strong> 70 GB (SSD required)</li></ul>";

    public const string EntityRec =
        "<strong>Recommended:</strong><br><ul class=\"bb_ul\"><li><strong>CPU:</strong> AMD Ryzen 7 3700X | Intel Core i7-9700K<br></li>" +
        "<li><strong>RAM:</strong> 16 GB<br></li><li><strong>Video Card:</strong> NVIDIA GeForce RTX 2070 SUPER, AMD Radeon RX 5700 XT (8 GB)<br></li></ul>";

    // Nur allgemeine Angaben – nichts davon ist in der Hardware-Datenbank.
    public const string VagueMin =
        "<strong>Minimum:</strong><br><ul class=\"bb_ul\"><li><strong>Processor:</strong> Quad-core Intel or AMD processor, 2.5 GHz or faster<br></li>" +
        "<li><strong>Memory:</strong> 4 GB RAM<br></li><li><strong>Graphics:</strong> DirectX 11 compatible graphics card with 1 GB of VRAM<br></li>" +
        "<li><strong>Storage:</strong> 10 GB available space</li></ul>";

    public static readonly PcProfile HighEnd = new("AMD Ryzen 7 7800X3D 8-Core Processor", 8, "NVIDIA GeForce RTX 4090", null, 32, 500, true, 12, 22631, true);

    public static string AppDetailsJson(int appId, string name, object pcRequirements)
        => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [appId.ToString(System.Globalization.CultureInfo.InvariantCulture)] = new
            {
                success = true,
                data = new { type = "game", name, steam_appid = appId, pc_requirements = pcRequirements },
            },
        });

    public static GameInfo Hogwarts() => Online(990080, "Hogwarts Legacy™");

    /// <summary>Spiel aus einer appdetails-Antwort; für Speichertests eine AppID/einen Namen verwenden, die nicht eingebaut sind.</summary>
    public static GameInfo Online(int appId, string name)
        => SteamRequirements.ParseAppDetails(AppDetailsJson(appId, name, new { minimum = ModernMin, recommended = ModernRec }),
            appId, new DateTime(2026, 10, 14)).Game ?? throw new InvalidOperationException("Parser lieferte kein Spiel");
}

public class SteamRequirementsParserTests
{
    [Fact]
    public void Modern_minimum_is_parsed_completely()
    {
        var min = SteamRequirements.ParseSection(SteamSamples.ModernMin, recommended: false)!;
        Assert.Equal(new[] { "Intel Core i5-6600", "AMD Ryzen 5 1400" }, min.Cpu);
        Assert.Equal(new[] { "NVIDIA GeForce GTX 960", "AMD Radeon RX 470" }, min.Gpu);
        Assert.Equal(16, min.RamGb);
        Assert.Equal(4, min.VramGb); // aus „4GB“ in der Grafikzeile
        Assert.Equal(85, min.StorageGb);
        Assert.Equal(12, min.DirectX);
        Assert.False(min.Ssd); // „SSD (Preferred)“ ist keine Pflicht
        Assert.NotNull(GameCompatibility.RequiredCpuIndex(min.Cpu));
        Assert.NotNull(GameCompatibility.RequiredGpuIndex(min.Gpu));
    }

    [Fact]
    public void Modern_recommended_uses_database_vram_and_mentions_ssd()
    {
        var rec = SteamRequirements.ParseSection(SteamSamples.ModernRec, recommended: true)!;
        Assert.Equal(new[] { "Intel Core i7-8700", "AMD Ryzen 5 3600" }, rec.Cpu);
        Assert.Equal(3, rec.Gpu.Count);
        Assert.Contains("AMD Radeon RX 5700 XT", rec.Gpu);
        Assert.Contains("INTEL Arc A770", rec.Gpu);
        Assert.Equal(8, rec.VramGb); // keine Angabe in der Zeile → kleinster bekannter Wert (RX 5700 XT 8 GB, Arc A770 16 GB)
        Assert.True(rec.Ssd);
        Assert.NotNull(GameCompatibility.RequiredGpuIndex(rec.Gpu));
    }

    [Fact]
    public void Old_game_with_megabytes_and_missing_recommended()
    {
        var parsed = SteamRequirements.ParseRequirements(SteamSamples.OldMin, null);
        Assert.NotNull(parsed);
        var (min, rec) = parsed.Value;
        Assert.Equal(2, min.RamGb);      // 2048 MB
        Assert.Equal(2, min.StorageGb);  // 1500 MB → aufgerundet
        Assert.Equal(1, min.VramGb);     // 512 MB → aufgerundet
        Assert.Equal(9, min.DirectX);    // „Version 9.0c“
        Assert.Contains("AMD Athlon X2", min.Cpu);
        Assert.Contains("ATI Radeon HD 4870", min.Gpu);
        Assert.Contains("NVIDIA GeForce 9800 GT", min.Gpu);
        Assert.NotNull(GameCompatibility.RequiredCpuIndex(min.Cpu));
        Assert.NotNull(GameCompatibility.RequiredGpuIndex(min.Gpu));
        // Fehlendes recommended → Mindestwerte.
        Assert.Equal(min.RamGb, rec.RamGb);
        Assert.Equal(min.Gpu, rec.Gpu);
        Assert.Equal(min.StorageGb, rec.StorageGb);
    }

    [Fact]
    public void Entities_or_better_and_required_ssd()
    {
        var parsed = SteamRequirements.ParseRequirements(SteamSamples.EntityMin, SteamSamples.EntityRec);
        Assert.NotNull(parsed);
        var (min, rec) = parsed.Value;
        Assert.Equal(new[] { "Intel Core i7-4790" }, min.Cpu);
        Assert.Equal(new[] { "NVIDIA GeForce GTX 1070", "AMD Radeon RX Vega 56" }, min.Gpu);
        Assert.Equal(8, min.RamGb);
        Assert.Equal(6, min.VramGb); // eigene VRAM-Zeile
        Assert.Equal(70, min.StorageGb);
        Assert.True(min.Ssd);
        Assert.Equal(0, min.DirectX); // nicht angegeben → 0 (N/A), nicht geraten

        Assert.Equal(new[] { "AMD Ryzen 7 3700X", "Intel Core i7-9700K" }, rec.Cpu);
        Assert.Equal(new[] { "NVIDIA GeForce RTX 2070 SUPER", "AMD Radeon RX 5700 XT" }, rec.Gpu);
        Assert.Equal(16, rec.RamGb);
        Assert.Equal(8, rec.VramGb);
        Assert.Equal(70, rec.StorageGb); // aus dem Minimum aufgefüllt
        Assert.True(rec.Ssd);
    }

    [Fact]
    public void Terabytes_and_thousands_separator()
    {
        Assert.Equal(1536, SteamRequirements.CeilGb(SteamRequirements.ParseSizeGb("1.5 TB available space")));
        Assert.Equal(1, SteamRequirements.CeilGb(SteamRequirements.ParseSizeGb("1,024 MB")));
        Assert.Equal(2, SteamRequirements.CeilGb(SteamRequirements.ParseSizeGb("1,5 GB")));
        Assert.Null(SteamRequirements.ParseSizeGb("8")); // ohne Einheit wird nichts geraten
        Assert.Equal(0, SteamRequirements.CeilGb(null));
    }

    [Theory]
    [InlineData("SSD required", true, true)]
    [InlineData("70 GB available space (SSD required)", true, true)]
    [InlineData("Requires SSD – HDD not supported", true, true)]
    [InlineData("SSD (Preferred), HDD (Supported)", false, true)]
    [InlineData("SSD recommended", false, true)]
    [InlineData("SSD not required", false, false)]
    [InlineData("Solid State Drive required", true, true)]
    [InlineData("50 GB available space", false, false)]
    public void Ssd_rules(string text, bool required, bool mentioned)
    {
        Assert.Equal(required, SteamRequirements.SsdRequired([text]));
        Assert.Equal(mentioned, SteamRequirements.SsdMentioned([text]));
    }

    [Theory]
    [InlineData("Intel Core i5-4460, 3.2 GHz or AMD FX-8350", "Intel Core i5-4460", "AMD FX-8350")]
    [InlineData("Intel® Core™ i5-8400 | AMD Ryzen™ 5 2600", "Intel Core i5-8400", "AMD Ryzen 5 2600")]
    [InlineData("Intel Core i7-4770K / AMD equivalent", "Intel Core i7-4770K", null)]
    public void Cpu_alternatives_are_cleaned(string line, string first, string? second)
    {
        var list = SteamRequirements.SplitAlternatives(line, gpu: false);
        Assert.Equal(first, list[0]);
        if (second is null) Assert.Single(list);
        else Assert.Equal(second, list[1]);
        Assert.All(list, n => Assert.True(HardwareDatabase.EstimateCpu(n).Recognized, n));
    }

    [Theory]
    [InlineData("NVIDIA GeForce GTX 1060 6GB / AMD Radeon RX 580 8GB", 6)]
    [InlineData("GeForce GTX 970 (4 GB) or better", 4)]
    [InlineData("Radeon HD 7870", 0)]
    public void Vram_from_graphics_line(string line, int expected)
        => Assert.Equal(expected, SteamRequirements.VramFromGraphicsText(line));

    [Fact]
    public void Unknown_hardware_stays_empty_and_rating_is_na()
    {
        var parsed = SteamRequirements.ParseRequirements(SteamSamples.VagueMin, null);
        Assert.NotNull(parsed);
        var (min, rec) = parsed.Value;
        Assert.Empty(min.Cpu);
        Assert.Empty(min.Gpu);
        Assert.Equal(4, min.RamGb);
        Assert.Equal(1, min.VramGb);
        Assert.Equal(11, min.DirectX); // „DirectX 11 compatible graphics card“

        var game = new GameInfo("Vage", [], 0, false, false, [], min, rec, Online: true, Source: "Test");
        var r = GameCompatibility.Evaluate(game, SteamSamples.HighEnd);
        Assert.Equal(CompatibilityRating.Unknown, r.Rating);
        Assert.Equal("N/A", r.RatingText);
        var cpu = r.Checks.Single(c => c.Area == "CPU");
        Assert.Equal(CheckStatus.NotAvailable, cpu.Status);
        Assert.Equal("N/A", cpu.Minimum);
        Assert.Null(GameCompatibility.EstimateFps(game, SteamSamples.HighEnd, Resolution.P1080, Quality.High, false, Upscaler.None).FpsLow);
    }

    [Fact]
    public void Empty_or_missing_sections_give_null()
    {
        Assert.Null(SteamRequirements.ParseRequirements(null, null));
        Assert.Null(SteamRequirements.ParseRequirements("<strong>Minimum:</strong><br>", ""));
    }

    [Fact]
    public void AppDetails_creates_online_game_with_source()
    {
        var g = SteamSamples.Hogwarts();
        Assert.Equal("Hogwarts Legacy", g.Name);
        Assert.Equal(990080, g.SteamAppId);
        Assert.True(g.Online);
        Assert.Equal("Steam (AppID 990080, abgerufen am 14.10.2026)", g.Source);
        Assert.False(g.RayTracing);
        Assert.Empty(g.Upscalers);
        Assert.Equal(16, g.Minimum.RamGb);
        Assert.True(g.Recommended.Ssd);
    }

    [Fact]
    public void AppDetails_with_empty_requirements_array_or_failure_gives_error()
    {
        var empty = SteamRequirements.ParseAppDetails(SteamSamples.AppDetailsJson(12345, "Some Game", Array.Empty<object>()), 12345, DateTime.Now);
        Assert.Null(empty.Game);
        Assert.Contains("keine lesbaren PC-Systemanforderungen", empty.Error, StringComparison.Ordinal);

        var failed = SteamRequirements.ParseAppDetails("{\"12345\":{\"success\":false}}", 12345, DateTime.Now);
        Assert.Null(failed.Game);
        Assert.NotNull(failed.Error);

        var broken = SteamRequirements.ParseAppDetails("{\"12345\":{\"success\":tr", 12345, DateTime.Now);
        Assert.Null(broken.Game);
        Assert.NotNull(broken.Error);

        Assert.NotNull(SteamRequirements.ParseAppDetails("{\"999\":{}}", 12345, DateTime.Now).Error);
    }

    [Fact]
    public void Urls_use_only_the_steam_store_over_https()
    {
        var url = SteamRequirements.SearchUrl("Gate 3 & co");
        var search = new Uri(url);
        Assert.Equal("https", search.Scheme);
        Assert.Equal(SteamRequirements.StoreHost, search.Host);
        Assert.Contains("term=Gate%203%20%26%20co&", url, StringComparison.Ordinal); // Name ist maskiert, kein Einschleusen weiterer Parameter
        Assert.Contains("cc=CH", search.Query, StringComparison.Ordinal);
        var details = new Uri(SteamRequirements.AppDetailsUrl(990080));
        Assert.Equal(SteamRequirements.StoreHost, details.Host);
        Assert.Contains("appids=990080", details.Query, StringComparison.Ordinal);
        Assert.Contains("filters=basic", details.Query, StringComparison.Ordinal);
    }
}

public class SteamSearchTests
{
    private static readonly SteamSearchItem[] Items =
    [
        new(1091501, "Cyberpunk 2077: Phantom Liberty", "app"),
        new(1091500, "Cyberpunk 2077", "app"),
        new(2138330, "Cyberpunk 2077 Soundtrack", "app"),
    ];

    [Theory]
    [InlineData("cyberpunk 2077", 1091500)]
    [InlineData("Cyberpunk", 1091500)]
    [InlineData("CYBERPUNK 2077 phantom liberty", 1091501)]
    public void Best_match_by_normalized_name(string query, int expected)
        => Assert.Equal(expected, SteamRequirements.PickBestMatch(Items, query)?.AppId);

    [Fact]
    public void No_similar_name_gives_null()
    {
        Assert.Null(SteamRequirements.PickBestMatch(Items, "Zelda"));
        Assert.Null(SteamRequirements.PickBestMatch(Items, " "));
        Assert.Null(SteamRequirements.PickBestMatch([], "Cyberpunk"));
    }

    [Fact]
    public void Search_json_keeps_only_apps()
    {
        const string json = "{\"total\":3,\"items\":[{\"type\":\"app\",\"name\":\"Hogwarts Legacy\",\"id\":990080}," +
                            "{\"type\":\"sub\",\"name\":\"Bundle\",\"id\":5},{\"type\":\"app\",\"name\":\"\",\"id\":7}]}";
        var items = SteamRequirements.ParseSearch(json);
        var item = Assert.Single(items);
        Assert.Equal(990080, item.AppId);
        Assert.Equal("Hogwarts Legacy", item.Name);
        Assert.Empty(SteamRequirements.ParseSearch("kein json"));
        Assert.Empty(SteamRequirements.ParseSearch("{\"total\":0,\"items\":[]}"));
    }
}

public class OnlineGameEstimateTests
{
    [Fact]
    public void Raytracing_and_upscaling_are_na_with_reason_for_online_games()
    {
        var g = SteamSamples.Hogwarts();
        var rt = GameCompatibility.EstimateFps(g, SteamSamples.HighEnd, Resolution.P1080, Quality.High, true, Upscaler.None);
        Assert.Null(rt.FpsLow);
        Assert.Equal(GameCompatibility.OnlineFeatureUnknown, rt.Limit);
        var dlss = GameCompatibility.EstimateFps(g, SteamSamples.HighEnd, Resolution.P1440, Quality.High, false, Upscaler.Dlss);
        Assert.Equal("Keine Angabe zu Raytracing/Upscaling in den Online-Daten", dlss.Limit);
        var plain = GameCompatibility.EstimateFps(g, SteamSamples.HighEnd, Resolution.P1080, Quality.High, false, Upscaler.None);
        Assert.NotNull(plain.FpsLow);
        Assert.Contains("ESTIMATED", plain.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_values_show_na_instead_of_zero()
    {
        var parsed = SteamRequirements.ParseRequirements(SteamSamples.EntityMin, SteamSamples.EntityRec)!.Value;
        var g = new GameInfo("Entity", [], 0, false, false, [], parsed.Minimum, parsed.Recommended, Online: true);
        var r = GameCompatibility.Evaluate(g, SteamSamples.HighEnd);
        var dx = r.Checks.Single(c => c.Area == "DirectX");
        Assert.Equal(CheckStatus.NotAvailable, dx.Status);
        Assert.Equal("N/A", dx.Minimum);
        Assert.NotEqual(CompatibilityRating.Unknown, r.Rating);
    }
}

/// <summary>Alle Tests mit dem globalen Zustand GameCompatibility.UserGames in einer Klasse (laufen nacheinander) – am Ende wird geleert.</summary>
public class UserGamesTests
{
    private static readonly GameRequirement Req = new(["Intel Core i5-6600"], ["NVIDIA GeForce GTX 960"], 8, 2, 10, false, 11);

    [Fact]
    public void Built_in_database_still_loads_without_online_fields()
    {
        Assert.True(GameCompatibility.BuiltInGames.Count >= 40);
        Assert.All(GameCompatibility.BuiltInGames, g => Assert.False(g.Online));
        Assert.All(GameCompatibility.BuiltInGames, g => Assert.Null(g.Source));
    }

    [Fact]
    public void User_games_are_merged_but_never_replace_built_in_games()
    {
        var online = new GameInfo("Zz Testspiel Online V13", [], 99_999_901, false, false, [], Req, Req, Online: true, Source: "Steam (Test)");
        var sameAppId = new GameInfo("Ganz anderer Name V13", [], 1091500, false, false, [], Req, Req, Online: true);
        var sameName = new GameInfo("Cyberpunk 2077", [], 99_999_902, false, false, [], Req, Req, Online: true);
        try
        {
            GameCompatibility.SetUserGames(new[] { online, sameAppId, sameName });
            Assert.Equal(3, GameCompatibility.UserGames.Count);
            Assert.Equal(GameCompatibility.BuiltInGames.Count + 1, GameCompatibility.Games.Count);
            Assert.Same(online, GameCompatibility.Find("Zz Testspiel Online V13"));
            Assert.Same(online, GameCompatibility.FindExact("zz testspiel online v13"));
            Assert.False(GameCompatibility.Find("Cyberpunk 2077")!.Online);
            Assert.False(GameCompatibility.Games.Single(g => g.SteamAppId == 1091500).Online);
        }
        finally
        {
            GameCompatibility.SetUserGames(Array.Empty<GameInfo>());
        }
        Assert.Empty(GameCompatibility.UserGames);
        Assert.Equal(GameCompatibility.BuiltInGames.Count, GameCompatibility.Games.Count);
    }

    [Fact]
    public void Storage_format_round_trips()
    {
        var g = SteamSamples.Online(99_999_903, "Zz Speichertest V13");
        var json = UserGameStore.Serialize([g]);
        var back = Assert.Single(UserGameStore.Deserialize(json));
        Assert.Equal(g.Name, back.Name);
        Assert.Equal(g.SteamAppId, back.SteamAppId);
        Assert.True(back.Online);
        Assert.Equal(g.Source, back.Source);
        Assert.Equal(g.Minimum.Cpu, back.Minimum.Cpu);
        Assert.Equal(g.Recommended.Gpu, back.Recommended.Gpu);
        Assert.Equal(g.Recommended.VramGb, back.Recommended.VramGb);
        Assert.Equal(g.Minimum.Ssd, back.Minimum.Ssd);
        Assert.Equal(g.Recommended.Ssd, back.Recommended.Ssd);
    }

    [Fact]
    public void Storage_format_tolerates_missing_and_null_fields()
    {
        const string json = "{\"version\":1,\"games\":[{\"name\":\"Alt\",\"aliases\":null,\"steam\":5,\"rt\":false,\"upscalers\":[]," +
                            "\"min\":{\"cpu\":[\"Intel Core i5-6600\"],\"gpu\":[],\"ram\":8,\"vram\":0,\"storage\":10,\"ssd\":false,\"dx\":11},\"rec\":null}," +
                            "{\"name\":\"\",\"aliases\":[],\"steam\":0}]}";
        var g = Assert.Single(UserGameStore.Deserialize(json));
        Assert.Equal("Alt", g.Name);
        Assert.Empty(g.Aliases);
        Assert.True(g.Online);
        Assert.Null(g.Source);
        Assert.Empty(g.Recommended.Cpu);
        Assert.Throws<JsonException>(() => UserGameStore.Deserialize("{kaputt"));
    }

    [Fact]
    public void Service_loads_and_removes_saved_games_atomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "games-user.json");
        try
        {
            var g = SteamSamples.Online(99_999_904, "Zz Diensttest V13");
            File.WriteAllText(file, UserGameStore.Serialize([g]));
            var service = new GameRequirementsOnlineService(file);
            var load = service.LoadUserGames();
            Assert.True(load.Success);
            Assert.Single(service.SavedGames);
            Assert.Contains(GameCompatibility.Games, x => x.SteamAppId == 99_999_904 && x.Online);

            var removed = service.Remove(service.SavedGames[0]);
            Assert.True(removed.Success, removed.Message);
            Assert.Empty(service.SavedGames);
            Assert.Empty(UserGameStore.Deserialize(File.ReadAllText(file)));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
            Assert.DoesNotContain(GameCompatibility.Games, x => x.Online);

            Assert.False(service.Remove(GameCompatibility.BuiltInGames[0]).Success); // eingebaute Spiele nicht entfernbar
        }
        finally
        {
            GameCompatibility.SetUserGames(Array.Empty<GameInfo>());
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Service_ignores_broken_file_without_crashing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxv13-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "games-user.json");
        try
        {
            File.WriteAllText(file, "{ kaputt");
            var service = new GameRequirementsOnlineService(file);
            var r = service.LoadUserGames();
            Assert.False(r.Success);
            Assert.Empty(service.SavedGames);
            Assert.True(File.Exists(file + ".broken"));
        }
        finally
        {
            GameCompatibility.SetUserGames(Array.Empty<GameInfo>());
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
