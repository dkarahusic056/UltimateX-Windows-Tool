using System.Globalization;

namespace UltimateX.Core.Backup;

/// <summary>Daten eines möglichen Ziellaufwerks (von DriveInfo, für Tests frei setzbar).</summary>
public sealed record ImageTargetDrive(char Letter, string Label, DriveType Type, string Format, bool IsReady, long TotalBytes, long FreeBytes)
{
    public string Display => $"{Letter}: {(string.IsNullOrWhiteSpace(Label) ? "" : Label + " ")}({Format}, {Gb(FreeBytes)} frei von {Gb(TotalBytes)})";

    private static string Gb(long b) => (b / 1024d / 1024 / 1024).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
}

public sealed record ImageTargetVerdict(bool Ok, string Message, long RequiredBytes);

/// <summary>
/// Vorabprüfung für das System-Abbild (wbadmin): Ziel ist ein bereites, fest oder extern angeschlossenes NTFS-Laufwerk,
/// nicht das Systemlaufwerk, mit mindestens belegtem Platz des Systemlaufwerks + 10 % frei.
/// </summary>
public static class SystemImageTargetCheck
{
    public static long RequiredBytes(long systemUsedBytes) => systemUsedBytes + systemUsedBytes / 10;

    public static ImageTargetVerdict Evaluate(ImageTargetDrive? target, char systemDrive, long systemUsedBytes)
    {
        if (target is null) return new(false, "Bitte ein Ziellaufwerk auswählen.", 0);
        var required = RequiredBytes(Math.Max(0, systemUsedBytes));
        if (char.ToUpperInvariant(target.Letter) == char.ToUpperInvariant(systemDrive))
            return new(false, "Das Systemlaufwerk kann nicht Ziel des eigenen Abbilds sein. Bitte eine externe Festplatte wählen.", required);
        if (!target.IsReady) return new(false, $"Laufwerk {target.Letter}: ist nicht bereit.", required);
        if (target.Type is not (DriveType.Fixed or DriveType.Removable))
            return new(false, $"Laufwerk {target.Letter}: ist kein lokal angeschlossener Datenträger (Netzlaufwerke, CD/DVD werden nicht unterstützt).", required);
        if (!string.Equals(target.Format, "NTFS", StringComparison.OrdinalIgnoreCase))
            return new(false, $"Laufwerk {target.Letter}: ist mit {(string.IsNullOrWhiteSpace(target.Format) ? "unbekanntem Dateisystem" : target.Format)} formatiert. Für ein System-Abbild ist NTFS nötig.", required);
        if (target.FreeBytes < required)
            return new(false, $"Zu wenig freier Platz auf {target.Letter}: – benötigt ca. {Gb(required)} (belegter Platz auf dem Systemlaufwerk + 10 %), frei sind {Gb(target.FreeBytes)}.", required);
        return new(true, $"Ziel {target.Letter}: ist geeignet (benötigt ca. {Gb(required)}, frei {Gb(target.FreeBytes)}).", required);
    }

    private static string Gb(long b) => (b / 1024d / 1024 / 1024).ToString("0.0", CultureInfo.GetCultureInfo("de-DE")) + " GB";
}
