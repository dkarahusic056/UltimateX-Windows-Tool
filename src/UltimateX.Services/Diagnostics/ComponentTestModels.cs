using System.Text.Json;
using System.Text.Json.Serialization;
using UltimateX.Core.Common;

namespace UltimateX.Services.Diagnostics;

/// <summary>Ergebnisstatus eines Komponententests (v1.1 Phase 4).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TestStatus
{
    Pass,
    Warning,
    Fail,
    NotAvailable,
    NotTested,
    UserActionRequired,
}

public static class TestStatusExtensions
{
    public static string Label(this TestStatus s) => s switch
    {
        TestStatus.Pass => "PASS",
        TestStatus.Warning => "WARNING",
        TestStatus.Fail => "FAIL",
        TestStatus.NotAvailable => "NOT AVAILABLE",
        TestStatus.UserActionRequired => "USER ACTION REQUIRED",
        _ => "NOT TESTED",
    };

    public static DataState ToDataState(this TestStatus s) => s switch
    {
        TestStatus.Pass => DataState.Ok,
        TestStatus.Warning or TestStatus.UserActionRequired => DataState.Warning,
        TestStatus.Fail => DataState.Problem,
        _ => DataState.NotAvailable,
    };

    /// <summary>Schwere für Sortierung und Gesamtbewertung (höher = schlechter).</summary>
    public static int Severity(this TestStatus s) => s switch
    {
        TestStatus.Fail => 4,
        TestStatus.Warning => 3,
        TestStatus.UserActionRequired => 2,
        TestStatus.Pass => 1,
        _ => 0,
    };
}

/// <summary>Kategorie für die Gruppierung in der Oberfläche und im Bericht.</summary>
public enum TestCategory { Hardware, Storage, Network, Peripherals, Security, Windows }

/// <summary>Beschreibung eines Tests. <see cref="Interactive"/>-Tests laufen nicht bei „Alle testen“ automatisch.</summary>
public sealed record ComponentTestDefinition(string Id, string Name, TestCategory Category, string Description, bool Interactive = false);

/// <summary>Ergebnis eines Tests. <see cref="Details"/> sind sachliche Einzelbefunde (keine Schätzwerte).</summary>
public sealed record ComponentTestResult(
    string Id,
    string Name,
    TestCategory Category,
    TestStatus Status,
    string Summary,
    IReadOnlyList<string> Details,
    double DurationMs,
    DateTime Time)
{
    public static ComponentTestResult Create(ComponentTestDefinition def, TestStatus status, string summary, IEnumerable<string>? details = null)
        => new(def.Id, def.Name, def.Category, status, summary, details?.ToList() ?? [], 0, DateTime.Now);
}

/// <summary>Ein kompletter Testlauf (für Verlauf, Vergleich und Bericht).</summary>
public sealed record ComponentTestRun(string RunId, DateTime Started, DateTime Finished, IReadOnlyList<ComponentTestResult> Results)
{
    public int Count(TestStatus s) => Results.Count(r => r.Status == s);

    public TestStatus Overall => Results.Count == 0 ? TestStatus.NotTested
        : Results.Any(r => r.Status == TestStatus.Fail) ? TestStatus.Fail
        : Results.Any(r => r.Status == TestStatus.Warning) ? TestStatus.Warning
        : TestStatus.Pass;
}

/// <summary>Veränderung eines Tests zwischen zwei Läufen.</summary>
public sealed record TestChange(string Name, TestStatus? Previous, TestStatus Current)
{
    public bool Improved => Previous is { } p && Current.Severity() < p.Severity();
    public bool Worsened => Previous is { } p && Current.Severity() > p.Severity();
}

/// <summary>
/// Testverlauf (lokal, %LOCALAPPDATA%\UltimateX\ComponentTests\history.json, max. 20 Läufe).
/// Enthält nur Teststatus und Befundtexte; Seriennummern/MAC werden in den Tests nicht erhoben.
/// </summary>
public sealed class ComponentTestHistory
{
    private const int MaxRuns = 20;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly object _gate = new();

    public ComponentTestHistory(string? path = null)
    {
        _path = path ?? Path.Combine(AppPaths.UserDataRoot, "ComponentTests", "history.json");
    }

    public IReadOnlyList<ComponentTestRun> Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path)) return [];
                var info = new FileInfo(_path);
                if (info.Length > 20 * 1024 * 1024) return []; // beschädigte/fremde Datei nicht einlesen
                return JsonSerializer.Deserialize<List<ComponentTestRun>>(File.ReadAllText(_path), Json) ?? [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
            {
                return [];
            }
        }
    }

    public void Save(ComponentTestRun run)
    {
        lock (_gate)
        {
            var runs = Load().ToList();
            runs.Add(run);
            if (runs.Count > MaxRuns) runs.RemoveRange(0, runs.Count - MaxRuns);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(runs, Json));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    /// <summary>Vergleicht zwei Läufe testweise (nur Tests, deren Status sich geändert hat oder die neu sind).</summary>
    public static IReadOnlyList<TestChange> Compare(ComponentTestRun? previous, ComponentTestRun current)
    {
        var prev = previous?.Results.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.Last().Status) ?? [];
        var list = new List<TestChange>();
        foreach (var r in current.Results)
        {
            if (r.Status == TestStatus.NotTested) continue;
            prev.TryGetValue(r.Id, out var p);
            var had = previous is not null && prev.ContainsKey(r.Id);
            if (!had || p != r.Status) list.Add(new TestChange(r.Name, had ? p : null, r.Status));
        }
        return list;
    }
}
