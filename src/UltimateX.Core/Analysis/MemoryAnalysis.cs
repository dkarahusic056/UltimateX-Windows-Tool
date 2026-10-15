using System.Globalization;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Analysis;

/// <summary>Ein RAM-Modul, wie Windows es über SMBIOS meldet (Win32_PhysicalMemory).</summary>
public sealed record RamModuleData(
    string Slot,
    string? Bank,
    ulong? CapacityBytes,
    int? RatedSpeedMTs,
    int? ConfiguredSpeedMTs,
    string? Manufacturer,
    string? PartNumber,
    string? SerialNumber,
    int? SmbiosMemoryType);

public enum ChannelMode { Unknown, Single, Dual, Quad, More }

public sealed record ChannelEstimate(ChannelMode Mode, int? Channels, bool Estimated, string Basis)
{
    public string Text => Mode switch
    {
        ChannelMode.Single => "Single Channel",
        ChannelMode.Dual => "Dual Channel",
        ChannelMode.Quad => "Quad Channel",
        ChannelMode.More => $"{Channels} Kanäle",
        _ => "N/A",
    } + (Estimated && Mode != ChannelMode.Unknown ? " (geschätzt)" : "");
}

public sealed record RamSpeedResult(
    int? CurrentMTs,
    int? JedecMTs,
    int? RatedMTs,
    bool RatedFromPartNumber,
    CheckStatus Status,
    string Message,
    string XmpText);

/// <summary>
/// Auswertung der RAM-Konfiguration (v1.2 §4): Typ, Kanäle, theoretische Bandbreite und ob der Speicher
/// langsamer läuft als vorgesehen. Windows liefert nur SMBIOS-Daten; XMP/EXPO-Profile selbst sind ohne
/// Treiber nicht lesbar. Der Nenntakt wird deshalb – gekennzeichnet – zusätzlich aus der Teilenummer abgeleitet.
/// </summary>
public static partial class MemoryAnalysis
{
    [GeneratedRegex(@"channel\s*([a-h])\b|(?:^|[_\s-])(?:dimm|ch)[_\s-]?([a-h])\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ChannelRegex();

    [GeneratedRegex(@"(?<!\d)(\d{4})(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex FourDigitsRegex();

    // Kingston FURY: KF<DDR-Generation><Takt/100>C…, z. B. KF560C36… (DDR5-6000), KF436C17… (DDR4-3600), KF548C38… (DDR5-4800)
    [GeneratedRegex(@"^KF(\d)(\d{2})C", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KingstonRegex();

    // Kingston ValueRAM / Server: KVR32N22…, KVR48U40… → 3200 / 4800
    [GeneratedRegex(@"^KVR(\d{2})[A-Z]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KingstonValueRegex();

    private static readonly int[] JedecDdr4 = [1600, 1866, 2133, 2400, 2666, 2667, 2933, 3200];
    private static readonly int[] JedecDdr5 = [4000, 4400, 4800, 5200, 5600, 6000, 6400];

    public static string TypeName(int? smbiosType) => smbiosType switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        27 => "LPDDR",
        28 => "LPDDR2",
        29 => "LPDDR3",
        30 => "LPDDR4",
        34 => "DDR5",
        35 => "LPDDR5",
        _ => "N/A",
    };

    /// <summary>Schätzt den Kanalmodus aus den Slot-Bezeichnungen (z. B. „DIMM_A1“, „ChannelB-DIMM0“).</summary>
    public static ChannelEstimate EstimateChannels(IReadOnlyList<RamModuleData> modules)
    {
        var populated = modules.Where(m => m.CapacityBytes is > 0).ToList();
        if (populated.Count == 0) return new ChannelEstimate(ChannelMode.Unknown, null, true, "Keine Module gemeldet");

        var channels = new HashSet<char>();
        foreach (var m in populated)
        {
            var match = ChannelRegex().Match((m.Slot + " " + m.Bank).Trim());
            if (!match.Success) { channels.Clear(); break; }
            var g = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            channels.Add(char.ToUpperInvariant(g[0]));
        }

        if (channels.Count > 0)
            return FromCount(channels.Count, estimated: false, $"Slot-Bezeichnungen: {string.Join(", ", populated.Select(p => p.Slot))}");

        // Ohne verwertbare Bezeichnung: Mainboards bestücken Kanäle paarweise.
        var guess = populated.Count switch
        {
            1 => 1,
            2 or 3 => 2,
            _ => populated.Count >= 8 ? 4 : 2,
        };
        return FromCount(guess, estimated: true, $"{populated.Count} Modul(e), Kanal nicht im Slotnamen erkennbar");
    }

    private static ChannelEstimate FromCount(int count, bool estimated, string basis) => count switch
    {
        1 => new(ChannelMode.Single, 1, estimated, basis),
        2 => new(ChannelMode.Dual, 2, estimated, basis),
        4 => new(ChannelMode.Quad, 4, estimated, basis),
        3 => new(ChannelMode.More, 3, estimated, basis),
        _ => new(ChannelMode.More, count, estimated, basis),
    };

    /// <summary>Theoretische Spitzenbandbreite in GB/s: Kanäle × MT/s × 8 Byte (64-Bit-Kanal).</summary>
    public static double? TheoreticalBandwidthGbs(int? channels, int? mtPerSecond)
        => channels is > 0 && mtPerSecond is > 0 ? channels.Value * mtPerSecond.Value * 8.0 / 1000.0 : null;

    /// <summary>Leitet den beworbenen Takt (XMP/EXPO) aus gängigen Teilenummern ab, z. B. „F5-6000J3038F16G“ → 6000.</summary>
    public static int? RatedSpeedFromPartNumber(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return null;
        var pn = partNumber.Trim();

        var k = KingstonRegex().Match(pn);
        if (k.Success)
            return int.Parse(k.Groups[2].Value, CultureInfo.InvariantCulture) * 100;
        var kv = KingstonValueRegex().Match(pn);
        if (kv.Success)
            return int.Parse(kv.Groups[1].Value, CultureInfo.InvariantCulture) * 100;

        int? best = null;
        foreach (Match m in FourDigitsRegex().Matches(pn))
        {
            var v = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (v is >= 2133 and <= 9600 && (best is null || v > best)) best = v;
        }
        return best;
    }

    /// <summary>
    /// Prüft, ob der RAM unter seinem Nenntakt läuft. Nenntakt = höchster Wert aus SMBIOS-„Speed“ und
    /// aus der Teilenummer abgeleitetem XMP/EXPO-Takt.
    /// </summary>
    public static RamSpeedResult CheckSpeed(IReadOnlyList<RamModuleData> modules)
    {
        var populated = modules.Where(m => m.CapacityBytes is > 0).ToList();
        var current = populated.Select(m => m.ConfiguredSpeedMTs).Where(v => v is > 0).Min();
        var jedec = populated.Select(m => m.RatedSpeedMTs).Where(v => v is > 0).Min();
        var fromPart = populated.Select(m => RatedSpeedFromPartNumber(m.PartNumber)).Where(v => v is > 0).Min();

        if (current is null)
            return new RamSpeedResult(null, jedec, jedec ?? fromPart, false, CheckStatus.NotAvailable,
                "Der aktuelle Speichertakt wird von Windows nicht gemeldet.", "N/A");

        var rated = Math.Max(jedec ?? 0, fromPart ?? 0);
        var ratedFromPart = fromPart is { } fp && fp > (jedec ?? 0);
        if (rated <= 0)
            return new RamSpeedResult(current, jedec, null, false, CheckStatus.NotAvailable,
                $"Aktuell {current} MT/s. Nenntakt nicht ermittelbar.", "N/A");

        string xmp;
        if (jedec is { } j && current > j * 1.03)
            xmp = "XMP/EXPO vermutlich aktiv (Takt über JEDEC-Standard)";
        else if (ratedFromPart && rated > current * 1.03)
            xmp = "XMP/EXPO vermutlich nicht aktiv";
        else
            xmp = "Standardtakt (JEDEC)";

        if (rated > current.Value * 1.03)
        {
            var source = ratedFromPart ? " (laut Teilenummer, XMP/EXPO)" : "";
            return new RamSpeedResult(current, jedec, rated, ratedFromPart, CheckStatus.Warning,
                $"RAM unterstützt {rated} MT/s{source}, läuft aktuell mit {current} MT/s. " +
                "Der Arbeitsspeicher läuft unter seinem Nenntakt – im BIOS/UEFI das XMP- bzw. EXPO-Profil aktivieren.", xmp);
        }
        return new RamSpeedResult(current, jedec, rated, ratedFromPart, CheckStatus.Pass,
            $"RAM läuft mit {current} MT/s (Nenntakt {rated} MT/s).", xmp);
    }

    /// <summary>Für die Plausibilitätsanzeige: ist der Wert ein typischer JEDEC-Standardtakt?</summary>
    public static bool IsJedecSpeed(int mts, int? smbiosType)
        => (smbiosType == 34 ? JedecDdr5 : JedecDdr4).Contains(mts);
}
