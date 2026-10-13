using System.Text;
using UltimateX.Core.Common;
using UltimateX.Services.Diagnostics;
using UltimateX.Services.Hardware;
using UltimateX.Services.Network;
using UltimateX.Services.Reports.Pdf;
using UltimateX.Services.Sensors;

namespace UltimateX.Core.Tests;

/// <summary>v1.1 Phase 2: einheitliches Sensormodell und Quellen-Failover.</summary>
public class SensorManagerTests
{
    private static SensorManager NewManager() => new() { IsProcessRunning = _ => false };

    [Theory]
    [InlineData("AMD Ryzen 7 5800X", "Core (Tctl/Tdie)", SensorKind.Temperature, UnifiedSensorId.CpuPackageTemp)]
    [InlineData("Intel Core i7-12700K", "CPU Package", SensorKind.Temperature, UnifiedSensorId.CpuPackageTemp)]
    [InlineData("Intel Core i7-12700K", "CPU Core #1", SensorKind.Temperature, UnifiedSensorId.CpuCoreTemp)]
    [InlineData("AMD Ryzen 7 5800X", "CCD1 (Tdie)", SensorKind.Temperature, UnifiedSensorId.CpuCoreTemp)]
    [InlineData("NVIDIA GeForce RTX 3080", "GPU Core", SensorKind.Temperature, UnifiedSensorId.GpuCoreTemp)]
    [InlineData("NVIDIA GeForce RTX 3080", "GPU Hot Spot", SensorKind.Temperature, UnifiedSensorId.GpuHotspotTemp)]
    [InlineData("NVIDIA GeForce RTX 3080", "GPU Memory Junction Temperature", SensorKind.Temperature, UnifiedSensorId.GpuMemoryTemp)]
    [InlineData("S.M.A.R.T.: Samsung SSD 980 PRO", "Drive Temperature", SensorKind.Temperature, UnifiedSensorId.NvmeTemp)]
    [InlineData("Nuvoton NCT6798D", "VRM MOS", SensorKind.Temperature, UnifiedSensorId.VrmTemp)]
    [InlineData("Nuvoton NCT6798D", "CPU Fan", SensorKind.Fan, UnifiedSensorId.CpuFan)]
    [InlineData("Nuvoton NCT6798D", "Fan #2", SensorKind.Fan, UnifiedSensorId.SystemFan)]
    [InlineData("AMD Ryzen 7 5800X", "Package", SensorKind.Power, UnifiedSensorId.CpuPower)]
    [InlineData("CPU", "CPU Gesamt", SensorKind.Load, UnifiedSensorId.CpuLoad)]
    public void Readings_are_classified(string hw, string name, SensorKind kind, UnifiedSensorId expected)
    {
        Assert.Equal(expected, SensorManager.Classify(new SensorReading(hw, name, kind, 50, "", "HWiNFO")));
    }

    [Fact]
    public void Acpi_zone_is_never_reported_as_cpu_temperature()
    {
        var id = SensorManager.Classify(new SensorReading("Mainboard (ACPI)", "Thermalzone CPUZ", SensorKind.Temperature, 40, "°C", "ACPI-Thermalzone"));
        Assert.Equal(UnifiedSensorId.MainboardTemp, id);
    }

    [Fact]
    public void Failover_prefers_hwinfo_over_lhm_and_native()
    {
        var m = NewManager();
        m.Update(
        [
            new SensorReading("NVIDIA GeForce RTX 3080", "GPU Temperature", SensorKind.Temperature, 61, "°C", "Windows-Grafikkernel"),
            new SensorReading("NVIDIA GeForce RTX 3080", "GPU Core", SensorKind.Temperature, 62, "°C", "LibreHardwareMonitor"),
            new SensorReading("GPU [#0]: NVIDIA GeForce RTX 3080", "GPU Temperature", SensorKind.Temperature, 63, "°C", "HWiNFO"),
        ]);
        var gpu = m.Best(UnifiedSensorId.GpuCoreTemp);
        Assert.NotNull(gpu);
        Assert.Equal(SensorSourceKind.HwInfo, gpu!.Source);
        Assert.Equal(63, gpu.Current);
        Assert.Equal(SensorSourceState.Connected, m.Sources.Single(s => s.Kind == SensorSourceKind.HwInfo).State);
        Assert.Equal(SensorSourceState.Available, m.Sources.Single(s => s.Kind == SensorSourceKind.LibreHardwareMonitor).State);
    }

    [Fact]
    public void Failover_falls_back_when_source_disappears()
    {
        var m = NewManager();
        m.Update([new SensorReading("GPU [#0]: RTX", "GPU Temperature", SensorKind.Temperature, 70, "°C", "HWiNFO")]);
        m.Update([new SensorReading("NVIDIA RTX", "GPU Temperature", SensorKind.Temperature, 65, "°C", "Windows-Grafikkernel")]);
        Assert.Equal(SensorSourceKind.WindowsNative, m.Best(UnifiedSensorId.GpuCoreTemp)!.Source);
        Assert.Equal(SensorSourceState.NotAvailable, m.Sources.Single(s => s.Kind == SensorSourceKind.HwInfo).State);
        Assert.Contains("NOT AVAILABLE", m.Sources.Single(s => s.Kind == SensorSourceKind.HwInfo).Headline);
    }

    [Fact]
    public void Statistics_track_min_average_max()
    {
        var m = NewManager();
        foreach (var v in new[] { 40.0, 60.0, 50.0 })
            m.Update([new SensorReading("Intel Core i5", "CPU Package", SensorKind.Temperature, v, "°C", "HWiNFO")]);
        var s = m.Best(UnifiedSensorId.CpuPackageTemp)!;
        Assert.Equal(40, s.Min);
        Assert.Equal(60, s.Max);
        Assert.Equal(50, s.Average, 3);
        Assert.Equal(3, s.Samples);
    }

    [Fact]
    public void Running_hwinfo_without_shared_memory_is_inactive()
    {
        var m = new SensorManager { IsProcessRunning = names => names.Contains("HWiNFO64") };
        m.Update([]);
        Assert.Equal(SensorSourceState.Inactive, m.Sources.Single(s => s.Kind == SensorSourceKind.HwInfo).State);
    }

    [Fact]
    public void No_values_are_invented_without_sources()
    {
        var m = NewManager();
        m.Update([]);
        Assert.Empty(m.Sensors);
        Assert.Null(m.Best(UnifiedSensorId.CpuPackageTemp));
    }
}

/// <summary>v1.1 Phase 3: Ping-Auswertung und Eingabeprüfung (keine Shell, keine Injection).</summary>
public class LivePingTests
{
    [Theory]
    [InlineData("192.168.178.1", true)]
    [InlineData("fritz.box", true)]
    [InlineData("drucker.local", true)]
    [InlineData("::1", true)]
    [InlineData("8.8.8.8 & del C:\\", false)]
    [InlineData("host;calc.exe", false)]
    [InlineData("$(whoami)", false)]
    [InlineData("-t 1.1.1.1", false)]
    [InlineData("", false)]
    [InlineData("a..b", false)]
    public void Targets_are_validated(string input, bool valid)
    {
        Assert.Equal(valid, LivePingService.TryValidateTarget(input, out _, out _));
    }

    private static PingReplyInfo Ok(int i, long ms) => new(i, DateTime.Now, true, ms, "OK");
    private static PingReplyInfo Lost(int i) => new(i, DateTime.Now, false, null, "Zeitüberschreitung");

    [Fact]
    public void Evaluation_computes_statistics()
    {
        var e = LivePingService.Evaluate("x", null, [Ok(1, 10), Ok(2, 20), Ok(3, 30), Lost(4)]);
        Assert.Equal(4, e.Sent);
        Assert.Equal(3, e.Received);
        Assert.Equal(25, e.LossPercent);
        Assert.Equal(10, e.MinMs);
        Assert.Equal(30, e.MaxMs);
        Assert.Equal(20, e.AvgMs!.Value, 3);
        Assert.Equal(10, e.JitterMs!.Value, 3);
        Assert.Equal(DataState.Problem, e.State);
    }

    [Fact]
    public void Evaluation_reports_unreachable_without_inventing_latency()
    {
        var e = LivePingService.Evaluate("x", null, [Lost(1), Lost(2)]);
        Assert.Equal(DataState.Problem, e.State);
        Assert.Null(e.AvgMs);
        Assert.Null(e.MinMs);
    }

    [Fact]
    public void Stable_connection_is_ok()
    {
        var e = LivePingService.Evaluate("x", null, [Ok(1, 5), Ok(2, 6), Ok(3, 5), Ok(4, 6)]);
        Assert.Equal(DataState.Ok, e.State);
    }

    [Fact]
    public async Task Run_rejects_invalid_target()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => LivePingService.RunAsync("1.1.1.1|calc", 1, _ => { }));
    }
}

/// <summary>v1.1 Phase 4: Komponententest-Logik ohne Systemzugriff.</summary>
public class ComponentTestLogicTests
{
    [Fact]
    public void Cpu_kernels_are_deterministic()
    {
        Assert.Equal(ComponentTestService.IntegerKernel(42, 100_000), ComponentTestService.IntegerKernel(42, 100_000));
        Assert.Equal(ComponentTestService.FloatKernel(10_000), ComponentTestService.FloatKernel(10_000));
        Assert.NotEqual(ComponentTestService.IntegerKernel(1, 1000), ComponentTestService.IntegerKernel(2, 1000));
    }

    [Fact]
    public void Memory_pattern_test_passes_on_working_memory()
    {
        var (ok, _) = ComponentTestService.MemoryPatternTest(8, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public void Every_required_component_has_a_test()
    {
        string[] required = ["cpu", "ram", "gpu", "vram", "ssd", "hdd", "nvme", "mainboard", "bios", "tpm", "secureboot", "ethernet", "wifi",
            "dns", "gateway", "internet", "bluetooth", "usb", "audio", "microphone", "speakers", "camera", "monitors", "keyboard", "mouse",
            "battery", "sensors", "fans", "services", "windowsupdate", "defender", "firewall", "eventlog", "wmi", "search", "printer",
            "storagecontroller", "gpudriver", "chipsetdriver", "systemtime", "winre"];
        var ids = ComponentTestService.Definitions.Select(d => d.Id).ToHashSet();
        Assert.All(required, r => Assert.Contains(r, ids));
        Assert.Equal(ids.Count, ComponentTestService.Definitions.Count); // keine doppelten IDs
    }

    [Fact]
    public void History_compare_reports_changes()
    {
        ComponentTestResult Res(string id, TestStatus s) => ComponentTestResult.Create(ComponentTestService.Definition(id), s, "x");
        var prev = new ComponentTestRun("a", DateTime.Now, DateTime.Now, [Res("cpu", TestStatus.Pass), Res("ram", TestStatus.Pass)]);
        var cur = new ComponentTestRun("b", DateTime.Now, DateTime.Now, [Res("cpu", TestStatus.Fail), Res("ram", TestStatus.Pass), Res("gpu", TestStatus.Pass)]);
        var changes = ComponentTestHistory.Compare(prev, cur);
        Assert.Equal(2, changes.Count);
        Assert.True(changes.Single(c => c.Name == "CPU").Worsened);
        Assert.Null(changes.Single(c => c.Name == "GPU").Previous);
    }

    [Fact]
    public void History_roundtrip_and_limit()
    {
        var path = Path.Combine(Path.GetTempPath(), "uxtest-" + Guid.NewGuid().ToString("N"), "history.json");
        try
        {
            var h = new ComponentTestHistory(path);
            for (var i = 0; i < 25; i++)
                h.Save(new ComponentTestRun(i.ToString(), DateTime.Now, DateTime.Now, [ComponentTestResult.Create(ComponentTestService.Definition("cpu"), TestStatus.Pass, "ok")]));
            var runs = h.Load();
            Assert.Equal(20, runs.Count);
            Assert.Equal("24", runs[^1].RunId);
            Assert.Equal(TestStatus.Pass, runs[^1].Results[0].Status);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Corrupt_history_does_not_crash()
    {
        var dir = Path.Combine(Path.GetTempPath(), "uxtest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "history.json");
            File.WriteAllText(path, "{ kein json ");
            Assert.Empty(new ComponentTestHistory(path).Load());
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Test_tone_is_valid_wav()
    {
        var wav = AudioTest.BuildStereoTone(440, 0.1, 0.2);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal(wav.Length - 8, BitConverter.ToInt32(wav, 4));
    }

    [Fact]
    public void Text_report_contains_status_labels()
    {
        var run = new ComponentTestRun("0123456789abcdef", DateTime.Now, DateTime.Now,
            [ComponentTestResult.Create(ComponentTestService.Definition("cpu"), TestStatus.Fail, "Rechenfehler")]);
        var text = ComponentTestReport.ToText(run, [], "1.1.0");
        Assert.Contains("FAIL", text);
        Assert.Contains("Rechenfehler", text);
        Assert.Contains("\"Fail\"", ComponentTestReport.ToJson(run, [], "1.1.0"));
    }
}

/// <summary>v1.1 Phase 5: echtes PDF, Layout und Datenschutzmodus.</summary>
public class PdfReportTests
{
    private static PcReportData Sample() => new()
    {
        ReportId = "UX-TEST-0001",
        Created = new DateTime(2026, 10, 10, 12, 0, 0),
        AppVersion = "1.1.0",
        UserName = "maxmuster",
        Adapters =
        [
            new AdapterInfo("Ethernet", "Intel(R) Ethernet Controller I225-V", "LAN", true, 1_000_000_000, "A4-BB-6D-12-34-56",
                ["192.168.178.23"], [], ["255.255.255.0"], ["192.168.178.1"], ["192.168.178.1"], true, ["192.168.178.1"]),
        ],
        CpuHistory = Enumerable.Range(0, 60).Select(i => (double)(i % 50)).ToArray(),
        RamHistory = Enumerable.Range(0, 60).Select(_ => 40.0).ToArray(),
    };

    private static string ContentText(byte[] pdf)
    {
        // Inhalt der (komprimierten) Seitenströme entpacken, um Texte zu prüfen.
        var raw = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        var idx = 0;
        while ((idx = raw.IndexOf(">>\nstream\n", idx, StringComparison.Ordinal)) >= 0)
        {
            idx += 10;
            var end = raw.IndexOf("\nendstream", idx, StringComparison.Ordinal);
            var bytes = pdf.AsSpan(idx, end - idx).ToArray();
            using var z = new System.IO.Compression.ZLibStream(new MemoryStream(bytes), System.IO.Compression.CompressionMode.Decompress);
            using var r = new StreamReader(z, Encoding.Latin1);
            sb.Append(r.ReadToEnd());
            idx = end;
        }
        return sb.ToString();
    }

    [Fact]
    public void Pdf_has_valid_structure()
    {
        var doc = PcReportBuilder.Build(Sample(), new PcReportOptions());
        var pdf = PdfWriter.Write(doc);
        var text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF\n", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains($"/Count {doc.Pages.Count}", text);
        // startxref zeigt auf die xref-Tabelle
        var sx = text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10;
        var offset = int.Parse(text[sx..text.IndexOf('\n', sx)]);
        Assert.Equal("xref", text.Substring(offset, 4));
        Assert.True(doc.Pages.Count >= 9); // Übersicht + 9 Abschnitte (je eigene Seite)
    }

    [Fact]
    public void Privacy_mode_masks_mac_ip_and_user()
    {
        var content = ContentText(PdfWriter.Write(PcReportBuilder.Build(Sample(), new PcReportOptions { PrivacyMode = true })));
        Assert.DoesNotContain("A4-BB-6D-12-34-56", content);
        Assert.DoesNotContain("192.168.178.23", content);
        Assert.DoesNotContain("maxmuster", content);
        Assert.Contains("A4-BB-6D-XX-XX-XX", content);
    }

    [Fact]
    public void Without_privacy_mode_full_values_are_included()
    {
        var content = ContentText(PdfWriter.Write(PcReportBuilder.Build(Sample(), new PcReportOptions { PrivacyMode = false })));
        Assert.Contains("A4-BB-6D-12-34-56", content);
        Assert.Contains("192.168.178.23", content);
    }

    [Fact]
    public void Section_toggles_reduce_pages()
    {
        var all = PcReportBuilder.Build(Sample(), new PcReportOptions());
        var few = PcReportBuilder.Build(Sample(), new PcReportOptions
        {
            Hardware = false, Sensors = false, Network = false, Security = false, Drivers = false,
            WindowsHealth = false, ComponentTests = false, Benchmarks = false, CrashAnalysis = false,
        });
        Assert.True(few.Pages.Count < all.Pages.Count);
        Assert.True(few.Pages.Count >= 1);
    }

    [Theory]
    [InlineData("a(b)c", "(a\\(b\\)c)")]
    [InlineData("C:\\x", "(C:\\\\x)")]
    [InlineData("Größe", "(Gr\\366\\337e)")]
    [InlineData("€ – •", "(\\200 \\226 \\225)")]
    [InlineData("→", "(->)")]
    public void Pdf_strings_are_escaped(string input, string expected)
    {
        Assert.Equal(expected, PdfWriter.PdfString(input));
    }

    [Fact]
    public void Text_wrapping_respects_width()
    {
        var lines = FontMetrics.Wrap("Dies ist ein längerer Satz, der auf mehrere Zeilen umbrochen werden muss.", 10, false, 100);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(FontMetrics.Width(l, 10, false) <= 100.01));
        Assert.Equal(5.56, FontMetrics.Width("0", 10, false), 2);
    }
}

/// <summary>v1.1 Security: Named-Pipe-Protokoll zwischen App und Admin-Helper (Schema, Replay, Größenlimits).</summary>
public class HelperPipeProtocolTests
{
    private const string Run = "0123456789abcdef0123456789abcdef";
    private static readonly string Nonce = UltimateX.Core.Processes.HelperPipeProtocol.NewNonce();

    private static UltimateX.Core.Processes.HelperFrame F(string type = "out", string run = Run, string? nonce = null, long seq = 1, string? data = "x", int? code = null, int v = 1)
        => new(v, type, run, nonce ?? Nonce, seq, data, code);

    [Fact]
    public void Valid_frame_passes() =>
        Assert.Null(UltimateX.Core.Processes.HelperPipeProtocol.Validate(F(), Run, Nonce, 0, "out", "exit"));

    [Theory]
    [InlineData("v")]
    [InlineData("type")]
    [InlineData("run")]
    [InlineData("nonce")]
    [InlineData("replay")]
    [InlineData("gap")]
    [InlineData("size")]
    [InlineData("exitcode")]
    public void Invalid_frames_are_rejected(string violation)
    {
        var f = violation switch
        {
            "v" => F(v: 2),
            "type" => F(type: "cmd"),
            "run" => F(run: "ffffffffffffffffffffffffffffffff"),
            "nonce" => F(nonce: UltimateX.Core.Processes.HelperPipeProtocol.NewNonce()),
            "replay" => F(seq: 0),
            "gap" => F(seq: 5),
            "size" => F(data: new string('a', UltimateX.Core.Processes.HelperPipeProtocol.MaxDataChars + 1)),
            _ => F(type: "exit", data: null, code: null),
        };
        Assert.NotNull(UltimateX.Core.Processes.HelperPipeProtocol.Validate(f, Run, Nonce, 0, "out", "exit"));
    }

    [Fact]
    public async Task Frames_roundtrip_through_stream()
    {
        using var ms = new MemoryStream();
        await UltimateX.Core.Processes.HelperPipeProtocol.WriteAsync(ms, F(data: "Größe ✓"), CancellationToken.None);
        await UltimateX.Core.Processes.HelperPipeProtocol.WriteAsync(ms, F(type: "exit", seq: 2, data: null, code: 0), CancellationToken.None);
        ms.Position = 0;
        var a = await UltimateX.Core.Processes.HelperPipeProtocol.ReadAsync(ms, CancellationToken.None);
        var b = await UltimateX.Core.Processes.HelperPipeProtocol.ReadAsync(ms, CancellationToken.None);
        var c = await UltimateX.Core.Processes.HelperPipeProtocol.ReadAsync(ms, CancellationToken.None);
        Assert.Equal("Größe ✓", a!.Data);
        Assert.Equal(0, b!.Code);
        Assert.Null(c);
    }

    [Fact]
    public async Task Oversized_length_prefix_is_rejected()
    {
        using var ms = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F, 1, 2, 3]);
        await Assert.ThrowsAsync<InvalidDataException>(() => UltimateX.Core.Processes.HelperPipeProtocol.ReadAsync(ms, CancellationToken.None));
    }

    [Fact]
    public async Task Garbage_json_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes("{nope");
        var buf = new byte[4 + body.Length];
        BitConverter.GetBytes(body.Length).CopyTo(buf, 0);
        body.CopyTo(buf, 4);
        using var ms = new MemoryStream(buf);
        await Assert.ThrowsAsync<InvalidDataException>(() => UltimateX.Core.Processes.HelperPipeProtocol.ReadAsync(ms, CancellationToken.None));
    }

    [Fact]
    public void Nonce_is_256_bit_hex() => Assert.Matches("^[0-9A-F]{64}$", UltimateX.Core.Processes.HelperPipeProtocol.NewNonce());
}
