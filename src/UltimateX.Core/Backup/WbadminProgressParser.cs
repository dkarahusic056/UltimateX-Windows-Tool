using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace UltimateX.Core.Backup;

public enum WbadminPhase
{
    Starting,
    ShadowCopy,
    Copying,
    Completed,
    Failed,
}

/// <summary>Zustand eines laufenden System-Abbilds (aus der wbadmin-Ausgabe).</summary>
public sealed record WbadminProgress(WbadminPhase Phase, double? Percent, string? Volume, string? LastLine)
{
    public string PhaseText => Phase switch
    {
        WbadminPhase.ShadowCopy => "Schattenkopie wird erstellt",
        WbadminPhase.Copying => Volume is null ? "Daten werden gesichert" : $"Volume {Volume} wird gesichert",
        WbadminPhase.Completed => "Abgeschlossen",
        WbadminPhase.Failed => "Fehlgeschlagen",
        _ => "Wird gestartet",
    };
}

/// <summary>
/// Liest den Fortschritt aus der Ausgabe von <c>wbadmin start backup</c> (deutsch und englisch).
/// Beispiele:
///  EN: "Creating a backup of volume Windows(C:), copied (37%)."
///  DE: "Sicherung von Volume Windows(C:) wird erstellt, 37 % kopiert."
///  EN: "The backup operation successfully completed." · DE: "Der Sicherungsvorgang wurde erfolgreich abgeschlossen."
/// Die Ausgabe kommt in Blöcken an; unvollständige Zeilen werden gepuffert. Unbekannte Zeilen ändern den Zustand nicht.
/// </summary>
public sealed partial class WbadminProgressParser
{
    private readonly StringBuilder _pending = new();

    public WbadminProgress Current { get; private set; } = new(WbadminPhase.Starting, null, null, null);

    [GeneratedRegex(@"(?<!\d)(\d{1,3}(?:[.,]\d{1,2})?)\s?%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    // Volume-Bezeichnung: "volume Windows(C:)", "Volume (EFI-Systempartition) (100.00 MB)" …
    [GeneratedRegex(@"[Vv]olume\s+(.+?)(?:,\s|\s+wird\b|\s+completed\b|\s+wurde\b|\s+erfolgreich\b|$)", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeRegex();

    /// <summary>Verarbeitet einen Ausgabeblock und liefert den aktuellen Zustand.</summary>
    public WbadminProgress Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk)) return Current;
        _pending.Append(chunk.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'));
        var text = _pending.ToString();
        var lastNl = text.LastIndexOf('\n');
        if (lastNl < 0)
        {
            // Fortschrittszeilen enden teils ohne Zeilenumbruch – vollständige Prozentangaben trotzdem auswerten.
            if (PercentRegex().IsMatch(text) && text.TrimEnd().EndsWith('.')) { _pending.Clear(); Apply(text); }
            return Current;
        }
        _pending.Clear();
        _pending.Append(text[(lastNl + 1)..]);
        foreach (var line in text[..lastNl].Split('\n')) Apply(line);
        return Current;
    }

    /// <summary>Am Ende: Restpuffer auswerten.</summary>
    public WbadminProgress Flush()
    {
        if (_pending.Length > 0) { Apply(_pending.ToString()); _pending.Clear(); }
        return Current;
    }

    private void Apply(string raw)
    {
        var line = raw.Trim();
        if (line.Length == 0) return;
        var parsed = ParseLine(line, Current);
        if (parsed is null) return;
        // Endzustände bleiben stehen: In der Zusammenfassung folgen nach "erfolgreich abgeschlossen" noch Zeilen je Volume.
        if (Current.Phase == WbadminPhase.Failed) return;
        if (Current.Phase == WbadminPhase.Completed && parsed.Phase != WbadminPhase.Failed) return;
        Current = parsed;
    }

    /// <summary>Wertet eine einzelne Zeile aus; null = Zeile ohne Bedeutung für den Fortschritt.</summary>
    public static WbadminProgress? ParseLine(string line, WbadminProgress? previous = null)
    {
        previous ??= new WbadminProgress(WbadminPhase.Starting, null, null, null);
        var l = line.Trim();
        if (l.Length == 0) return null;

        if (IsFailureLine(l)) return previous with { Phase = WbadminPhase.Failed, LastLine = l };
        if (IsOverallSuccessLine(l)) return previous with { Phase = WbadminPhase.Completed, Percent = 100, LastLine = l };

        if (l.Contains("shadow copy", StringComparison.OrdinalIgnoreCase) || l.Contains("Schattenkopie", StringComparison.OrdinalIgnoreCase))
            return previous with { Phase = WbadminPhase.ShadowCopy, LastLine = l };

        var isBackupLine = l.Contains("copied", StringComparison.OrdinalIgnoreCase) || l.Contains("kopiert", StringComparison.OrdinalIgnoreCase)
                           || l.Contains("backup of volume", StringComparison.OrdinalIgnoreCase) || l.Contains("Sicherung von Volume", StringComparison.OrdinalIgnoreCase)
                           || l.Contains("Compacting", StringComparison.OrdinalIgnoreCase) || l.Contains("Komprimier", StringComparison.OrdinalIgnoreCase);
        if (!isBackupLine) return null;

        string? volume = previous.Volume;
        var vm = VolumeRegex().Match(l);
        if (vm.Success) volume = vm.Groups[1].Value.Trim().TrimEnd(',', '.').Trim();

        double? percent = null;
        var pm = PercentRegex().Match(l);
        if (pm.Success && double.TryParse(pm.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var p) && p is >= 0 and <= 100)
            percent = p;

        // "The backup of volume X completed successfully" (pro Volume) → 100 % für dieses Volume, Gesamtvorgang läuft weiter.
        if (percent is null && (l.Contains("completed successfully", StringComparison.OrdinalIgnoreCase) || l.Contains("erfolgreich abgeschlossen", StringComparison.OrdinalIgnoreCase)))
            percent = 100;

        if (!vm.Success && percent is null) return null;
        return new WbadminProgress(WbadminPhase.Copying, percent ?? previous.Percent, volume, l);
    }

    private static bool IsOverallSuccessLine(string l)
        => l.Contains("backup operation successfully completed", StringComparison.OrdinalIgnoreCase)
           || l.Contains("backup operation completed successfully", StringComparison.OrdinalIgnoreCase)
           || l.Contains("Sicherungsvorgang wurde erfolgreich abgeschlossen", StringComparison.OrdinalIgnoreCase)
           || l.Contains("Sicherungsvorgang erfolgreich abgeschlossen", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailureLine(string l)
        => l.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || l.StartsWith("FEHLER", StringComparison.OrdinalIgnoreCase)
           || l.Contains("backup operation stopped", StringComparison.OrdinalIgnoreCase)
           || l.Contains("backup operation failed", StringComparison.OrdinalIgnoreCase)
           || l.Contains("Sicherungsvorgang wurde beendet", StringComparison.OrdinalIgnoreCase)
           || l.Contains("Sicherungsvorgang ist fehlgeschlagen", StringComparison.OrdinalIgnoreCase)
           || l.Contains("Sicherungsvorgang fehlgeschlagen", StringComparison.OrdinalIgnoreCase);

    /// <summary>Zeitpunkte aus <c>wbadmin get versions</c> ("Backup time:" / "Sicherungszeit:").</summary>
    public static IReadOnlyList<string> ParseVersionTimes(string output)
    {
        var list = new List<string>();
        foreach (var raw in output.Split('\n'))
        {
            var l = raw.Trim();
            foreach (var key in new[] { "Backup time:", "Sicherungszeit:", "Sicherungszeitpunkt:" })
            {
                if (l.StartsWith(key, StringComparison.OrdinalIgnoreCase)) { list.Add(l[key.Length..].Trim()); break; }
            }
        }
        return list;
    }
}
