using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateX.Core.NetSec;

public enum PrivacyScope
{
    /// <summary>HKCU – wird direkt (ohne Adminrechte) geschrieben.</summary>
    CurrentUser,
    /// <summary>HKLM – nur über AllowedCommand.PrivacySetMachine (UAC, reg.exe mit festen Argumenten).</summary>
    Machine,
}

public enum PrivacyState
{
    /// <summary>Alle Werte entsprechen der datenschutzfreundlichen Einstellung.</summary>
    Protected,
    /// <summary>Windows-Standard bzw. Freigabe aktiv.</summary>
    Default,
    /// <summary>Nur ein Teil der Werte ist datenschutzfreundlich gesetzt.</summary>
    Partial,
}

/// <summary>
/// Eine schaltbare Datenschutz-Einstellung aus der FESTEN Tabelle. Alle Werte sind REG_DWORD.
/// <see cref="DefaultValue"/> = Wert für „Windows-Standard“ (null = Wert löschen).
/// </summary>
public sealed record PrivacySetting(
    string Id,
    string Title,
    string Description,
    PrivacyScope Scope,
    string KeyPath,
    IReadOnlyList<string> ValueNames,
    int PrivateValue,
    int? DefaultValue,
    IReadOnlyList<int> AllowedMachineValues)
{
    public bool RequiresAdmin => Scope == PrivacyScope.Machine;

    public string RegistryPath => (Scope == PrivacyScope.Machine ? @"HKLM\" : @"HKCU\") + KeyPath;
}

/// <summary>
/// v1.3 Datenschutz: feste Tabelle (id → Schlüssel/Wert/Typ). Keine Windows-Komponenten werden entfernt,
/// keine Hosts-Datei, keine Dienste. HKLM-Parameter: <c>&lt;id&gt;:&lt;0|1|default&gt;</c>.
/// </summary>
public static class PrivacyCatalog
{
    public static IReadOnlyList<PrivacySetting> Settings { get; } =
    [
        new("advertising-id", "Werbe-ID",
            "Apps dürfen die Werbe-ID für personalisierte Werbung nutzen. Datenschutz: aus.",
            PrivacyScope.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", ["Enabled"], 0, 1, []),
        new("suggestions", "Tipps und Vorschläge (Start & Sperrbildschirm)",
            "Vorgeschlagene Inhalte, Tipps und App-Vorschläge im Startmenü und auf dem Sperrbildschirm. Datenschutz: aus.",
            PrivacyScope.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
            ["SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled", "SystemPaneSuggestionsEnabled"], 0, 1, []),
        new("tailored", "Maßgeschneiderte Erfahrungen",
            "Microsoft nutzt Diagnosedaten für personalisierte Tipps und Angebote. Datenschutz: aus.",
            PrivacyScope.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", ["TailoredExperiencesWithDiagnosticDataEnabled"], 0, 1, []),
        new("websearch", "Websuche im Startmenü",
            "Die Suche im Startmenü schickt Eingaben an Bing und zeigt Web-Vorschläge. Datenschutz: Websuche aus (Richtlinie für dieses Konto).",
            PrivacyScope.CurrentUser, @"Software\Policies\Microsoft\Windows\Explorer", ["DisableSearchBoxSuggestions"], 1, null, []),
        new("activity", "Aktivitätsverlauf",
            "Windows speichert, welche Apps, Dateien und Webseiten Sie nutzen (Zeitachse). Datenschutz: aus (gilt für alle Benutzer, Adminrechte).",
            PrivacyScope.Machine, @"SOFTWARE\Policies\Microsoft\Windows\System", ["PublishUserActivities"], 0, null, [0, 1]),
        new("telemetry", "Diagnosedaten nur „Erforderlich“",
            "Begrenzt die an Microsoft gesendeten Diagnosedaten auf die erforderliche Stufe (gilt für alle Benutzer, Adminrechte). Standard: Windows-Einstellung.",
            PrivacyScope.Machine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", ["AllowTelemetry"], 1, null, [1]),
    ];

    public static PrivacySetting? Find(string? id) => Settings.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>Zustand aus den aktuell gelesenen Werten (null = Wert nicht vorhanden).</summary>
    public static PrivacyState Evaluate(PrivacySetting s, IReadOnlyDictionary<string, int?> current)
    {
        var matches = s.ValueNames.Count(n => current.TryGetValue(n, out var v) && v == s.PrivateValue);
        if (matches == s.ValueNames.Count) return PrivacyState.Protected;
        return matches == 0 ? PrivacyState.Default : PrivacyState.Partial;
    }

    public static string StateText(PrivacyState s) => s switch
    {
        PrivacyState.Protected => "Datenschutz aktiv",
        PrivacyState.Partial => "Teilweise",
        _ => "Windows-Standard",
    };

    // ---------------------------------------------------------------- HKLM-Parameter (Admin-Helper)

    public static string MachineParameter(string id, int? value)
    {
        var s = Find(id) ?? throw new ArgumentException("Unbekannte Einstellung.", nameof(id));
        if (s.Scope != PrivacyScope.Machine) throw new ArgumentException("Keine Computer-Einstellung.", nameof(id));
        if (value is { } v && !s.AllowedMachineValues.Contains(v)) throw new ArgumentOutOfRangeException(nameof(value));
        return $"{id}:{(value is { } x ? x.ToString(CultureInfo.InvariantCulture) : "default")}";
    }

    /// <summary>Prüft „&lt;id&gt;:&lt;0|1|default&gt;“ gegen die feste Tabelle. value = null bedeutet „Standard“ (Wert löschen).</summary>
    public static bool TryParseMachineParameter(string? parameter, out PrivacySetting? setting, out int? value)
    {
        setting = null;
        value = null;
        if (parameter is null || parameter.Length > 64) return false;
        var parts = parameter.Split(':');
        if (parts.Length != 2) return false;
        var s = Find(parts[0]);
        if (s is null || s.Scope != PrivacyScope.Machine) return false;
        if (parts[1] == "default")
        {
            setting = s;
            return true;
        }
        if (parts[1] is not ("0" or "1")) return false;
        var v = parts[1][0] - '0';
        if (!s.AllowedMachineValues.Contains(v)) return false;
        setting = s;
        value = v;
        return true;
    }

    /// <summary>reg.exe-Argumente (feste Form, ArgumentList – keine Shell). Nur ein Wert pro Computer-Einstellung.</summary>
    public static IReadOnlyList<string> RegArguments(PrivacySetting s, int? value)
    {
        if (s.Scope != PrivacyScope.Machine || s.ValueNames.Count != 1) throw new ArgumentException("Nur Computer-Einstellungen mit einem Wert.", nameof(s));
        var key = @"HKLM\" + s.KeyPath;
        var name = s.ValueNames[0];
        return value is { } v
            ? ["add", key, "/v", name, "/t", "REG_DWORD", "/d", v.ToString(CultureInfo.InvariantCulture), "/f", "/reg:64"]
            : ["delete", key, "/v", name, "/f", "/reg:64"];
    }

    /// <summary>Parameter, um einen gesicherten Ursprungswert wiederherzustellen; null = mit erlaubten Werten nicht darstellbar.</summary>
    public static string? MachineRestoreParameter(PrivacySetting s, int? original)
    {
        if (s.Scope != PrivacyScope.Machine) return null;
        if (original is null) return MachineParameter(s.Id, null);
        return s.AllowedMachineValues.Contains(original.Value) ? MachineParameter(s.Id, original) : null;
    }
}

/// <summary>
/// Sicherung der ursprünglichen Werte (%LOCALAPPDATA%\UltimateX\privacy-backup.json).
/// Je Einstellung wird nur der ERSTE gesehene Zustand gemerkt, damit „Rückgängig“ immer zum Ausgangszustand führt.
/// </summary>
public sealed class PrivacyBackup
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public int Version { get; set; } = 1;

    /// <summary>id → (Wertname → ursprünglicher Wert; null = war nicht vorhanden).</summary>
    public Dictionary<string, Dictionary<string, int?>> Original { get; set; } = new(StringComparer.Ordinal);

    public DateTime? Created { get; set; }

    public bool Has(string id) => Original.ContainsKey(id);

    /// <summary>Merkt die Werte nur, wenn für diese Einstellung noch keine Sicherung existiert. true = neu gemerkt.</summary>
    public bool Remember(string id, IReadOnlyDictionary<string, int?> values)
    {
        if (Original.ContainsKey(id)) return false;
        Original[id] = new Dictionary<string, int?>(values, StringComparer.Ordinal);
        Created ??= DateTime.Now;
        return true;
    }

    public bool Forget(string id) => Original.Remove(id);

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    /// <summary>Liest die Sicherung; unbekannte IDs/Wertnamen (nicht in der festen Tabelle) werden verworfen.</summary>
    public static PrivacyBackup Deserialize(string json)
    {
        PrivacyBackup? b;
        try { b = JsonSerializer.Deserialize<PrivacyBackup>(json, Json); }
        catch (JsonException) { return new PrivacyBackup(); }
        var result = new PrivacyBackup { Created = b?.Created };
        if (b?.Original is null) return result;
        foreach (var (id, values) in b.Original)
        {
            var s = PrivacyCatalog.Find(id);
            if (s is null || values is null) continue;
            var clean = values.Where(kv => s.ValueNames.Contains(kv.Key, StringComparer.Ordinal))
                              .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (clean.Count > 0) result.Original[id] = clean;
        }
        return result;
    }
}
