using System.Globalization;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Analysis;

public enum GpuVendor { Nvidia, Amd, Intel, Unknown }

/// <summary>
/// Kenndaten einer Grafikkarte. <see cref="Fp32Tflops"/> = Herstellerangabe (Boost-Takt), <see cref="GameIndex"/> =
/// grob gemittelte relative Spieleleistung (RTX 4070 = 100). Beides sind Richtwerte und werden als Schätzung angezeigt.
/// </summary>
public sealed record GpuSpec(string Key, string Name, GpuVendor Vendor, double Fp32Tflops, int VramGb, int GameIndex,
    bool HardwareRayTracing, bool Dlss, bool DualIssueFp32)
{
    /// <summary>Typischer Anteil der theoretischen FP32-Leistung, den ein einfacher FMA-Rechentest erreicht.</summary>
    public double ExpectedComputeEfficiency => DualIssueFp32 ? 0.45 : Vendor == GpuVendor.Nvidia ? 0.80 : 0.70;
}

/// <summary>Geschätzte Leistungsklasse einer CPU (aus dem Modellnamen abgeleitet).</summary>
public sealed record CpuEstimate(string Name, string Family, int? Generation, int SingleIndex, int Cores, int GameIndex, bool Recognized)
{
    /// <summary>Grobe Mehrkernleistung relativ (Einkernindex × Kernskalierung).</summary>
    public int MultiIndex => (int)Math.Round(SingleIndex * Math.Pow(Math.Max(1, Cores) / 6.0, 0.85));
}

/// <summary>
/// Lokale Hardware-Datenbank (v1.2 §10, §13, §14). Es werden keine Online-Daten abgefragt.
/// Unbekannte Modelle liefern null bzw. Recognized = false – es wird dann nichts geschätzt.
/// </summary>
public static partial class HardwareDatabase
{
    private static GpuSpec N(string key, string name, double tf, int vram, int idx, bool rt = true, bool dlss = true)
        => new(key, name, GpuVendor.Nvidia, tf, vram, idx, rt, dlss, false);

    private static GpuSpec A(string key, string name, double tf, int vram, int idx, bool rt = true, bool dual = false)
        => new(key, name, GpuVendor.Amd, tf, vram, idx, rt, false, dual);

    private static GpuSpec I(string key, string name, double tf, int vram, int idx, bool rt = true)
        => new(key, name, GpuVendor.Intel, tf, vram, idx, rt, false, false);

    public static IReadOnlyList<GpuSpec> Gpus { get; } =
    [
        // NVIDIA GeForce RTX 50
        N("rtx 5090", "GeForce RTX 5090", 104.8, 32, 230),
        N("rtx 5080", "GeForce RTX 5080", 56.3, 16, 165),
        N("rtx 5070 ti", "GeForce RTX 5070 Ti", 43.9, 16, 145),
        N("rtx 5070", "GeForce RTX 5070", 30.9, 12, 120),
        N("rtx 5060 ti", "GeForce RTX 5060 Ti", 23.7, 16, 95),
        N("rtx 5060", "GeForce RTX 5060", 19.2, 8, 80),
        // RTX 40
        N("rtx 4090", "GeForce RTX 4090", 82.6, 24, 195),
        N("rtx 4080 super", "GeForce RTX 4080 SUPER", 52.2, 16, 155),
        N("rtx 4080", "GeForce RTX 4080", 48.7, 16, 152),
        N("rtx 4070 ti super", "GeForce RTX 4070 Ti SUPER", 44.1, 16, 135),
        N("rtx 4070 ti", "GeForce RTX 4070 Ti", 40.1, 12, 125),
        N("rtx 4070 super", "GeForce RTX 4070 SUPER", 35.5, 12, 115),
        N("rtx 4070", "GeForce RTX 4070", 29.1, 12, 100),
        N("rtx 4060 ti", "GeForce RTX 4060 Ti", 22.1, 8, 78),
        N("rtx 4060", "GeForce RTX 4060", 15.1, 8, 66),
        // RTX 30
        N("rtx 3090 ti", "GeForce RTX 3090 Ti", 40.0, 24, 120),
        N("rtx 3090", "GeForce RTX 3090", 35.6, 24, 108),
        N("rtx 3080 ti", "GeForce RTX 3080 Ti", 34.1, 12, 105),
        N("rtx 3080", "GeForce RTX 3080", 29.8, 10, 98),
        N("rtx 3070 ti", "GeForce RTX 3070 Ti", 21.7, 8, 82),
        N("rtx 3070", "GeForce RTX 3070", 20.3, 8, 77),
        N("rtx 3060 ti", "GeForce RTX 3060 Ti", 16.2, 8, 68),
        N("rtx 3060", "GeForce RTX 3060", 12.7, 12, 52),
        N("rtx 3050", "GeForce RTX 3050", 9.1, 8, 38),
        // RTX 20
        N("rtx 2080 ti", "GeForce RTX 2080 Ti", 13.4, 11, 80),
        N("rtx 2080 super", "GeForce RTX 2080 SUPER", 11.2, 8, 68),
        N("rtx 2080", "GeForce RTX 2080", 10.1, 8, 64),
        N("rtx 2070 super", "GeForce RTX 2070 SUPER", 9.1, 8, 60),
        N("rtx 2070", "GeForce RTX 2070", 7.5, 8, 53),
        N("rtx 2060 super", "GeForce RTX 2060 SUPER", 7.2, 8, 50),
        N("rtx 2060", "GeForce RTX 2060", 6.5, 6, 44),
        // GTX 16 / 10 (kein Hardware-Raytracing, kein DLSS)
        N("gtx 1660 ti", "GeForce GTX 1660 Ti", 5.4, 6, 36, false, false),
        N("gtx 1660 super", "GeForce GTX 1660 SUPER", 5.0, 6, 35, false, false),
        N("gtx 1660", "GeForce GTX 1660", 5.0, 6, 31, false, false),
        N("gtx 1650 super", "GeForce GTX 1650 SUPER", 4.4, 4, 28, false, false),
        N("gtx 1650", "GeForce GTX 1650", 3.0, 4, 21, false, false),
        N("gtx 1080 ti", "GeForce GTX 1080 Ti", 11.3, 11, 58, false, false),
        N("gtx 1080", "GeForce GTX 1080", 8.9, 8, 46, false, false),
        N("gtx 1070 ti", "GeForce GTX 1070 Ti", 8.2, 8, 42, false, false),
        N("gtx 1070", "GeForce GTX 1070", 6.5, 8, 38, false, false),
        N("gtx 1060", "GeForce GTX 1060", 4.4, 6, 27, false, false),
        N("gtx 1050 ti", "GeForce GTX 1050 Ti", 2.1, 4, 15, false, false),
        N("gtx 970", "GeForce GTX 970", 3.9, 4, 22, false, false),
        // Ältere Karten (vor allem für den Abgleich mit Mindestanforderungen von Spielen)
        N("gtx 980 ti", "GeForce GTX 980 Ti", 6.1, 6, 30, false, false),
        N("gtx 980", "GeForce GTX 980", 5.0, 4, 25, false, false),
        N("gtx 960", "GeForce GTX 960", 2.4, 2, 16, false, false),
        N("gtx 950", "GeForce GTX 950", 1.8, 2, 13, false, false),
        N("gtx 1050", "GeForce GTX 1050", 1.9, 2, 12, false, false),
        N("gtx 780", "GeForce GTX 780", 4.0, 3, 17, false, false),
        N("gtx 770", "GeForce GTX 770", 3.2, 2, 15, false, false),
        N("gtx 760", "GeForce GTX 760", 2.3, 2, 12, false, false),
        N("geforce 760", "GeForce GTX 760", 2.3, 2, 12, false, false),
        N("gtx 750 ti", "GeForce GTX 750 Ti", 1.4, 2, 8, false, false),
        N("gtx 700", "GeForce GTX 700", 2.3, 2, 12, false, false),
        N("gtx 670", "GeForce GTX 670", 2.5, 2, 12, false, false),
        N("gtx 660", "GeForce GTX 660", 1.9, 2, 10, false, false),
        N("gtx 650", "GeForce GTX 650", 0.8, 1, 5, false, false),
        N("gtx 600", "GeForce GTX 600", 1.9, 2, 10, false, false),
        N("gtx 560", "GeForce GTX 560", 1.1, 1, 7, false, false),
        N("gtx 460", "GeForce GTX 460", 0.9, 1, 6, false, false),
        N("gt 640", "GeForce GT 640", 0.7, 1, 3, false, false),
        N("9800 gt", "GeForce 9800 GT", 0.5, 1, 2, false, false),
        N("9600 gt", "GeForce 9600 GT", 0.3, 1, 1, false, false),
        N("8600", "GeForce 8600", 0.1, 1, 1, false, false),
        A("rx 480", "Radeon RX 480", 5.8, 8, 25, rt: false),
        A("rx 470", "Radeon RX 470", 4.9, 4, 21, rt: false),
        A("rx 560", "Radeon RX 560", 2.6, 4, 12, rt: false),
        A("r9 390", "Radeon R9 390", 5.1, 8, 24, rt: false),
        A("r9 380", "Radeon R9 380", 3.5, 4, 17, rt: false),
        A("r9 290", "Radeon R9 290", 4.8, 4, 22, rt: false),
        A("r9 280", "Radeon R9 280", 3.0, 3, 16, rt: false),
        A("r7 370", "Radeon R7 370", 2.0, 2, 12, rt: false),
        A("r7 270x", "Radeon R7 270X", 2.7, 2, 11, rt: false),
        A("r7 260x", "Radeon R7 260X", 2.0, 2, 8, rt: false),
        A("hd 7970", "Radeon HD 7970", 3.8, 3, 18, rt: false),
        A("hd 7870", "Radeon HD 7870", 2.6, 2, 12, rt: false),
        A("hd 7750", "Radeon HD 7750", 0.8, 1, 5, rt: false),
        A("hd 7700", "Radeon HD 7700", 0.8, 1, 5, rt: false),
        A("hd 7000", "Radeon HD 7000", 1.0, 1, 6, rt: false),
        A("hd 6950", "Radeon HD 6950", 2.3, 2, 9, rt: false),
        A("hd 6570", "Radeon HD 6570", 0.5, 1, 2, rt: false),
        A("hd 5870", "Radeon HD 5870", 2.7, 1, 7, rt: false),
        A("hd 4870", "Radeon HD 4870", 1.2, 1, 3, rt: false),
        A("hd 2600", "Radeon HD 2600", 0.2, 1, 1, rt: false),
        A("vega 8", "Radeon Vega 8", 1.1, 0, 7, rt: false),
        A("radeon r5", "Radeon R5", 0.4, 0, 3, rt: false),
        // AMD Radeon RX 9000 / 7000 (RDNA 4/3: Dual-Issue-FP32 – Rechenangabe überzeichnet die Spieleleistung)
        A("rx 9070 xt", "Radeon RX 9070 XT", 48.7, 16, 145, dual: true),
        A("rx 9070", "Radeon RX 9070", 36.1, 16, 128, dual: true),
        A("rx 9060 xt", "Radeon RX 9060 XT", 25.6, 16, 82, dual: true),
        A("rx 7900 xtx", "Radeon RX 7900 XTX", 61.4, 24, 160, dual: true),
        A("rx 7900 xt", "Radeon RX 7900 XT", 51.5, 20, 140, dual: true),
        A("rx 7900 gre", "Radeon RX 7900 GRE", 46.0, 16, 118, dual: true),
        A("rx 7800 xt", "Radeon RX 7800 XT", 37.3, 16, 110, dual: true),
        A("rx 7700 xt", "Radeon RX 7700 XT", 35.2, 12, 92, dual: true),
        A("rx 7600 xt", "Radeon RX 7600 XT", 22.6, 16, 66, dual: true),
        A("rx 7600", "Radeon RX 7600", 21.8, 8, 62, dual: true),
        // RX 6000 / 5000 / Polaris / Vega
        A("rx 6950 xt", "Radeon RX 6950 XT", 23.6, 16, 120),
        A("rx 6900 xt", "Radeon RX 6900 XT", 23.0, 16, 113),
        A("rx 6800 xt", "Radeon RX 6800 XT", 20.7, 16, 108),
        A("rx 6800", "Radeon RX 6800", 16.2, 16, 95),
        A("rx 6750 xt", "Radeon RX 6750 XT", 13.3, 12, 80),
        A("rx 6700 xt", "Radeon RX 6700 XT", 13.2, 12, 76),
        A("rx 6650 xt", "Radeon RX 6650 XT", 10.8, 8, 62),
        A("rx 6600 xt", "Radeon RX 6600 XT", 10.6, 8, 60),
        A("rx 6600", "Radeon RX 6600", 8.9, 8, 52),
        A("rx 6500 xt", "Radeon RX 6500 XT", 5.8, 4, 25),
        A("rx 5700 xt", "Radeon RX 5700 XT", 9.75, 8, 50, rt: false),
        A("rx 5700", "Radeon RX 5700", 7.9, 8, 45, rt: false),
        A("rx 5600 xt", "Radeon RX 5600 XT", 7.2, 6, 40, rt: false),
        A("rx 5500 xt", "Radeon RX 5500 XT", 5.2, 8, 28, rt: false),
        A("rx 590", "Radeon RX 590", 7.1, 8, 27, rt: false),
        A("rx 580", "Radeon RX 580", 6.2, 8, 25, rt: false),
        A("rx 570", "Radeon RX 570", 5.1, 4, 21, rt: false),
        A("vega 64", "Radeon RX Vega 64", 12.7, 8, 40, rt: false),
        A("vega 56", "Radeon RX Vega 56", 10.5, 8, 36, rt: false),
        // Integrierte AMD-Grafik
        A("radeon 890m", "Radeon 890M", 11.9, 0, 30, dual: true),
        A("radeon 780m", "Radeon 780M", 8.9, 0, 25, dual: true),
        A("radeon 760m", "Radeon 760M", 6.5, 0, 20, dual: true),
        A("radeon 680m", "Radeon 680M", 3.4, 0, 20),
        A("radeon 660m", "Radeon 660M", 1.8, 0, 12),
        // Intel Arc
        I("arc b580", "Intel Arc B580", 13.7, 12, 72),
        I("arc b570", "Intel Arc B570", 11.5, 10, 62),
        I("arc a770", "Intel Arc A770", 19.7, 16, 62),
        I("arc a750", "Intel Arc A750", 17.2, 8, 55),
        I("arc a580", "Intel Arc A580", 12.3, 8, 47),
        I("arc a380", "Intel Arc A380", 4.2, 6, 20),
        I("arc 140v", "Intel Arc 140V", 4.0, 0, 24),
        I("iris xe", "Intel Iris Xe Graphics", 2.1, 0, 8, rt: false),
        I("uhd graphics 770", "Intel UHD Graphics 770", 0.8, 0, 5, rt: false),
        I("uhd graphics 630", "Intel UHD Graphics 630", 0.4, 0, 4, rt: false),
        I("uhd graphics", "Intel UHD Graphics", 0.4, 0, 4, rt: false),
        I("hd graphics 4000", "Intel HD Graphics 4000", 0.3, 0, 2, rt: false),
        I("hd graphics", "Intel HD Graphics", 0.3, 0, 3, rt: false),
    ];

    // Längste Schlüssel zuerst, damit „rtx 4070 ti super“ vor „rtx 4070“ geprüft wird.
    private static readonly IReadOnlyList<GpuSpec> GpusByKeyLength = Gpus.OrderByDescending(g => g.Key.Length).ToList();

    public static string NormalizeGpuName(string name)
    {
        var s = name.ToLowerInvariant().Replace("(tm)", " ", StringComparison.Ordinal).Replace("(r)", " ", StringComparison.Ordinal)
            .Replace("nvidia", " ", StringComparison.Ordinal).Replace("geforce", " ", StringComparison.Ordinal)
            .Replace("amd", " ", StringComparison.Ordinal).Replace("laptop gpu", " ", StringComparison.Ordinal);
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return " " + s + " ";
    }

    /// <summary>Findet die Kenndaten einer Grafikkarte über ihren Windows-Namen (z. B. „NVIDIA GeForce RTX 4070 SUPER“).</summary>
    public static GpuSpec? FindGpu(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = NormalizeGpuName(name);
        // Mobile Varianten sind deutlich langsamer – nicht als Desktopkarte bewerten.
        if (name.Contains("Laptop", StringComparison.OrdinalIgnoreCase) || name.Contains("Mobile", StringComparison.OrdinalIgnoreCase))
            return null;
        var compact = n.Replace(" ", "", StringComparison.Ordinal);
        foreach (var g in GpusByKeyLength)
        {
            if (compact.Contains(g.Key.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal)) return g;
        }
        return null;
    }

    // ------------------------------------------------------------------ CPU

    [GeneratedRegex(@"\bi([3579])[-\s]?(\d{4,5})([a-z0-9]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntelCoreRegex();

    [GeneratedRegex(@"\bultra\s*([3579])\s*(\d{3})([a-z]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IntelUltraRegex();

    [GeneratedRegex(@"\bryzen\s*([3579])\s*(?:pro\s*)?(\d{4})([a-z0-9]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RyzenRegex();

    [GeneratedRegex(@"threadripper", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ThreadripperRegex();

    private static readonly Dictionary<int, int> IntelGenSingle = new()
    {
        [2] = 45, [3] = 48, [4] = 52, [5] = 54, [6] = 58, [7] = 62, [8] = 66, [9] = 70, [10] = 74, [11] = 85, [12] = 100, [13] = 110, [14] = 115,
    };

    private static readonly Dictionary<int, int> RyzenGenSingle = new()
    {
        [1] = 55, [2] = 62, [3] = 75, [4] = 82, [5] = 95, [7] = 115, [8] = 105, [9] = 125,
    };

    /// <summary>Schätzt die Leistungsklasse aus dem Modellnamen; <paramref name="knownCores"/> (falls bekannt) ersetzt die Kernschätzung.</summary>
    public static CpuEstimate EstimateCpu(string? name, int? knownCores = null)
    {
        var n = name ?? "";
        int? gen = null;
        int single;
        int cores;
        string family;

        if (IntelUltraRegex().Match(n) is { Success: true } u)
        {
            var tier = int.Parse(u.Groups[1].Value, CultureInfo.InvariantCulture);
            var series = int.Parse(u.Groups[2].Value, CultureInfo.InvariantCulture) / 100; // 1xx Meteor/Lunar Lake, 2xx Arrow Lake
            var mobile = u.Groups[3].Value.Contains('u', StringComparison.OrdinalIgnoreCase) || u.Groups[3].Value.Contains('v', StringComparison.OrdinalIgnoreCase)
                         || u.Groups[3].Value.Contains('h', StringComparison.OrdinalIgnoreCase);
            gen = 14 + series;
            single = (series >= 2 ? 112 : 95) + TierBonus(tier, 6);
            if (mobile) single = (int)(single * 0.85);
            cores = tier switch { 5 => 14, 7 => 20, _ => 24 };
            if (mobile) cores = tier switch { 5 => 12, 7 => 16, _ => 16 };
            family = "Intel Core Ultra";
        }
        else if (IntelCoreRegex().Match(n) is { Success: true } m)
        {
            var tier = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            var digits = m.Groups[2].Value;
            // 5-stellig (12400) oder 4-stellig mit 10–14 am Anfang (Mobilmodelle wie 1165G7, 1360P) → zweistellige Generation.
            gen = digits.Length == 5 || (digits[0] == '1' && digits[1] is >= '0' and <= '4')
                ? int.Parse(digits[..2], CultureInfo.InvariantCulture)
                : int.Parse(digits[..1], CultureInfo.InvariantCulture);
            var suffix = m.Groups[3].Value.ToUpperInvariant();
            single = (IntelGenSingle.TryGetValue(gen.Value, out var s) ? s : 50) + TierBonus(tier, 6);
            if (suffix.Contains('U', StringComparison.Ordinal) || suffix.Contains('Y', StringComparison.Ordinal) || suffix.Contains('G', StringComparison.Ordinal))
                single = (int)(single * 0.8);
            else if (suffix.Contains('H', StringComparison.Ordinal) || suffix.Contains('P', StringComparison.Ordinal)) single = (int)(single * 0.92);
            cores = IntelCores(gen.Value, tier);
            family = "Intel Core";
        }
        else if (ThreadripperRegex().IsMatch(n))
        {
            single = 100;
            cores = 24;
            family = "AMD Ryzen Threadripper";
        }
        else if (RyzenRegex().Match(n) is { Success: true } r)
        {
            var tier = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture);
            var model = r.Groups[2].Value;
            gen = model[0] - '0';
            var suffix = r.Groups[3].Value.ToUpperInvariant();
            single = (RyzenGenSingle.TryGetValue(gen.Value, out var s) ? s : 60) + TierBonus(tier, 4);
            if (suffix.Contains("X3D", StringComparison.Ordinal)) single = (int)(single * 1.10); // großer L3-Cache: deutlich mehr Spieleleistung
            if (suffix.StartsWith('U')) single = (int)(single * 0.8);
            else if (suffix.StartsWith('H')) single = (int)(single * 0.9);
            else if (suffix.StartsWith('G') && gen < 7) single = (int)(single * 0.92);
            cores = tier switch { 3 => 4, 5 => 6, 7 => 8, _ => gen >= 3 ? 12 : 8 };
            if (tier == 9 && model.Length == 4 && model[2] == '5') cores = 16; // 3950X, 5950X, 7950X, 9950X
            family = "AMD Ryzen";
        }
        else
        {
            var lower = n.ToLowerInvariant();
            (single, cores, family) = lower switch
            {
                _ when lower.Contains("xeon", StringComparison.Ordinal) => (60, 8, "Intel Xeon"),
                _ when lower.Contains("pentium", StringComparison.Ordinal) || lower.Contains("celeron", StringComparison.Ordinal) => (32, 2, "Intel Pentium/Celeron"),
                _ when lower.Contains("athlon", StringComparison.Ordinal) => (38, 2, "AMD Athlon"),
                _ when lower.Contains("fx-", StringComparison.Ordinal) || lower.Contains("fx ", StringComparison.Ordinal) => (35, 8, "AMD FX"),
                _ when lower.Contains("snapdragon", StringComparison.Ordinal) => (80, 12, "Qualcomm Snapdragon"),
                _ => (0, 0, "Unbekannt"),
            };
            if (single == 0)
                return new CpuEstimate(n, family, null, 0, knownCores ?? 0, 0, false);
        }

        if (knownCores is > 0) cores = knownCores.Value;
        // Spiele profitieren bis etwa 6–8 Kerne; darunter sinkt die Leistung spürbar.
        var game = (int)Math.Round(single * Math.Min(1.05, Math.Pow(Math.Max(1, cores) / 6.0, 0.35)));
        return new CpuEstimate(n, family, gen, single, cores, game, true);
    }

    private static int TierBonus(int tier, int step) => tier switch { 3 => -2 * step, 5 => 0, 7 => step, 9 => (int)(step * 1.6), _ => 0 };

    private static int IntelCores(int gen, int tier) => (gen, tier) switch
    {
        (<= 7, 3) => 2,
        (<= 7, _) => 4,
        (8 or 9, 3) => 4,
        (8 or 9, 5) => 6,
        (8, 7) => 6,
        (9, 7) => 8,
        (8 or 9, _) => 8,
        (10 or 11, 3) => 4,
        (10 or 11, 5) => 6,
        (10 or 11, 7) => 8,
        (10, _) => 10,
        (11, _) => 8,
        (_, 3) => 4,
        (_, 5) => 10,
        (_, 7) => 16,
        _ => 24,
    };
}
