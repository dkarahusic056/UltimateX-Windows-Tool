using System.Net;
using UltimateX.Core.Common;
using UltimateX.Services.Health;
using UltimateX.Services.Network;

namespace UltimateX.Core.Tests;

public class ScannerTests
{
    [Theory]
    [InlineData("192.168.1.10", true)]
    [InlineData("10.0.0.5", true)]
    [InlineData("172.16.4.1", true)]
    [InlineData("172.32.0.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("100.64.0.1", false)]
    public void OnlyPrivateAddresses(string ip, bool expected) => Assert.Equal(expected, NetworkScannerService.IsPrivate(IPAddress.Parse(ip)));

    [Fact]
    public void SubnetMath()
    {
        Assert.Equal(IPAddress.Parse("192.168.178.0"), NetworkScannerService.NetworkAddress(IPAddress.Parse("192.168.178.42"), 24));
        var hosts = NetworkScannerService.Hosts(IPAddress.Parse("192.168.178.0"), 24).ToList();
        Assert.Equal(254, hosts.Count);
        Assert.Equal(IPAddress.Parse("192.168.178.1"), hosts[0]);
        Assert.Equal(IPAddress.Parse("192.168.178.254"), hosts[^1]);
        Assert.Equal(1022, NetworkScannerService.Hosts(IPAddress.Parse("10.0.0.0"), 22).Count());
    }

    [Fact]
    public void ParsesIeeeCsvAndDetectsRandomMac()
    {
        var csv = "Registry,Assignment,Organization Name,Organization Address\r\n" +
                  "MA-L,3C2AF4,\"Brother Industries, LTD.\",\"Nagoya, JP\"\r\n" +
                  "MA-M,ABCDEF1,Ignored,X\r\n" +
                  "MA-L,ZZZZZZ,Invalid,X\r\n";
        var map = NetworkScannerService.ParseOui(csv);
        Assert.Single(map);
        Assert.Equal("Brother Industries, LTD.", NetworkScannerService.Vendor("3C:2A:F4:11:22:33", map));
        Assert.Equal("Private/zufällige MAC-Adresse", NetworkScannerService.Vendor("DA:A1:19:00:00:01", map));
        Assert.Null(NetworkScannerService.Vendor(null, map));
    }

    [Fact]
    public void GuessesDeviceTypes()
    {
        Assert.Equal("Drucker", NetworkScannerService.GuessType("Brother Industries", null, []));
        Assert.Equal("NAS", NetworkScannerService.GuessType(null, null, [5000, 5001]));
        Assert.Equal("", NetworkScannerService.GuessType(null, null, []));
    }
}

public class CrashAnalysisTests
{
    [Fact]
    public void CatalogKnowsCommonCodesAndVariants()
    {
        Assert.Equal("IRQL_NOT_LESS_OR_EQUAL", BugCheckCatalog.Lookup(0x0A).Name);
        Assert.Equal(BugCheckCatalog.Lookup(0x7E).Name, BugCheckCatalog.Lookup(0x1000007E).Name);
        Assert.StartsWith("0x", BugCheckCatalog.Lookup(0xDEAD).Name, StringComparison.Ordinal);
    }

    [Fact]
    public void NoEvents_GivesOkHint()
    {
        var hints = CrashAnalysisService.BuildHints([], 0, 0, [], []);
        Assert.Contains(hints, h => h.Severity == DataState.Ok);
    }

    [Fact]
    public void WheaFatal_IsProblem()
    {
        var whea = new[] { new HardwareErrorGroup("x", 18, 1, DateTime.Now, "x") };
        var hints = CrashAnalysisService.BuildHints([], 0, 0, whea, []);
        Assert.Contains(hints, h => h.Severity == DataState.Problem);
    }

    [Fact]
    public void DriverBugchecks_SuggestDebugger()
    {
        var blue = new[] { new BlueScreenEvent(DateTime.Now, 0xD1, "", null, "test") };
        var hints = CrashAnalysisService.BuildHints(blue, 0, 0, [], []);
        Assert.Contains(hints, h => h.Title == "Treiberfehler wahrscheinlich");
    }

    [Fact]
    public void ParsesDebuggerOutput()
    {
        const string output = "DRIVER_IRQL_NOT_LESS_OR_EQUAL (d1)\n\nMODULE_NAME: nvlddmkm\nIMAGE_NAME:  nvlddmkm.sys\nPROCESS_NAME:  System\n" +
                              "FAILURE_BUCKET_ID:  AV_nvlddmkm!unknown\nProbably caused by : nvlddmkm.sys ( nvlddmkm+12345 )\n";
        var a = CrashAnalysisService.ParseDebuggerOutput(output);
        Assert.True(a.DebuggerAvailable);
        Assert.Equal("nvlddmkm.sys", a.Image);
        Assert.Equal("System", a.Process);
        Assert.StartsWith("nvlddmkm.sys", a.ProbablyCausedBy, StringComparison.Ordinal);
        Assert.Equal("DRIVER_IRQL_NOT_LESS_OR_EQUAL (0xd1)", a.BugCheckText);
    }
}
