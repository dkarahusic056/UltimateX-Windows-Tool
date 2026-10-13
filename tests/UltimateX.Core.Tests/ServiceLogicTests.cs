using System.Text;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;

namespace UltimateX.Core.Tests;

/// <summary>Auswertungslogik ohne Systemzugriff: SFC/DISM/CHKDSK-Interpretation und Health Score.</summary>
public class RepairInterpretationTests
{
    [Theory]
    [InlineData("Der Windows-Ressourcenschutz hat keine Integritätsverletzungen gefunden.", RepairOutcome.NoProblems)]
    [InlineData("Windows Resource Protection did not find any integrity violations.", RepairOutcome.NoProblems)]
    [InlineData("Windows Resource Protection found corrupt files and successfully repaired them.", RepairOutcome.Repaired)]
    [InlineData("Der Windows-Ressourcenschutz hat beschädigte Dateien gefunden, konnte jedoch einige davon nicht reparieren.", RepairOutcome.NotRepaired)]
    [InlineData("Windows Resource Protection could not perform the requested operation.", RepairOutcome.CouldNotRun)]
    [InlineData("Irgendetwas Unerwartetes", RepairOutcome.Unknown)]
    public void Sfc_output_is_interpreted(string output, RepairOutcome expected)
    {
        var (outcome, _) = WindowsHealthService.InterpretSfc(output);
        Assert.Equal(expected, outcome);
    }

    [Fact]
    public void Dism_repairable_is_detected()
    {
        var (outcome, _) = WindowsHealthService.InterpretDism(AllowedCommand.DismScanHealth, 0, "The component store is repairable.");
        Assert.Equal(RepairOutcome.RepairableCorruption, outcome);
    }

    [Fact]
    public void Dism_nonzero_exit_is_failure()
    {
        var (outcome, _) = WindowsHealthService.InterpretDism(AllowedCommand.DismRestoreHealth, unchecked((int)0x800F081F), "");
        Assert.Equal(RepairOutcome.CouldNotRun, outcome);
    }

    [Theory]
    [InlineData(0, RepairOutcome.NoProblems)]
    [InlineData(1, RepairOutcome.Repaired)]
    [InlineData(3, RepairOutcome.RepairableCorruption)]
    [InlineData(42, RepairOutcome.Unknown)]
    public void Chkdsk_exit_codes(int code, RepairOutcome expected)
        => Assert.Equal(expected, WindowsHealthService.InterpretChkdsk(code).Item1);
}

public class HealthScoreTests
{
    private static HardwareSnapshot Snapshot(double usedPercent, DataState diskHealth) => new(
        null, [], new MemoryInfo(null, null, null, []),
        new BoardInfo(null, null, null, null, null, null, "UEFI", "Aktiviert", DataState.Ok, "TPM 2.0", DataState.Ok),
        [new PhysicalDiskInfo("SSD", "NVMe SSD", "NVMe", 1000, "x", diskHealth)],
        [new VolumeInfo("C:", null, "NTFS", 1000, (ulong)(1000 - usedPercent * 10))],
        new OsInfo("Windows 11", "24H2", "26100", "64-Bit", null, "PC", null, false),
        []);

    [Fact]
    public void Healthy_system_scores_100()
    {
        var r = HealthScore.Calculate(Snapshot(50, DataState.Ok), [], new RebootStatus(false, []), [], new CrashSummary(0, 0, 0, null, null));
        Assert.Equal(100, r.Score);
        Assert.Empty(r.Factors);
    }

    [Fact]
    public void Every_deduction_has_a_reason()
    {
        var r = HealthScore.Calculate(Snapshot(97, DataState.Problem), null, new RebootStatus(true, ["Test"]), null, new CrashSummary(5, 2, 0, null, null));
        Assert.True(r.Score < 100);
        Assert.All(r.Factors, f => Assert.False(string.IsNullOrWhiteSpace(f.Reason)));
        Assert.Equal(100 - r.Factors.Sum(f => f.Deduction), r.Score);
    }

    [Fact]
    public void Missing_data_gives_no_deduction()
    {
        var r = HealthScore.Calculate(null, null, null, null, null);
        Assert.Equal(100, r.Score);
    }
}

public class OutputDecodingTests
{
    [Fact]
    public void Utf16_sfc_output_is_detected()
    {
        var bytes = Encoding.Unicode.GetBytes("Überprüfung 100 % abgeschlossen.");
        Assert.Equal(Encoding.Unicode, UltimateX.Core.Processes.ProcessRunner.DetectEncoding(bytes));
    }
}

public class WingetParserTests
{
    [Fact]
    public void German_table_is_parsed_by_column_positions()
    {
        const string output = """
            Name                           ID                        Version       Verfügbar     Quelle
            ---------------------------------------------------------------------------------------------
            Mozilla Firefox (x64 de)       Mozilla.Firefox.de        128.0         129.0.1       winget
            Notepad++ (64-bit x64)         Notepad++.Notepad++       8.6.8         8.6.9         winget
            2 Aktualisierungen verfügbar.
            """;
        var list = UltimateX.Services.Updates.WingetService.Parse(output);
        Assert.Equal(2, list.Count);
        Assert.Equal("Mozilla.Firefox.de", list[0].Id);
        Assert.Equal("128.0", list[0].InstalledVersion);
        Assert.Equal("129.0.1", list[0].AvailableVersion);
        Assert.Equal("Notepad++.Notepad++", list[1].Id);
    }

    [Fact]
    public void Garbage_output_yields_no_updates()
        => Assert.Empty(UltimateX.Services.Updates.WingetService.Parse("Fehler: Keine Quelle gefunden."));
}

public class DesktopOrganizerTests
{
    private static readonly UltimateX.Services.Desktop.OrganizerRules Rules = new();

    [Theory]
    [InlineData(@"C:\Users\x\Desktop\Rechnung.PDF", false, "PDF")]
    [InlineData(@"C:\Users\x\Desktop\Chrome.lnk", false, "Programme")]
    [InlineData(@"C:\Users\x\Desktop\Projekte", true, "Ordner")]
    [InlineData(@"C:\Users\x\Desktop\daten.xyz", false, "Andere")]
    [InlineData("::{645FF040-5081-101B-9F08-00AA002F954E}", false, "System")]
    public void Items_are_categorized(string path, bool isFolder, string expected)
        => Assert.Equal(expected, UltimateX.Services.Desktop.DesktopOrganizerService.Categorize(path, isFolder, Rules));

    [Fact]
    public void Invalid_extensions_are_ignored()
    {
        var c = new UltimateX.Services.Desktop.OrganizerCategory("Test", ".pdf ..\\evil .x;y docx");
        Assert.Contains(".pdf", c.ExtensionSet);
        Assert.Contains(".docx", c.ExtensionSet);
        Assert.DoesNotContain("..\\evil", c.ExtensionSet);
    }
}

public class DnsQueryTests
{
    [Fact]
    public void Query_has_header_and_labels()
    {
        var q = UltimateX.Services.Network.NetworkService.BuildDnsQuery(0x1234, "www.microsoft.com");
        Assert.Equal(0x12, q[0]);
        Assert.Equal(0x34, q[1]);
        Assert.Equal(3, q[12]);                 // Länge "www"
        Assert.Equal(new byte[] { 0, 1, 0, 1 }, q[^4..]); // QTYPE A, QCLASS IN
    }

    [Fact]
    public void Overlong_label_is_rejected()
        => Assert.Throws<ArgumentException>(() => UltimateX.Services.Network.NetworkService.BuildDnsQuery(1, new string('a', 64) + ".de"));
}
