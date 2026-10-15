using System.Globalization;

namespace UltimateX.Core.Analysis;

/// <summary>Komponenten des Vergleichs „Expected vs Actual“.</summary>
public enum BenchComponent { Cpu, Gpu, Ram, Storage }

/// <summary>Zusatzangaben zum Datenträger für die Ursachensuche (null = unbekannt).</summary>
public sealed record StorageCauseFacts(
    bool? MeasuredDriveIsSystem = null,
    string? BusType = null,
    double? FreePercent = null,
    double? MaxTempC = null);

/// <summary>Mögliche Ursachen einer Abweichung: erkannte Ursachen oder – wenn nichts erkannt wurde – die allgemeine Prüfliste.</summary>
public sealed record CauseList(IReadOnlyList<string> Items, bool Detected);

/// <summary>
/// v1.3: „Mögliche Ursachen“ je Komponente. In v1.2 wurde eine gemeinsame Liste für alle Komponenten gebildet –
/// dadurch erschien z. B. beim RAM „PCIe-Konfiguration“. Jetzt werden nur Ursachen geprüft und genannt, die die jeweilige
/// Messung tatsächlich beeinflussen können. Die Texte sind deutsche Einzelbegriffe (Übersetzung je Element in der Oberfläche).
/// </summary>
public static class BenchmarkCauses
{
    public const string Temperature = "Temperatur";
    public const string PowerLimit = "Power Limit";
    public const string PowerPlan = "falscher Energieplan";
    public const string Pcie = "PCIe-Konfiguration";
    public const string Driver = "Treiber";
    public const string Background = "Hintergrundprogramme";
    public const string CpuBottleneck = "CPU Bottleneck";
    public const string XmpOff = "XMP/EXPO aus";
    public const string SingleChannel = "Single Channel";
    public const string WrongDrive = "falsches Laufwerk getestet";
    public const string SataInsteadOfNvme = "SATA statt NVMe";
    public const string DriveFull = "Laufwerk fast voll";
    public const string DriveTemperature = "Laufwerkstemperatur";

    /// <summary>Allgemeine Prüfliste je Komponente (wenn keine konkrete Ursache erkannt wurde).</summary>
    public static IReadOnlyList<string> Checklist(BenchComponent c) => c switch
    {
        BenchComponent.Gpu => new[] { Temperature, PowerLimit, Pcie, Driver, Background, CpuBottleneck, PowerPlan },
        BenchComponent.Ram => new[] { XmpOff, SingleChannel, Background, PowerPlan },
        BenchComponent.Storage => new[] { WrongDrive, SataInsteadOfNvme, DriveFull, DriveTemperature, Background },
        _ => new[] { Temperature, PowerLimit, PowerPlan, Background },
    };

    public static CauseList For(BenchComponent c, SystemFacts f, StorageCauseFacts? storage = null)
    {
        var list = new List<string>();
        var background = f.HeavyBackgroundProcesses.Count > 0;
        var saver = f.PowerPlanIsSaver == true;
        switch (c)
        {
            case BenchComponent.Gpu:
                if (f.GpuThrottled == true || f.GpuMaxTempC >= 85) list.Add(Temperature);
                if (f.GpuThrottled == true) list.Add(PowerLimit);
                if (f.PcieCurrentWidth < f.PcieMaxWidth) list.Add(Pcie);
                if (f.GpuDriverAgeDays > 180) list.Add(Driver);
                if (background) list.Add(Background);
                if (f.Scores.Cpu + 20 <= f.Scores.Gpu) list.Add(CpuBottleneck);
                if (saver) list.Add(PowerPlan);
                break;
            case BenchComponent.Ram:
                if (f.RamBelowRatedSpeed == true) list.Add(XmpOff);
                if (f.RamSingleChannel == true) list.Add(SingleChannel);
                if (background) list.Add(Background);
                if (saver) list.Add(PowerPlan);
                break;
            case BenchComponent.Storage:
                if (storage?.MeasuredDriveIsSystem == false) list.Add(WrongDrive);
                if (string.Equals(storage?.BusType, "SATA", StringComparison.OrdinalIgnoreCase)) list.Add(SataInsteadOfNvme);
                if (storage?.FreePercent < 10) list.Add(DriveFull);
                if (storage?.MaxTempC >= 70) list.Add(DriveTemperature);
                if (background) list.Add(Background);
                break;
            default:
                if (f.CpuThrottled == true || f.CpuMaxTempC >= 90) list.Add(Temperature);
                // Drosselung ohne hohe Temperatur deutet eher auf ein Leistungslimit (PL1/PL2, PPT) hin.
                if (f.CpuThrottled == true && f.CpuMaxTempC is not >= 90) list.Add(PowerLimit);
                if (saver) list.Add(PowerPlan);
                if (background) list.Add(Background);
                break;
        }
        return list.Count > 0 ? new CauseList(list, true) : new CauseList(Checklist(c), false);
    }
}

/// <summary>
/// Erwartete sequentielle Lesegeschwindigkeit eines Datenträgers (ESTIMATED) aus Bustyp und – bei NVMe – PCIe-Generation.
/// Ohne eindeutige Generation gibt es keine Erwartung (N/A).
/// </summary>
public static class StorageExpectation
{
    /// <summary>
    /// NVMe Gen3 ≈ 3 000, Gen4 ≈ 6 500, Gen5 ≈ 10 000 MB/s (x4, ESTIMATED); SATA-SSD ≈ 500, SATA-HDD ≈ 150 MB/s.
    /// Mit weniger als 4 PCIe-Lanes sinkt die Erwartung anteilig.
    /// </summary>
    public static double? ExpectedSeqReadMbs(string? busType, string? mediaType, int? pcieGen, int? pcieWidth = null)
    {
        if (string.Equals(busType, "NVMe", StringComparison.OrdinalIgnoreCase))
        {
            double? x4 = pcieGen switch { 3 => 3000d, 4 => 6500d, 5 => 10000d, _ => (double?)null };
            if (x4 is not { } e) return null;
            return pcieWidth is >= 1 and < 4 ? e * pcieWidth.Value / 4 : e;
        }
        if (string.Equals(busType, "SATA", StringComparison.OrdinalIgnoreCase))
        {
            if (mediaType?.Contains("SSD", StringComparison.OrdinalIgnoreCase) == true) return 500;
            if (mediaType?.Contains("HDD", StringComparison.OrdinalIgnoreCase) == true) return 150;
        }
        return null;
    }

    /// <summary>PCIe-Generation aus dem Wert von DEVPKEY_PciDevice_CurrentLinkSpeed (1 = 2,5 GT/s … 5 = 32 GT/s, 6 = 64 GT/s).</summary>
    public static int? PcieGenFromLinkSpeed(int? linkSpeed) => linkSpeed is >= 1 and <= 6 ? linkSpeed : null;

    /// <summary>
    /// Eindeutige Generation aus allen NVMe-Controllern: nur wenn so viele Controller wie NVMe-Datenträger gefunden wurden
    /// und alle dieselbe Generation melden – sonst ist die Zuordnung Laufwerk ↔ Controller nicht sicher (null).
    /// </summary>
    public static (int? Gen, int? Width) UnambiguousNvmeLink(IReadOnlyList<(int? Gen, int? Width)> controllers, int nvmeDiskCount)
    {
        if (controllers.Count == 0 || controllers.Count != nvmeDiskCount) return (null, null);
        var gens = controllers.Select(c => c.Gen).Distinct().ToList();
        if (gens.Count != 1 || gens[0] is null) return (null, null);
        var widths = controllers.Select(c => c.Width).Distinct().ToList();
        return (gens[0], widths.Count == 1 ? widths[0] : null);
    }
}

/// <summary>
/// v1.3: Kennung des getesteten Laufwerks in Datenträger-Läufen. Neue Läufe tragen eine Kennzahl (Laufwerksbuchstabe
/// A = 1 … Z = 26) als eigene Metrik; ältere Läufe erkennt man nur an der Notiz, die mit dem Laufwerk beginnt („D:\ · Testdatei …“).
/// </summary>
public static class DiskRunIdentity
{
    public const string MetricDriveId = "Laufwerk-Kennung";

    /// <summary>Laufwerksbuchstabe aus einem Pfad („C:\“, „c:“, „D:\Daten“) – null, wenn keiner erkennbar ist.</summary>
    public static char? Letter(string? root)
    {
        if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':') return null;
        var c = char.ToUpperInvariant(root[0]);
        return c is >= 'A' and <= 'Z' ? c : null;
    }

    public static double? Encode(string? root) => Letter(root) is { } c ? c - 'A' + 1 : null;

    public static char? Decode(double? id)
        => id is { } v && v >= 1 && v <= 26 && Math.Abs(v - Math.Round(v)) < 1e-9 ? (char)('A' + (int)Math.Round(v) - 1) : null;

    /// <summary>
    /// Gehört ein Lauf zum Laufwerk <paramref name="root"/>? Mit Kennung: Vergleich der Kennung. Ohne Kennung (ältere Läufe):
    /// nur, wenn die Notiz mit dem Laufwerk beginnt.
    /// </summary>
    public static bool IsDrive(double? driveId, string? note, string root)
    {
        var want = Letter(root);
        if (want is null) return false;
        if (driveId is not null) return Decode(driveId) == want;
        var noted = Letter(note?.TrimStart());
        return noted == want;
    }

    /// <summary>Anzeigename „C:\“ aus Kennung oder Notiz (null = unbekannt).</summary>
    public static string? DriveText(double? driveId, string? note)
        => (Decode(driveId) ?? Letter(note?.TrimStart())) is { } c ? c + ":\\" : null;
}

/// <summary>Manuell eingetragenes Geekbench-Ergebnis (Primate Labs). Fließt NICHT in den ULTIMATE-X-Score ein.</summary>
public sealed record GeekbenchEntry(int Version, int SingleCore, int MultiCore, int? GpuScore, string? GpuApi);

/// <summary>Prüfung der manuellen Geekbench-Eingabe und Namen der Verlaufs-Metriken.</summary>
public static class Geekbench
{
    public const string Kind = "Geekbench";
    public const string MetricSingle = "Geekbench Single-Core";
    public const string MetricMulti = "Geekbench Multi-Core";
    public const string MetricGpu = "Geekbench GPU";
    public const string MetricVersion = "Geekbench-Version";
    public const int MaxScore = 2_000_000;

    public static IReadOnlyList<string> GpuApis { get; } = ["OpenCL", "Vulkan"];

    /// <summary>
    /// Liest die Eingabe (ganze Zahlen, Tausenderpunkte/-kommas und Leerzeichen erlaubt). Single- und Multi-Core sind Pflicht,
    /// GPU optional (dann ist eine API OpenCL/Vulkan nötig). Fehler → false mit deutscher Meldung.
    /// </summary>
    public static bool TryParse(int version, string? single, string? multi, string? gpu, string? gpuApi, out GeekbenchEntry? entry, out string error)
    {
        entry = null;
        error = "";
        if (version is not (5 or 6)) { error = "Bitte die Geekbench-Version (5 oder 6) wählen."; return false; }
        if (ParseScore(single) is not { } s) { error = "Bitte einen gültigen Single-Core-Wert eingeben (ganze Zahl > 0)."; return false; }
        if (ParseScore(multi) is not { } m) { error = "Bitte einen gültigen Multi-Core-Wert eingeben (ganze Zahl > 0)."; return false; }
        if (m < s) { error = "Der Multi-Core-Wert ist kleiner als der Single-Core-Wert – bitte die Eingabe prüfen."; return false; }
        int? g = null;
        string? api = null;
        if (!string.IsNullOrWhiteSpace(gpu))
        {
            if (ParseScore(gpu) is not { } gv) { error = "Bitte einen gültigen GPU-Wert eingeben oder das Feld leer lassen."; return false; }
            api = GpuApis.FirstOrDefault(a => string.Equals(a, gpuApi?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (api is null) { error = "Bitte die GPU-Schnittstelle (OpenCL oder Vulkan) wählen."; return false; }
            g = gv;
        }
        entry = new GeekbenchEntry(version, s, m, g, api);
        return true;
    }

    /// <summary>Ganze Zahl 1 … <see cref="MaxScore"/>; Tausendertrenner (Punkt, Komma, Leerzeichen, Apostroph) werden ignoriert.</summary>
    public static int? ParseScore(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var digits = new string(text.Trim().Where(ch => ch is not ('.' or ',' or ' ' or '\u00A0' or '\u202F' or '\'')).ToArray());
        if (digits.Length is 0 or > 9 || !digits.All(char.IsAsciiDigit)) return null;
        var v = int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
        return v is >= 1 and <= MaxScore ? v : null;
    }

    /// <summary>Veränderung in Prozent gegenüber dem vorherigen Lauf derselben Geekbench-Version (null = kein Vergleich).</summary>
    public static double? ChangePercent(double current, double? previous)
        => previous is > 0 ? (current - previous.Value) / previous.Value * 100 : null;
}
