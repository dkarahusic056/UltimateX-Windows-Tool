using System.Text;
using UltimateX.Core.Common;
using UltimateX.Core.Security;

namespace UltimateX.Core.NetSec;

/// <summary>Markierung einer geplanten Aufgabe nach ihrem ausgeführten Programm.</summary>
public enum TaskMarker
{
    /// <summary>Gültige Signatur von Microsoft.</summary>
    MicrosoftSigned,
    /// <summary>Gültige Signatur eines (anderen) Herausgebers.</summary>
    KnownPublisher,
    /// <summary>Datei im Windows-Ordner ohne eingebettete Signatur (Windows-Dateien sind meist katalogsigniert – nicht geprüft).</summary>
    WindowsComponent,
    /// <summary>Keine oder ungültige Signatur.</summary>
    Unsigned,
    /// <summary>Das eingetragene Programm existiert nicht (verwaiste Aufgabe).</summary>
    ProgramMissing,
    /// <summary>Programm liegt in Temp/AppData (vom Benutzer beschreibbar – typischer Ablageort von Schadsoftware).</summary>
    UserWritableLocation,
    /// <summary>Keine Programm-Aktion (z. B. COM-Handler) – keine Datei zu prüfen.</summary>
    NoProgram,
}

/// <summary>Fakten zum Programm einer Aufgabe (für die testbare Einstufung).</summary>
public sealed record TaskProgramFacts(string? ProgramPath, bool ProgramExists, SignatureStatus? Signature, string? Publisher);

/// <summary>
/// v1.3 Geplante Aufgaben: Einstufung, Anzeige-Texte und der sichere Aufgaben-Token für den Admin-Helper.
/// Aufgabenpfade enthalten Backslashes und Leerzeichen und erfüllen <see cref="CommandCatalog.IsSafeToken"/> daher nicht –
/// sie werden als <c>T&lt;base64url(UTF-8)&gt;</c> übergeben und im Build streng dekodiert und geprüft.
/// </summary>
public static class ScheduledTaskLogic
{
    public const int MaxPathLength = 200;

    /// <summary>Höchstlänge des Tokens (unter der Grenze von <see cref="CommandCatalog.IsSafeToken"/>).</summary>
    public const int MaxTokenLength = 300;

    private static readonly char[] ForbiddenPathChars = ['"', '<', '>', '|', '*', '?', '/', ':'];

    /// <summary>Gültiger Aufgabenpfad: beginnt mit „\“, nur „\“-getrennte, nicht leere Segmente, keine Steuer-/Sonderzeichen.</summary>
    public static bool IsValidTaskPath(string? path)
    {
        if (path is null || path.Length is < 2 or > MaxPathLength || path[0] != '\\' || path[^1] == '\\') return false;
        if (path.Any(c => char.IsControl(c) || char.IsSurrogate(c) || ForbiddenPathChars.Contains(c))) return false;
        if (path.Any(c => c is '\u2028' or '\u2029' or '\uFEFF' or '\u202E' or '\u202D')) return false;
        foreach (var segment in path[1..].Split('\\'))
        {
            if (segment.Length == 0 || segment is "." or "..") return false;
            if (segment != segment.Trim() || segment.EndsWith('.')) return false;
        }
        return true;
    }

    public static string EncodeToken(string taskPath)
    {
        if (!IsValidTaskPath(taskPath)) throw new ArgumentException("Ungültiger Aufgabenpfad.", nameof(taskPath));
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(taskPath)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var token = "T" + b64;
        if (token.Length > MaxTokenLength) throw new ArgumentException("Der Aufgabenpfad ist zu lang für eine sichere Übergabe.", nameof(taskPath));
        return token;
    }

    public static bool TryDecodeToken(string? token, out string taskPath)
    {
        taskPath = string.Empty;
        if (token is null || token.Length is < 3 or > MaxTokenLength || token[0] != 'T') return false;
        foreach (var c in token.AsSpan(1))
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return false;
        }
        var b64 = token[1..].Replace('-', '+').Replace('_', '/');
        if (b64.Length % 4 == 1) return false;
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        string decoded;
        try
        {
            decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(b64));
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
        if (!IsValidTaskPath(decoded)) return false;
        // Kanonische Form erzwingen (keine alternativen Kodierungen desselben Pfads).
        if (!string.Equals(EncodeToken(decoded), token, StringComparison.Ordinal)) return false;
        taskPath = decoded;
        return true;
    }

    /// <summary>Aufgaben unter \Microsoft\Windows\ gehören zu Windows – Änderungen nur mit zusätzlicher Warnung.</summary>
    public static bool IsMicrosoftWindowsTask(string path) => path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Programmpfad aus einer Exec-Aktion: Anführungszeichen entfernen, Umgebungsvariablen auflösen,
    /// relative Namen im System32-Ordner suchen. Nur zur Anzeige/Signaturprüfung – nie zur Ausführung.
    /// </summary>
    public static string? NormalizeProgramPath(string? actionPath, Func<string, string> expand, string system32, Func<string, bool> fileExists)
    {
        if (string.IsNullOrWhiteSpace(actionPath)) return null;
        var p = expand(actionPath.Trim()).Trim().Trim('"').Trim();
        if (p.Length == 0 || p.Length > 1024 || p.Any(char.IsControl)) return null;
        try
        {
            if (Path.IsPathFullyQualified(p)) return Path.GetFullPath(p);
            if (p.Contains('\\') || p.Contains('/')) return p;
            var candidate = Path.Combine(system32, p);
            if (fileExists(candidate)) return candidate;
            if (!p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(candidate + ".exe")) return candidate + ".exe";
            return p;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Einstufung. Reihenfolge: kein Programm → Programm fehlt → Temp/AppData → Signatur.
    /// </summary>
    public static TaskMarker Classify(TaskProgramFacts f, IReadOnlyList<string> userWritableRoots, string windowsDirectory)
    {
        if (string.IsNullOrWhiteSpace(f.ProgramPath)) return TaskMarker.NoProgram;
        if (!f.ProgramExists) return TaskMarker.ProgramMissing;
        if (IsUserWritableLocation(f.ProgramPath, userWritableRoots)) return TaskMarker.UserWritableLocation;
        return f.Signature switch
        {
            SignatureStatus.Valid when f.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true => TaskMarker.MicrosoftSigned,
            SignatureStatus.Valid => TaskMarker.KnownPublisher,
            SignatureStatus.NotSigned when IsWithin(f.ProgramPath, windowsDirectory) => TaskMarker.WindowsComponent,
            _ => TaskMarker.Unsigned,
        };
    }

    /// <summary>Temp/AppData-Pfade (eigene Roots plus jedes „\AppData\“ bzw. „\Temp\“ im Pfad, z. B. anderer Profile).</summary>
    public static bool IsUserWritableLocation(string path, IReadOnlyList<string> userWritableRoots)
    {
        if (userWritableRoots.Any(r => !string.IsNullOrWhiteSpace(r) && IsWithin(path, r))) return true;
        var p = path.Replace('/', '\\');
        return p.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) || p.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase)
               || p.Contains(@"\Tmp\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithin(string path, string root)
    {
        var r = root.Replace('/', '\\').TrimEnd('\\') + "\\";
        return path.Replace('/', '\\').StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static DataState StateOf(TaskMarker m, SignatureStatus? signature) => m switch
    {
        TaskMarker.MicrosoftSigned or TaskMarker.KnownPublisher or TaskMarker.WindowsComponent => DataState.Ok,
        TaskMarker.NoProgram => DataState.NotAvailable,
        TaskMarker.ProgramMissing => DataState.Warning,
        TaskMarker.UserWritableLocation => signature == SignatureStatus.Valid ? DataState.Warning : DataState.Problem,
        _ => signature == SignatureStatus.Tampered ? DataState.Problem : DataState.Warning,
    };

    public static string Label(TaskMarker m) => m switch
    {
        TaskMarker.MicrosoftSigned => "Microsoft-signiert",
        TaskMarker.KnownPublisher => "Bekannter Herausgeber",
        TaskMarker.WindowsComponent => "Windows-Ordner (Katalogsignatur nicht geprüft)",
        TaskMarker.Unsigned => "Unsigniert",
        TaskMarker.ProgramMissing => "Programm fehlt",
        TaskMarker.UserWritableLocation => "Pfad in Temp/AppData",
        _ => "Kein Programm (z. B. COM-Handler)",
    };

    /// <summary>TASK_STATE der Aufgabenplanung.</summary>
    public static string StateText(int state) => state switch
    {
        1 => "Deaktiviert",
        2 => "In Warteschlange",
        3 => "Bereit",
        4 => "Wird ausgeführt",
        _ => "Unbekannt",
    };

    /// <summary>TASK_TRIGGER_TYPE2 der Aufgabenplanung.</summary>
    public static string TriggerText(int type) => type switch
    {
        0 => "Ereignis",
        1 => "Einmalig",
        2 => "Täglich",
        3 => "Wöchentlich",
        4 => "Monatlich",
        5 => "Monatlich (Wochentag)",
        6 => "Im Leerlauf",
        7 => "Bei Erstellung/Änderung",
        8 => "Beim Systemstart",
        9 => "Bei Anmeldung",
        11 => "Sitzungswechsel",
        12 => "Benutzerdefiniert",
        _ => "Sonstiger Auslöser",
    };

    /// <summary>Datum der Aufgabenplanung: 30.12.1899 bzw. Jahre vor 1980 bedeuten „nie“.</summary>
    public static DateTime? ValidRunTime(DateTime value) => value.Year < 1980 ? null : value;
}
