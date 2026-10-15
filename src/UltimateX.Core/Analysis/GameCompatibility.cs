using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateX.Core.Analysis;

public sealed record GameRequirement(
    [property: JsonPropertyName("cpu")] IReadOnlyList<string> Cpu,
    [property: JsonPropertyName("gpu")] IReadOnlyList<string> Gpu,
    [property: JsonPropertyName("ram")] int RamGb,
    [property: JsonPropertyName("vram")] int VramGb,
    [property: JsonPropertyName("storage")] int StorageGb,
    [property: JsonPropertyName("ssd")] bool Ssd,
    [property: JsonPropertyName("dx")] int DirectX);

public sealed record GameInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases,
    [property: JsonPropertyName("steam")] int SteamAppId,
    [property: JsonPropertyName("esports")] bool Esports,
    [property: JsonPropertyName("rt")] bool RayTracing,
    [property: JsonPropertyName("upscalers")] IReadOnlyList<string> Upscalers,
    [property: JsonPropertyName("min")] GameRequirement Minimum,
    [property: JsonPropertyName("rec")] GameRequirement Recommended,
    [property: JsonPropertyName("online")] bool Online = false,
    [property: JsonPropertyName("source")] string? Source = null);

internal sealed record GameFile(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("note")] string Note,
    [property: JsonPropertyName("games")] IReadOnlyList<GameInfo> Games);

/// <summary>Daten des eigenen PCs für den Vergleich.</summary>
public sealed record PcProfile(
    string CpuName,
    int? CpuCores,
    string GpuName,
    int? VramGb,
    double? RamGb,
    double? FreeStorageGb,
    bool? SystemDriveIsSsd,
    int? DirectXVersion,
    int? WindowsBuild,
    bool Is64Bit);

public enum CompatibilityRating { Excellent, VeryGood, Good, Playable, Limited, NotRecommended, Unknown }

public sealed record RequirementCheck(string Area, string Mine, string Minimum, string Recommended, CheckStatus Status);

public sealed record CompatibilityResult(GameInfo Game, CompatibilityRating Rating, IReadOnlyList<RequirementCheck> Checks, string Summary)
{
    public string RatingText => Rating switch
    {
        CompatibilityRating.Excellent => "EXCELLENT",
        CompatibilityRating.VeryGood => "VERY GOOD",
        CompatibilityRating.Good => "GOOD",
        CompatibilityRating.Playable => "PLAYABLE",
        CompatibilityRating.Limited => "LIMITED",
        CompatibilityRating.NotRecommended => "NOT RECOMMENDED",
        _ => "N/A",
    };

    public CheckStatus Status => Rating switch
    {
        CompatibilityRating.Excellent or CompatibilityRating.VeryGood or CompatibilityRating.Good => CheckStatus.Pass,
        CompatibilityRating.Playable or CompatibilityRating.Limited => CheckStatus.Warning,
        CompatibilityRating.NotRecommended => CheckStatus.Fail,
        _ => CheckStatus.NotAvailable,
    };
}

public enum Resolution { P1080, P1440, P2160 }

public enum Quality { Low, Medium, High, Ultra }

public enum Upscaler { None, Dlss, Fsr, Xess }

public sealed record FpsEstimate(Resolution Resolution, Quality Quality, bool RayTracing, Upscaler Upscaler, int? FpsLow, int? FpsHigh, string? Limit)
{
    public string Text => FpsLow is null ? "N/A" : $"{FpsLow}–{FpsHigh} FPS (ESTIMATED)";
}

/// <summary>
/// „CAN MY PC RUN IT?“ (v1.2 §13, §14): Vergleich mit einer lokal mitgelieferten Datenbank veröffentlichter
/// Mindest-/Empfehlungsanforderungen. CPU-/GPU-Klassen werden aus den Modellnamen geschätzt (siehe <see cref="HardwareDatabase"/>);
/// alle FPS-Angaben sind Schätzungen und werden als ESTIMATED gekennzeichnet. Unbekannte Hardware → N/A.
/// </summary>
public static class GameCompatibility
{
    private static readonly Lazy<GameFile> Data = new(Load);
    private static readonly object UserLock = new();
    private static IReadOnlyList<GameInfo> _userGames = [];
    private static IReadOnlyList<GameInfo>? _merged;

    /// <summary>Mitgelieferte (eingebettete) Spieledatenbank.</summary>
    public static IReadOnlyList<GameInfo> BuiltInGames => Data.Value.Games;

    /// <summary>Vom Benutzer online abgerufene und lokal gespeicherte Spiele (v1.3).</summary>
    public static IReadOnlyList<GameInfo> UserGames => Volatile.Read(ref _userGames);

    /// <summary>
    /// Alle Spiele: eingebaute Datenbank + gespeicherte Online-Spiele. Ein Online-Spiel mit derselben Steam-AppID oder
    /// demselben Namen wie ein eingebautes Spiel ersetzt dieses nicht (eingebaute Daten enthalten zusätzlich Raytracing/Upscaler).
    /// </summary>
    public static IReadOnlyList<GameInfo> Games => Volatile.Read(ref _merged) ?? BuiltInGames;

    /// <summary>Ersetzt die Liste der gespeicherten Online-Spiele (thread-sicher; leere Liste = keine).</summary>
    public static void SetUserGames(IEnumerable<GameInfo>? games)
    {
        var list = (games ?? Enumerable.Empty<GameInfo>()).Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Name)).ToList();
        lock (UserLock)
        {
            var builtIn = BuiltInGames;
            var appIds = new HashSet<int>(builtIn.Where(g => g.SteamAppId > 0).Select(g => g.SteamAppId));
            var names = new HashSet<string>(builtIn.SelectMany(g => g.Aliases.Prepend(g.Name)).Select(Normalize), StringComparer.Ordinal);
            var merged = new List<GameInfo>(builtIn.Count + list.Count);
            merged.AddRange(builtIn);
            foreach (var g in list)
            {
                if (g.SteamAppId > 0 && !appIds.Add(g.SteamAppId)) continue;
                if (!names.Add(Normalize(g.Name))) continue;
                merged.Add(g);
            }
            Volatile.Write(ref _userGames, list);
            Volatile.Write(ref _merged, list.Count == 0 ? null : merged);
        }
    }

    /// <summary>Exakter Treffer über Name oder Alias (ohne Teilstring-Suche).</summary>
    public static GameInfo? FindExact(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var q = Normalize(query);
        if (q.Length < 2) return null;
        return Games.FirstOrDefault(g => Normalize(g.Name) == q || g.Aliases.Any(a => Normalize(a) == q));
    }

    public static string DatabaseVersion => Data.Value.Version;

    public static string DatabaseNote => Data.Value.Note;

    private static GameFile Load()
    {
        using var stream = typeof(GameCompatibility).Assembly.GetManifestResourceStream("UltimateX.Core.Analysis.games.json");
        if (stream is null) return new GameFile("–", "Spieledatenbank fehlt.", []);
        return JsonSerializer.Deserialize<GameFile>(stream) ?? new GameFile("–", "Spieledatenbank leer.", []);
    }

    /// <summary>Vereinfacht Namen für den Vergleich (Kleinschreibung, nur Buchstaben/Ziffern).</summary>
    public static string Normalize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Sucht ein Spiel nach Name/Alias (exakt, dann Teilstring).</summary>
    public static GameInfo? Find(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        var q = Normalize(query);
        if (q.Length < 2) return null;
        var exact = Games.FirstOrDefault(g => Normalize(g.Name) == q || g.Aliases.Any(a => Normalize(a) == q));
        if (exact is not null) return exact;
        return Games.FirstOrDefault(g => Normalize(g.Name).Contains(q, StringComparison.Ordinal)
                                         || (q.Length >= 5 && q.Contains(Normalize(g.Name), StringComparison.Ordinal))
                                         || g.Aliases.Any(a => Normalize(a).Contains(q, StringComparison.Ordinal)));
    }

    public static IReadOnlyList<GameInfo> Search(string? query, int max = 8)
    {
        if (string.IsNullOrWhiteSpace(query)) return Games.Take(max).ToList();
        var q = Normalize(query);
        return Games.Where(g => Normalize(g.Name).Contains(q, StringComparison.Ordinal) || g.Aliases.Any(a => Normalize(a).Contains(q, StringComparison.Ordinal)))
            .Take(max).ToList();
    }

    /// <summary>Niedrigster bekannter Leistungsindex der genannten Alternativen (jede Alternative erfüllt die Anforderung).</summary>
    public static int? RequiredGpuIndex(IEnumerable<string> names)
        => names.Select(HardwareDatabase.FindGpu).Where(g => g is not null).Select(g => (int?)g!.GameIndex).Min();

    public static int? RequiredCpuIndex(IEnumerable<string> names)
        => names.Select(n => HardwareDatabase.EstimateCpu(n)).Where(c => c.Recognized).Select(c => (int?)c.GameIndex).Min();

    public static CompatibilityResult Evaluate(GameInfo game, PcProfile pc)
    {
        var checks = new List<RequirementCheck>();
        var cpu = HardwareDatabase.EstimateCpu(pc.CpuName, pc.CpuCores);
        var gpu = HardwareDatabase.FindGpu(pc.GpuName);
        var vram = pc.VramGb ?? (gpu is { VramGb: > 0 } ? gpu.VramGb : null);

        // Je Bereich: Verhältnis eigener Wert / Empfehlung (1.0 = genau Empfehlung) – für die Gesamteinstufung.
        var ratios = new List<double>();
        var belowMinimum = false;

        void Compare(string area, double? mine, double? min, double? rec, string mineText, string minText, string recText, bool higherIsBetter = true)
        {
            if (mine is null || (min is null && rec is null))
            {
                checks.Add(new RequirementCheck(area, mine is null ? "N/A" : mineText, minText, recText, CheckStatus.NotAvailable));
                return;
            }
            var meetsMin = min is null || (higherIsBetter ? mine >= min * 0.97 : mine <= min);
            var meetsRec = rec is null || (higherIsBetter ? mine >= rec * 0.97 : mine <= rec);
            var status = meetsRec ? CheckStatus.Pass : meetsMin ? CheckStatus.Warning : CheckStatus.Fail;
            if (!meetsMin) belowMinimum = true;
            checks.Add(new RequirementCheck(area, mineText, minText, recText, status));
            if (rec is > 0 && higherIsBetter) ratios.Add(mine.Value / rec.Value);
        }

        static double? Positive(int v) => v > 0 ? v : null;
        static string Gb(int v) => v > 0 ? $"{v} GB" : "N/A";
        static string Names(IReadOnlyList<string> list) => list.Count == 0 ? "N/A" : string.Join(" / ", list);
        static string Dx(int v) => v > 0 ? $"DirectX {v}" : "N/A";

        var minCpu = RequiredCpuIndex(game.Minimum.Cpu);
        var recCpu = RequiredCpuIndex(game.Recommended.Cpu);
        Compare("CPU", cpu.Recognized ? cpu.GameIndex : null, minCpu, recCpu, pc.CpuName,
            Names(game.Minimum.Cpu), Names(game.Recommended.Cpu));

        var minGpu = RequiredGpuIndex(game.Minimum.Gpu);
        var recGpu = RequiredGpuIndex(game.Recommended.Gpu);
        Compare("GPU", gpu?.GameIndex, minGpu, recGpu, pc.GpuName,
            Names(game.Minimum.Gpu), Names(game.Recommended.Gpu));

        Compare("VRAM", vram, Positive(game.Minimum.VramGb), Positive(game.Recommended.VramGb), vram is null ? "N/A" : $"{vram} GB",
            Gb(game.Minimum.VramGb), Gb(game.Recommended.VramGb));
        Compare("RAM", pc.RamGb is { } r ? Math.Round(r) : null, Positive(game.Minimum.RamGb), Positive(game.Recommended.RamGb),
            pc.RamGb is { } rr ? $"{rr:0} GB" : "N/A", Gb(game.Minimum.RamGb), Gb(game.Recommended.RamGb));
        Compare("DirectX", pc.DirectXVersion, Positive(game.Minimum.DirectX), Positive(game.Recommended.DirectX),
            pc.DirectXVersion is { } dx ? $"DirectX {dx}" : "N/A", Dx(game.Minimum.DirectX), Dx(game.Recommended.DirectX));

        var winOk = pc.Is64Bit && (pc.WindowsBuild is null || pc.WindowsBuild >= 19041);
        checks.Add(new RequirementCheck("Windows", pc.Is64Bit ? $"64 Bit, Build {pc.WindowsBuild?.ToString(CultureInfo.InvariantCulture) ?? "?"}" : "32 Bit",
            "Windows 10 64 Bit", "Windows 10/11 64 Bit", winOk ? CheckStatus.Pass : CheckStatus.Fail));
        if (!winOk) belowMinimum = true;

        var storageNeed = Math.Max(game.Minimum.StorageGb, game.Recommended.StorageGb);
        if (pc.FreeStorageGb is { } free && storageNeed > 0)
        {
            var st = free >= storageNeed * 1.1 ? CheckStatus.Pass : free >= storageNeed ? CheckStatus.Warning : CheckStatus.Fail;
            checks.Add(new RequirementCheck("Speicherplatz", $"{free:0} GB frei", Gb(game.Minimum.StorageGb), Gb(game.Recommended.StorageGb), st));
        }
        else checks.Add(new RequirementCheck("Speicherplatz", pc.FreeStorageGb is { } f ? $"{f:0} GB frei" : "N/A",
            Gb(game.Minimum.StorageGb), Gb(game.Recommended.StorageGb), CheckStatus.NotAvailable));

        var ssdNeeded = game.Minimum.Ssd || game.Recommended.Ssd;
        checks.Add(new RequirementCheck("SSD", pc.SystemDriveIsSsd switch { true => "SSD", false => "HDD", _ => "N/A" },
            game.Minimum.Ssd ? "SSD erforderlich" : "nicht erforderlich", game.Recommended.Ssd ? "SSD empfohlen" : "nicht erforderlich",
            pc.SystemDriveIsSsd switch
            {
                null => CheckStatus.NotAvailable,
                true => CheckStatus.Pass,
                false when game.Minimum.Ssd => CheckStatus.Fail,
                false when ssdNeeded => CheckStatus.Warning,
                _ => CheckStatus.Pass,
            }));
        if (pc.SystemDriveIsSsd == false && game.Minimum.Ssd) belowMinimum = true;

        CompatibilityRating rating;
        string summary;
        var gpuCpuKnown = checks.Where(c => c.Area is "CPU" or "GPU").All(c => c.Status != CheckStatus.NotAvailable);
        if (!gpuCpuKnown)
        {
            rating = CompatibilityRating.Unknown;
            summary = "Für CPU oder GPU liegen keine Vergleichsdaten vor – keine verlässliche Einstufung möglich.";
        }
        else if (belowMinimum)
        {
            rating = CompatibilityRating.NotRecommended;
            summary = "Mindestens eine Mindestanforderung wird nicht erfüllt.";
        }
        else
        {
            var weakest = ratios.Count == 0 ? 1.0 : ratios.Min();
            rating = weakest switch
            {
                >= 2.0 => CompatibilityRating.Excellent,
                >= 1.4 => CompatibilityRating.VeryGood,
                >= 0.97 => CompatibilityRating.Good,
                >= 0.75 => CompatibilityRating.Playable,
                _ => CompatibilityRating.Limited,
            };
            summary = rating switch
            {
                CompatibilityRating.Excellent => "Deutlich über den Empfehlungen – hohe Einstellungen und hohe Bildraten zu erwarten.",
                CompatibilityRating.VeryGood => "Über den Empfehlungen.",
                CompatibilityRating.Good => "Erfüllt die empfohlenen Anforderungen.",
                CompatibilityRating.Playable => "Zwischen Mindest- und Empfehlungsanforderungen – mittlere Einstellungen.",
                _ => "Knapp über den Mindestanforderungen – niedrige Einstellungen.",
            };
        }
        return new CompatibilityResult(game, rating, checks, summary);
    }

    // ------------------------------------------------------------------ FPS-Schätzung

    /// <summary>Begründung, wenn für Online-Spiele Raytracing/Upscaling gewählt ist.</summary>
    public const string OnlineFeatureUnknown = "Keine Angabe zu Raytracing/Upscaling in den Online-Daten";

    private static double ResolutionFactor(Resolution r) => r switch { Resolution.P1080 => 1.0, Resolution.P1440 => 0.68, _ => 0.38 };

    private static double QualityFactor(Quality q) => q switch { Quality.Low => 1.7, Quality.Medium => 1.3, Quality.High => 1.0, _ => 0.8 };

    /// <summary>
    /// Vorsichtige FPS-Schätzung. Annahme: Die empfohlene Hardware erreicht ca. 60 FPS in 1080p mit hohen Details
    /// (übliche Herstellerdefinition). Ergebnis ist ein Bereich (±20 %), begrenzt durch die geschätzte CPU-Leistung.
    /// Ohne Vergleichsdaten wird N/A geliefert.
    /// </summary>
    public static FpsEstimate EstimateFps(GameInfo game, PcProfile pc, Resolution res, Quality quality, bool rayTracing, Upscaler upscaler)
    {
        var gpu = HardwareDatabase.FindGpu(pc.GpuName);
        var cpu = HardwareDatabase.EstimateCpu(pc.CpuName, pc.CpuCores);
        var recGpu = RequiredGpuIndex(game.Recommended.Gpu);
        var recCpu = RequiredCpuIndex(game.Recommended.Cpu);
        if (gpu is null || recGpu is null or 0) return new FpsEstimate(res, quality, rayTracing, upscaler, null, null, "Keine Vergleichsdaten für die GPU");

        // Online-Daten (Steam) enthalten keine Angaben zu Raytracing/Upscaling – nicht als „nicht unterstützt“ werten.
        if (game.Online && (rayTracing || upscaler != Upscaler.None))
            return new FpsEstimate(res, quality, rayTracing, upscaler, null, null, OnlineFeatureUnknown);

        if (rayTracing && (!game.RayTracing || !gpu.HardwareRayTracing))
            return new FpsEstimate(res, quality, rayTracing, upscaler, null, null,
                !game.RayTracing ? "Das Spiel unterstützt kein Raytracing" : "Die GPU unterstützt kein Hardware-Raytracing");

        var name = upscaler switch { Upscaler.Dlss => "DLSS", Upscaler.Fsr => "FSR", Upscaler.Xess => "XeSS", _ => null };
        if (name is not null && !game.Upscalers.Contains(name))
            return new FpsEstimate(res, quality, rayTracing, upscaler, null, null, $"{name} wird von diesem Spiel nicht unterstützt");
        if (upscaler == Upscaler.Dlss && !gpu.Dlss)
            return new FpsEstimate(res, quality, rayTracing, upscaler, null, null, "DLSS erfordert eine NVIDIA-RTX-Grafikkarte");

        var baseFps = game.Esports ? 144.0 : 60.0;
        var gpuFps = baseFps * Math.Pow(gpu.GameIndex / (double)recGpu.Value, 0.9) * ResolutionFactor(res) * QualityFactor(quality);
        if (rayTracing) gpuFps *= gpu.Vendor == GpuVendor.Nvidia ? 0.65 : 0.5;
        gpuFps *= upscaler switch { Upscaler.Dlss => 1.35, Upscaler.Fsr => 1.3, Upscaler.Xess => 1.25, _ => 1.0 };

        string? limit = null;
        var fps = gpuFps;
        if (cpu.Recognized && recCpu is > 0)
        {
            // Die CPU-Grenze hängt kaum von Auflösung/Details ab.
            var cpuFps = baseFps * 1.6 * (cpu.GameIndex / (double)recCpu.Value);
            if (cpuFps < gpuFps) { fps = cpuFps; limit = "CPU-limitiert (geschätzt)"; }
        }

        // Zu wenig VRAM bei hoher Auflösung/Details drückt die Bildrate deutlich.
        var vram = pc.VramGb ?? gpu.VramGb;
        if (vram > 0 && vram < game.Recommended.VramGb && (res != Resolution.P1080 || quality >= Quality.High))
        {
            fps *= 0.75;
            limit = (limit is null ? "" : limit + " · ") + "VRAM knapp";
        }

        var low = (int)Math.Max(1, Math.Round(fps * 0.8));
        var high = (int)Math.Max(low, Math.Round(fps * 1.2));
        return new FpsEstimate(res, quality, rayTracing, upscaler, low, high, limit);
    }

    /// <summary>Welche Bildraten-Ziele (30/60/120/144/240) mit der Schätzung voraussichtlich erreicht werden.</summary>
    public static IReadOnlyList<(int Target, bool Reached)> Targets(FpsEstimate e)
    {
        int[] targets = [30, 60, 120, 144, 240];
        return targets.Select(t => (t, e.FpsLow is { } low && low >= t)).ToList();
    }
}
