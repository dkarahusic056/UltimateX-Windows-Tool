using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateX.Services.Diagnostics;

/// <summary>Export eines Komponententest-Laufs als TXT oder JSON (PDF über den PC-Bericht, Phase 5).</summary>
public static class ComponentTestReport
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string CategoryName(TestCategory c) => c switch
    {
        TestCategory.Hardware => "Hardware",
        TestCategory.Storage => "Speicher",
        TestCategory.Network => "Netzwerk",
        TestCategory.Peripherals => "Peripherie",
        TestCategory.Security => "Sicherheit",
        _ => "Windows",
    };

    public static string ToText(ComponentTestRun run, IReadOnlyList<TestChange> changes, string appVersion)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ULTIMATE X – SYSTEM COMPONENT TEST REPORT");
        sb.AppendLine($"Version {appVersion} · Lauf {run.RunId[..8]} · {run.Started:dd.MM.yyyy HH:mm:ss} – {run.Finished:HH:mm:ss}");
        sb.AppendLine($"Gesamt: {run.Overall.Label()} · PASS {run.Count(TestStatus.Pass)} · WARNING {run.Count(TestStatus.Warning)} · FAIL {run.Count(TestStatus.Fail)} · " +
                      $"NOT AVAILABLE {run.Count(TestStatus.NotAvailable)} · USER ACTION REQUIRED {run.Count(TestStatus.UserActionRequired)}");
        sb.AppendLine(new string('=', 78));
        foreach (var group in run.Results.GroupBy(r => r.Category))
        {
            sb.AppendLine();
            sb.AppendLine($"[{CategoryName(group.Key).ToUpperInvariant()}]");
            foreach (var r in group)
            {
                sb.AppendLine($"  {r.Status.Label(),-22} {r.Name}: {r.Summary}");
                foreach (var d in r.Details) sb.AppendLine($"      · {d}");
            }
        }
        if (changes.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[ÄNDERUNGEN ZUM LETZTEN LAUF]");
            foreach (var c in changes)
                sb.AppendLine($"  {c.Name}: {(c.Previous?.Label() ?? "neu")} → {c.Current.Label()}");
        }
        sb.AppendLine();
        sb.AppendLine("Alle Tests sind nicht-destruktiv. Werte ohne Messquelle sind als NOT AVAILABLE gekennzeichnet, nicht geschätzt.");
        return sb.ToString();
    }

    public static string ToJson(ComponentTestRun run, IReadOnlyList<TestChange> changes, string appVersion) =>
        JsonSerializer.Serialize(new { Product = "ULTIMATE X Windows Control", Version = appVersion, Run = run, Overall = run.Overall, Changes = changes }, Json);
}
