using UltimateX.Core.Analysis;
using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

public class V13DriverTests
{
    [Theory]
    [InlineData("32.0.16.1692", "616.92")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("30.0.14.7141", "471.41")]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("27.21.14.5671", "456.71")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("1.2", null)]
    [InlineData("32.0.x.1692", null)]
    public void Windows_driver_version_converts_to_nvidia_version(string? win, string? expected)
        => Assert.Equal(expected, NvidiaDriver.FromWindowsDriverVersion(win));

    [Fact]
    public void Version_comparison_is_numeric()
    {
        Assert.True(NvidiaDriver.IsNewer("617.42", "616.92"));
        Assert.False(NvidiaDriver.IsNewer("616.92", "616.92"));
        Assert.False(NvidiaDriver.IsNewer("560.94", "616.92"));
        Assert.True(NvidiaDriver.IsNewer("617.42", null));
        Assert.True(NvidiaDriver.Compare("1000.01", "999.99") > 0);
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2704&SUBSYS_88CA1043&REV_A1\4&365B4216&0&0008", "NVIDIA GeForce RTX 4080", true)]
    [InlineData(@"PCI\VEN_1002&DEV_744C", "AMD Radeon RX 7900 XTX", false)]
    [InlineData(@"PCI\VEN_8086&DEV_A780", "Intel(R) UHD Graphics 770", false)]
    [InlineData(null, "NVIDIA GeForce GTX 1060", true)]
    public void Nvidia_is_detected_by_vendor_id(string? pnp, string name, bool expected) => Assert.Equal(expected, NvidiaDriver.IsNvidia(pnp, name));

    private const string ProductXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <LookupValueSearch><LookupValues>
          <LookupValue ParentID="127"><Name>NVIDIA GeForce RTX 4080 SUPER</Name><Value>1041</Value></LookupValue>
          <LookupValue ParentID="127"><Name>NVIDIA GeForce RTX 4080</Name><Value>999</Value></LookupValue>
          <LookupValue ParentID="129"><Name>GeForce RTX 4080 Laptop GPU</Name><Value>1005</Value></LookupValue>
          <LookupValue ParentID="120"><Name>GeForce GTX 1060</Name><Value>815</Value></LookupValue>
          <LookupValue ParentID="x"><Name>Kaputt</Name><Value>1</Value></LookupValue>
        </LookupValues></LookupValueSearch>
        """;

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", 127, 999)]
    [InlineData("NVIDIA GeForce RTX 4080 SUPER", 127, 1041)]
    [InlineData("NVIDIA GeForce RTX 4080 Laptop GPU", 129, 1005)]
    [InlineData("NVIDIA  GeForce GTX 1060", 120, 815)]
    public void Product_is_matched_exactly(string gpuName, int psid, int pfid)
    {
        var products = NvidiaDriver.ParseProductList(ProductXml);
        Assert.Equal(4, products.Count);
        var p = NvidiaDriver.FindProduct(products, gpuName);
        Assert.NotNull(p);
        Assert.Equal(psid, p.Psid);
        Assert.Equal(pfid, p.Pfid);
    }

    [Fact]
    public void Unknown_product_or_bad_xml_gives_nothing()
    {
        Assert.Null(NvidiaDriver.FindProduct(NvidiaDriver.ParseProductList(ProductXml), "NVIDIA GeForce RTX 4070"));
        Assert.Empty(NvidiaDriver.ParseProductList("<kein xml"));
        Assert.Contains("psid=127&pfid=999&osID=57", NvidiaDriver.LookupUrl(new NvidiaProduct("x", 127, 999)), StringComparison.Ordinal);
    }

    private static string Json(string url, string version = "617.42")
        => "{\"Success\":\"1\",\"IDS\":[{\"downloadInfo\":{\"Success\":\"1\",\"Version\":\"" + version
           + "\",\"Name\":\"GeForce%20Game%20Ready%20Driver\",\"ReleaseDateTime\":\"Tue Oct 06, 2026\",\"DownloadURLFileSize\":\"990.85 MB\",\"DownloadURL\":\""
           + url + "\",\"DetailsURL\":\"https://www.nvidia.com/x\"}}]}";

    [Fact]
    public void Lookup_is_parsed()
    {
        var d = NvidiaDriver.ParseLookup(Json("https://us.download.nvidia.com/Windows/617.42/617.42-desktop-win10-win11-64bit-international-dch-whql.exe"));
        Assert.NotNull(d);
        Assert.Equal("617.42", d.Version);
        Assert.Equal("GeForce Game Ready Driver", d.Name);
        Assert.Equal("990.85 MB", d.SizeText);
        Assert.Equal("06.10.2026", d.ReleaseDate);
        Assert.Equal("irgendwas", NvidiaDriver.FormatDate("irgendwas"));
    }

    [Theory]
    [InlineData("http://us.download.nvidia.com/Windows/617.42/a.exe")]           // kein HTTPS
    [InlineData("https://us.download.nvidia.com.evil.example/a.exe")]             // fremder Host
    [InlineData("https://download.nvidia.com.cn.example/a.exe")]
    [InlineData("https://www.mediafire.com/a.exe")]
    [InlineData("https://us.download.nvidia.com/Windows/617.42/a.zip")]          // keine .exe
    public void Lookup_with_unofficial_download_is_rejected(string url) => Assert.Null(NvidiaDriver.ParseLookup(Json(url)));

    [Fact]
    public void Lookup_with_bad_version_or_json_is_rejected()
    {
        Assert.Null(NvidiaDriver.ParseLookup(Json("https://us.download.nvidia.com/Windows/1/a.exe", "617.42; rm")));
        Assert.Null(NvidiaDriver.ParseLookup("{}"));
        Assert.Null(NvidiaDriver.ParseLookup("kein json"));
        Assert.Null(NvidiaDriver.ParseLookup("""{"IDS":[]}"""));
    }

    [Fact]
    public void International_download_host_is_official()
        => Assert.True(NvidiaDriver.IsOfficialDownload("https://international.download.nvidia.com/Windows/617.42/a.exe"));

    [Fact]
    public void Install_command_uses_fixed_path_and_no_parameters()
    {
        var spec = CommandCatalog.Build(AllowedCommand.NvidiaDriverInstall);
        Assert.Equal(CommandCatalog.NvidiaDriverPath, spec.FilePath);
        Assert.True(spec.RequiresElevation);
        Assert.Empty(spec.Arguments);
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.NvidiaDriverInstall, "-s"));
    }

    [Fact]
    public void New_catalog_entries_exist()
    {
        foreach (var key in new[] { "onedrive", "winrar", "hwinfo", "inteldsa", "chrome" })
            Assert.NotNull(SoftwareCatalog.Get(key));
        Assert.Contains("chrome", SoftwareCatalog.ForProfile(SetupProfile.Basic).Select(p => p.Key));
        var (_, keys, _) = SoftwareCatalog.Recommend(new SystemFacts { GpuName = "NVIDIA GeForce RTX 4080" }, []);
        Assert.Contains("hwinfo", keys);
    }
}
