using System.Net;
using System.Net.NetworkInformation;
using UltimateX.Core.Analysis;
using UltimateX.Core.NetSec;
using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

// v1.3 Netzwerk-Werkzeuge, Sicherheits-Check, Geplante Aufgaben, Datenschutz – alles offline und ohne Adminrechte.

public class V13TracerouteTests
{
    private static readonly IPAddress Router = IPAddress.Parse("192.168.1.1");
    private static readonly IPAddress Target = IPAddress.Parse("93.184.216.34");

    [Theory]
    [InlineData(IPStatus.Success, HopKind.Destination)]
    [InlineData(IPStatus.TtlExpired, HopKind.Router)]
    [InlineData(IPStatus.TimeExceeded, HopKind.Router)]
    [InlineData(IPStatus.TimedOut, HopKind.NoReply)]
    [InlineData(IPStatus.DestinationHostUnreachable, HopKind.Unreachable)]
    [InlineData(IPStatus.DestinationNetworkUnreachable, HopKind.Unreachable)]
    [InlineData(IPStatus.BadRoute, HopKind.Error)]
    public void Status_is_classified(IPStatus status, HopKind expected) => Assert.Equal(expected, TracerouteLogic.Classify(status));

    [Fact]
    public void Router_hop_with_one_lost_probe()
    {
        var hop = TracerouteLogic.Combine(1, [new(IPStatus.TtlExpired, Router, 2), new(IPStatus.TimedOut, IPAddress.Any, null), new(IPStatus.TtlExpired, Router, 0)]);
        Assert.Equal(HopKind.Router, hop.Kind);
        Assert.Equal(Router, hop.Address);
        Assert.Equal("2 ms   *   <1 ms", hop.TimesText);
        Assert.False(TracerouteLogic.IsFinished(hop, 0));
    }

    [Fact]
    public void Silent_hop_shows_star()
    {
        var hop = TracerouteLogic.Combine(4, [new(IPStatus.TimedOut, IPAddress.Any, null), new(IPStatus.TimedOut, null, null), new(IPStatus.TimedOut, null, null)]);
        Assert.Equal(HopKind.NoReply, hop.Kind);
        Assert.Null(hop.Address);
        Assert.Equal("*", hop.AddressText);
        Assert.False(TracerouteLogic.IsFinished(hop, 1));
        Assert.True(TracerouteLogic.IsFinished(hop, TracerouteLogic.MaxSilentHops));
    }

    [Fact]
    public void Destination_ends_the_trace()
    {
        var hop = TracerouteLogic.Combine(9, [new(IPStatus.TimedOut, null, null), new(IPStatus.Success, Target, 21), new(IPStatus.Success, Target, 20)]);
        Assert.Equal(HopKind.Destination, hop.Kind);
        Assert.Equal(Target, hop.Address);
        Assert.True(TracerouteLogic.IsFinished(hop, 0));
        Assert.Equal("Ziel nach 9 Hop(s) erreicht.", TracerouteLogic.Summary([hop], false));
    }

    [Fact]
    public void Trace_is_limited_to_30_hops()
    {
        Assert.Equal(30, TracerouteLogic.MaxHops);
        var last = TracerouteLogic.Combine(30, [new(IPStatus.TtlExpired, Router, 5)]);
        Assert.True(TracerouteLogic.IsFinished(last, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TracerouteLogic.Combine(31, [new(IPStatus.TtlExpired, Router, 5)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => TracerouteLogic.Combine(0, []));
    }

    [Fact]
    public void Unreachable_ends_the_trace() =>
        Assert.True(TracerouteLogic.IsFinished(TracerouteLogic.Combine(3, [new(IPStatus.DestinationHostUnreachable, Router, 3)]), 0));

    [Theory]
    [InlineData("example.com", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("example.com; calc", false)]
    [InlineData("-n 1 example.com", false)]
    [InlineData("", false)]
    public void Trace_targets_use_the_input_validator(string input, bool valid) => Assert.Equal(valid, InputValidator.TryNormalizeHost(input, out _));
}

public class V13PortCheckTests
{
    [Fact]
    public void Ports_are_parsed_and_deduplicated()
    {
        Assert.True(PortCheckLogic.TryParsePorts("80, 443;8080 443\t3389", out var ports, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { 80, 443, 8080, 3389 }, ports);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("1-1024")]
    [InlineData("80,abc")]
    [InlineData("80;calc")]
    [InlineData("-1")]
    [InlineData("+80")]
    [InlineData("080000")]
    public void Invalid_port_lists_are_rejected(string input) => Assert.False(PortCheckLogic.TryParsePorts(input, out _, out _));

    [Fact]
    public void At_most_20_ports_per_run()
    {
        var twenty = string.Join(",", Enumerable.Range(1, 20));
        var twentyOne = string.Join(",", Enumerable.Range(1, 21));
        Assert.True(PortCheckLogic.TryParsePorts(twenty, out var p, out _));
        Assert.Equal(20, p.Count);
        Assert.False(PortCheckLogic.TryParsePorts(twentyOne, out _, out var error));
        Assert.Contains("20", error);
    }

    [Fact]
    public void Common_services_fit_into_one_run()
    {
        Assert.Equal(PortCheckLogic.MaxPorts, PortCheckLogic.CommonServices.Count);
        Assert.Equal(PortCheckLogic.CommonServices.Count, PortCheckLogic.CommonServices.Select(s => s.Port).Distinct().Count());
        Assert.True(PortCheckLogic.TryParsePorts(string.Join(", ", PortCheckLogic.CommonServices.Select(s => s.Port)), out _, out _));
        Assert.Equal("Remotedesktop", PortCheckLogic.ServiceName(3389));
        Assert.Equal("–", PortCheckLogic.ServiceName(4711));
    }
}

public class V13DnsTests
{
    [Theory]
    [InlineData("12:cloudflare", "ipv4", "set", "address=1.1.1.1")]
    [InlineData("12:cloudflare:4:2", "ipv4", "add", "address=1.0.0.1")]
    [InlineData("7:quad9", "ipv4", "set", "address=9.9.9.9")]
    [InlineData("7:quad9:4:2", "ipv4", "add", "address=149.112.112.112")]
    [InlineData("3:google:6:1", "ipv6", "set", "address=2001:4860:4860::8888")]
    [InlineData("3:google:6:2", "ipv6", "add", "address=2001:4860:4860::8844")]
    public void Static_dns_commands_use_fixed_addresses(string parameter, string family, string verb, string address)
    {
        Assert.True(CommandCatalog.IsSafeToken(parameter));
        var spec = CommandCatalog.Build(AllowedCommand.DnsSetStatic, parameter);
        Assert.True(spec.RequiresElevation);
        Assert.EndsWith("netsh.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "interface", family, verb, "dnsservers" }, spec.Arguments.Take(4));
        Assert.Contains(address, spec.Arguments);
        Assert.Contains("name=" + parameter.Split(':')[0], spec.Arguments);
        Assert.Contains("validate=no", spec.Arguments);
    }

    [Fact]
    public void Dhcp_dns_commands()
    {
        Assert.Equal(new[] { "interface", "ipv4", "set", "dnsservers", "name=12", "source=dhcp" }, CommandCatalog.Build(AllowedCommand.DnsSetDhcp, "12").Arguments);
        Assert.Equal(new[] { "interface", "ipv6", "set", "dnsservers", "name=12", "source=dhcp" }, CommandCatalog.Build(AllowedCommand.DnsSetDhcp, "12:6").Arguments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0:cloudflare")]
    [InlineData("12:evil")]
    [InlineData("12:Cloudflare")]
    [InlineData("12:cloudflare:5:1")]
    [InlineData("12:cloudflare:4:3")]
    [InlineData("12:cloudflare:4")]
    [InlineData("Ethernet:cloudflare")]
    [InlineData("12 :cloudflare")]
    [InlineData("12:cloudflare;calc")]
    [InlineData("12:8.8.8.8")]
    [InlineData("1234567890:google")]
    [InlineData("-12:google")]
    public void Invalid_static_dns_parameters_are_rejected(string? parameter) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.DnsSetStatic, parameter));

    [Theory]
    [InlineData(null)]
    [InlineData("0")]
    [InlineData("12:4")]
    [InlineData("12:cloudflare")]
    [InlineData("name=12")]
    [InlineData("12 source=static")]
    public void Invalid_dhcp_parameters_are_rejected(string? parameter) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.DnsSetDhcp, parameter));

    [Fact]
    public void Parameter_helpers_roundtrip()
    {
        Assert.Equal("12:cloudflare", DnsPresets.StaticParameter(12, "cloudflare"));
        Assert.Equal("12:quad9:6:2", DnsPresets.StaticParameter(12, "quad9", IpFamily.V6, 2));
        Assert.True(DnsPresets.TryParseStatic("12:quad9:6:2", out var p));
        Assert.Equal(12, p!.InterfaceIndex);
        Assert.Equal("2620:fe::9", p.Address);
        Assert.Equal("5:6", DnsPresets.DhcpParameter(5, IpFamily.V6));
        Assert.Throws<ArgumentException>(() => DnsPresets.StaticParameter(12, "evil"));
        Assert.Throws<ArgumentOutOfRangeException>(() => DnsPresets.StaticParameter(0, "google"));
        Assert.Equal(3, DnsPresets.Providers.Count);
        Assert.All(DnsPresets.Providers, x => Assert.True(IPAddress.TryParse(x.PrimaryV6, out _) && IPAddress.TryParse(x.SecondaryV4, out _)));
        Assert.Equal("quad9", DnsPresets.Recognize(["9.9.9.9", "192.168.1.1"])?.Id);
        Assert.Null(DnsPresets.Recognize(["192.168.1.1"]));
    }
}

public class V13WakeOnLanTests
{
    [Theory]
    [InlineData("AA:BB:CC:DD:EE:01")]
    [InlineData("aa-bb-cc-dd-ee-01")]
    [InlineData("AABBCCDDEE01")]
    [InlineData("aabb.ccdd.ee01")]
    [InlineData("  00:11:22:33:44:55 ")]
    public void Mac_formats_are_accepted(string input)
    {
        Assert.True(WakeOnLan.TryParseMac(input, out var mac));
        Assert.Equal(6, mac.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00:00:00:00:00:00")]
    [InlineData("FF:FF:FF:FF:FF:FF")]
    [InlineData("01:00:5E:00:00:01")]
    [InlineData("AA:BB:CC:DD:EE")]
    [InlineData("AA:BB-CC:DD:EE:01")]
    [InlineData("GG:BB:CC:DD:EE:01")]
    [InlineData("AA:BB:CC:DD:EE:01:02")]
    public void Invalid_macs_are_rejected(string input) => Assert.False(WakeOnLan.TryParseMac(input, out _));

    [Fact]
    public void Magic_packet_layout()
    {
        Assert.True(WakeOnLan.TryParseMac("00:11:22:33:44:55", out var mac));
        var packet = WakeOnLan.BuildMagicPacket(mac);
        Assert.Equal(102, packet.Length);
        Assert.All(packet.Take(6), b => Assert.Equal(0xFF, b));
        for (int i = 0; i < 16; i++) Assert.Equal(mac, packet.Skip(6 + i * 6).Take(6).ToArray());
        Assert.Equal("00:11:22:33:44:55", WakeOnLan.FormatMac(mac));
    }

    [Theory]
    [InlineData("192.168.1.20", 24, "192.168.1.255")]
    [InlineData("10.0.5.7", 16, "10.0.255.255")]
    [InlineData("172.16.3.9", 22, "172.16.3.255")]
    public void Directed_broadcast_of_own_subnet(string ip, int prefix, string expected) =>
        Assert.Equal(IPAddress.Parse(expected), WakeOnLan.DirectedBroadcast(IPAddress.Parse(ip), prefix));
}

public class V13SecurityAuditTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static AuditFacts Good() => new()
    {
        Smb1FeatureEnabled = false,
        RdpEnabled = false,
        RdpNla = true,
        Firewall = [new("Domäne", true), new("Privat", true), new("Öffentlich", true)],
        Administrators = ["Dennis"],
        LocalAccounts =
        [
            new("Administrator", true, true, "S-1-5-21-1-2-3-500"),
            new("Gast", true, false, "S-1-5-21-1-2-3-501"),
            new("Dennis", false, true, "S-1-5-21-1-2-3-1001"),
        ],
        UpnpGatewayFound = false,
        AutoplayDisabled = true,
        PowerShell2Enabled = false,
        WindowsBuild = 26200,
        WindowsEdition = "Professional",
        Today = Today,
    };

    private static Finding Get(AuditResult r, string id) => r.Items.Single(i => i.Id == id).Finding;

    [Fact]
    public void Hardened_pc_scores_100_without_fixes()
    {
        var r = SecurityAudit.Evaluate(Good());
        Assert.Equal(100, r.Score);
        Assert.Equal(0, r.OpenIssues);
        Assert.All(r.Findings, f => Assert.Equal(CheckStatus.Pass, f.Status));
        Assert.False(r.Offers(AuditFix.Smb1Disable) || r.Offers(AuditFix.RdpDisable) || r.Offers(AuditFix.FirewallEnableAll));
    }

    [Fact]
    public void Smb1_rdp_and_firewall_offer_fixes()
    {
        var r = SecurityAudit.Evaluate(Good() with
        {
            Smb1FeatureEnabled = true,
            RdpEnabled = true,
            RdpNla = false,
            Firewall = [new("Domäne", true), new("Privat", false), new("Öffentlich", true)],
        });
        Assert.Equal(CheckStatus.Fail, Get(r, "smb1").Status);
        Assert.Equal(CheckStatus.Fail, Get(r, "rdp").Status);
        Assert.Equal(CheckStatus.Fail, Get(r, "firewall").Status);
        Assert.True(r.Offers(AuditFix.Smb1Disable));
        Assert.True(r.Offers(AuditFix.RdpDisable));
        Assert.True(r.Offers(AuditFix.FirewallEnableAll));
        Assert.True(r.Score < 100);
    }

    [Fact]
    public void Rdp_with_nla_is_only_a_warning() =>
        Assert.Equal(CheckStatus.Warning, Get(SecurityAudit.Evaluate(Good() with { RdpEnabled = true, RdpNla = true }), "rdp").Status);

    [Fact]
    public void Smb1_without_admin_rights_is_na_not_guessed()
    {
        var r = SecurityAudit.Evaluate(Good() with { Smb1FeatureEnabled = null, Smb1NeedsAdmin = true });
        var f = Get(r, "smb1");
        Assert.Equal(CheckStatus.NotAvailable, f.Status);
        Assert.Equal("N/A ohne Adminrechte", f.Value);
        Assert.Equal(100, r.Score); // N/A zählt nicht
        Assert.Equal(CheckStatus.Pass, Get(SecurityAudit.Evaluate(Good() with { Smb1FeatureEnabled = null, Smb1ServerRegistry = 0 }), "smb1").Status);
    }

    [Fact]
    public void Accounts_guest_and_admins()
    {
        var r = SecurityAudit.Evaluate(Good() with
        {
            Administrators = ["Administrator", "Dennis", "Service", "Hilfe"],
            LocalAccounts =
            [
                new("Administrator", false, true, "S-1-5-21-1-2-3-500"),
                new("Gast", false, false, "S-1-5-21-1-2-3-501"),
                new("Kind", false, false, "S-1-5-21-1-2-3-1002"),
                new("Alt", true, false, "S-1-5-21-1-2-3-1003"),
            ],
        });
        Assert.Equal(CheckStatus.Fail, Get(r, "guest").Status);
        Assert.Equal(CheckStatus.Warning, Get(r, "admins").Status);
        var noPw = Get(r, "nopassword");
        Assert.Equal(CheckStatus.Warning, noPw.Status);
        Assert.Contains("Kind", noPw.Value);
        Assert.DoesNotContain("Alt", noPw.Value); // deaktivierte Konten zählen nicht
    }

    [Fact]
    public void Unknown_facts_are_na_and_not_scored()
    {
        var r = SecurityAudit.Evaluate(new AuditFacts { Today = Today });
        Assert.All(r.Findings.Where(f => f.Area != "Autorun / AutoPlay"), f => Assert.Equal(CheckStatus.NotAvailable, f.Status));
        // AutoPlay ist ohne Einstellung Windows-Standard (aktiv) → Warnung, daher Score aus genau einem Befund.
        Assert.Equal(50, r.Score);
    }

    [Fact]
    public void Upnp_and_powershell2_warn()
    {
        var r = SecurityAudit.Evaluate(Good() with { UpnpGatewayFound = true, UpnpDevice = "192.168.1.1", PowerShell2Enabled = true, AutoplayDisabled = null });
        Assert.Equal(CheckStatus.Warning, Get(r, "upnp").Status);
        Assert.Contains("Router meldet UPnP aktiv", Get(r, "upnp").Value);
        Assert.Equal(CheckStatus.Warning, Get(r, "ps2").Status);
        Assert.Equal(CheckStatus.Warning, Get(r, "autorun").Status);
        Assert.Equal(CheckStatus.Pass, Get(SecurityAudit.Evaluate(Good() with { AutoplayDisabled = null, NoDriveTypeAutoRun = 0xFF }), "autorun").Status);
    }

    [Theory]
    [InlineData(26200, "Professional", CheckStatus.Pass)]
    [InlineData(26100, "Professional", CheckStatus.Fail)]   // 24H2 Home/Pro: Supportende 13.10.2026
    [InlineData(26100, "Enterprise", CheckStatus.Pass)]
    [InlineData(26100, "EnterpriseS", CheckStatus.Pass)]    // LTSC 2024
    [InlineData(22631, "Enterprise", CheckStatus.Warning)]  // Supportende 10.11.2026 (< 90 Tage)
    [InlineData(22631, "Core", CheckStatus.Fail)]
    [InlineData(19045, "Professional", CheckStatus.Fail)]
    [InlineData(19044, "EnterpriseS", CheckStatus.Warning)] // LTSC 2021 bis 12.01.2027 (in 90 Tagen)
    [InlineData(17763, "Professional", CheckStatus.Fail)]
    [InlineData(28000, "Professional", CheckStatus.NotAvailable)]
    [InlineData(22000, "ServerStandard", CheckStatus.NotAvailable)]
    public void Windows_support_table(int build, string edition, CheckStatus expected) =>
        Assert.Equal(expected, WindowsSupport.Evaluate(build, edition, null, Today).Finding.Status);

    [Fact]
    public void Windows_10_end_mentions_esu()
    {
        var f = WindowsSupport.Evaluate(19045, "Core", "22H2", Today).Finding;
        Assert.Contains("14.10.2025", f.Value);
        Assert.Contains("ESU", f.Advice);
    }
}

public class V13ScheduledTaskTests
{
    [Theory]
    [InlineData(@"\Contoso\Update Task")]
    [InlineData(@"\Microsoft\Windows\Defrag\ScheduledDefrag")]
    [InlineData(@"\GoogleUpdateTaskMachineUA{2D5E8A1B-1234-4C5D-9E8F-0123456789AB}")]
    [InlineData(@"\Ünïcödé Ordner\Aufgabe (täglich) #1")]
    [InlineData(@"\OneDrive Standalone Update Task-S-1-5-21-1111-2222-3333-1001")]
    public void Task_tokens_roundtrip_and_are_safe(string path)
    {
        var token = ScheduledTaskLogic.EncodeToken(path);
        Assert.True(CommandCatalog.IsSafeToken(token));
        Assert.True(ScheduledTaskLogic.TryDecodeToken(token, out var decoded));
        Assert.Equal(path, decoded);
        var spec = CommandCatalog.Build(AllowedCommand.ScheduledTaskDisable, token);
        Assert.Equal(new[] { "/Change", "/TN", path, "/Disable" }, spec.Arguments);
        Assert.True(spec.RequiresElevation);
        Assert.EndsWith("schtasks.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("/Enable", CommandCatalog.Build(AllowedCommand.ScheduledTaskEnable, token).Arguments[^1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Contoso\\Task")]            // nicht absolut
    [InlineData("\\")]                       // nur Wurzel
    [InlineData("\\Contoso\\")]              // leeres Segment am Ende
    [InlineData("\\Contoso\\\\Task")]        // leeres Segment
    [InlineData("\\Contoso\\..\\Task")]
    [InlineData("\\Contoso\\Task\" /Delete /F \"")]
    [InlineData("\\Task\r\n/Delete")]
    [InlineData("\\Task|calc")]
    [InlineData("\\Task&calc<x>")]
    [InlineData("\\C:\\Windows")]
    [InlineData("\\a/b")]
    [InlineData("\\Task.")]
    [InlineData("\\ Task")]
    public void Invalid_task_paths_cannot_be_encoded(string path)
    {
        Assert.False(ScheduledTaskLogic.IsValidTaskPath(path));
        Assert.Throws<ArgumentException>(() => ScheduledTaskLogic.EncodeToken(path));
    }

    [Fact]
    public void Too_long_paths_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => ScheduledTaskLogic.EncodeToken("\\" + new string('a', ScheduledTaskLogic.MaxPathLength)));
        // 200 Zeichen mit Mehrbyte-UTF-8 sprengen die Tokenlänge → ebenfalls abgelehnt statt abgeschnitten.
        Assert.Throws<ArgumentException>(() => ScheduledTaskLogic.EncodeToken("\\" + new string('ü', 150)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("T")]
    [InlineData("XXEnRhc2s")]                     // falsches Präfix
    [InlineData("T\\Task")]
    [InlineData("T a b")]
    [InlineData("TXENvbnRvc28=")]                 // Padding nicht erlaubt
    [InlineData("T////")]
    [InlineData("TXA")]                           // dekodiert zu ungültigem Pfad
    public void Invalid_task_tokens_are_rejected(string? token)
    {
        Assert.False(ScheduledTaskLogic.TryDecodeToken(token, out _));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.ScheduledTaskDisable, token));
    }

    [Fact]
    public void Injection_path_smuggled_in_base64_is_rejected()
    {
        var evil = "T" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("\\Task\" /Delete /F /TN \"\\X")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.True(CommandCatalog.IsSafeToken(evil));
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.ScheduledTaskEnable, evil));
    }

    [Fact]
    public void Non_canonical_token_is_rejected()
    {
        // "\A" = 0x5C 0x41 → "XEE" (letztes Zeichen hat 2 freie Bits). "XEF" dekodiert zu denselben Bytes, ist aber nicht kanonisch.
        Assert.Equal("TXEE", ScheduledTaskLogic.EncodeToken(@"\A"));
        Assert.True(ScheduledTaskLogic.TryDecodeToken("TXEE", out _));
        Assert.False(ScheduledTaskLogic.TryDecodeToken("TXEF", out _));
    }

    [Fact]
    public void Microsoft_tasks_are_detected()
    {
        Assert.True(ScheduledTaskLogic.IsMicrosoftWindowsTask(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan"));
        Assert.False(ScheduledTaskLogic.IsMicrosoftWindowsTask(@"\Microsoft\Office\OfficeTelemetryAgentLogOn"));
        Assert.False(ScheduledTaskLogic.IsMicrosoftWindowsTask(@"\MicrosoftEdgeUpdateTaskMachineCore"));
    }

    private static readonly IReadOnlyList<string> Writable = [@"C:\Users\Max\AppData\Local\Temp\", @"C:\Users\Max\AppData\Local", @"C:\Users\Max\AppData\Roaming"];

    [Theory]
    [InlineData(null, false, null, null, TaskMarker.NoProgram)]
    [InlineData(@"C:\Program Files\Contoso\gone.exe", false, null, null, TaskMarker.ProgramMissing)]
    [InlineData(@"C:\Users\Max\AppData\Roaming\x\evil.exe", true, SignatureStatus.NotSigned, null, TaskMarker.UserWritableLocation)]
    [InlineData(@"C:\Users\Other\AppData\Local\Updater\up.exe", true, SignatureStatus.Valid, "Contoso Ltd", TaskMarker.UserWritableLocation)]
    [InlineData(@"C:\Windows\Temp\x.exe", true, SignatureStatus.NotSigned, null, TaskMarker.UserWritableLocation)]
    [InlineData(@"C:\Windows\System32\defrag.exe", true, SignatureStatus.NotSigned, null, TaskMarker.WindowsComponent)]
    [InlineData(@"C:\Windows\System32\MusNotification.exe", true, SignatureStatus.Valid, "Microsoft Windows", TaskMarker.MicrosoftSigned)]
    [InlineData(@"C:\Program Files\Google\Update\GoogleUpdate.exe", true, SignatureStatus.Valid, "Google LLC", TaskMarker.KnownPublisher)]
    [InlineData(@"C:\Program Files\Tool\tool.exe", true, SignatureStatus.NotSigned, null, TaskMarker.Unsigned)]
    [InlineData(@"C:\Program Files\Tool\tool.exe", true, SignatureStatus.Tampered, "Contoso", TaskMarker.Unsigned)]
    public void Tasks_are_classified(string? program, bool exists, SignatureStatus? sig, string? publisher, TaskMarker expected) =>
        Assert.Equal(expected, ScheduledTaskLogic.Classify(new TaskProgramFacts(program, exists, sig, publisher), Writable, @"C:\Windows"));

    [Fact]
    public void Marker_states()
    {
        Assert.Equal(Common.DataState.Problem, ScheduledTaskLogic.StateOf(TaskMarker.UserWritableLocation, SignatureStatus.NotSigned));
        Assert.Equal(Common.DataState.Warning, ScheduledTaskLogic.StateOf(TaskMarker.UserWritableLocation, SignatureStatus.Valid));
        Assert.Equal(Common.DataState.Problem, ScheduledTaskLogic.StateOf(TaskMarker.Unsigned, SignatureStatus.Tampered));
        Assert.Equal(Common.DataState.Ok, ScheduledTaskLogic.StateOf(TaskMarker.MicrosoftSigned, SignatureStatus.Valid));
    }

    [Fact]
    public void Program_paths_are_normalized_without_touching_the_disk()
    {
        string Expand(string s) => s.Replace("%windir%", @"C:\Windows", StringComparison.OrdinalIgnoreCase);
        bool Exists(string p) => p.Equals(@"C:\Windows\System32\rundll32.exe", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(@"C:\Windows\system32\sc.exe", ScheduledTaskLogic.NormalizeProgramPath("\"%windir%\\system32\\sc.exe\"", Expand, @"C:\Windows\System32", Exists));
        Assert.Equal(@"C:\Windows\System32\rundll32.exe", ScheduledTaskLogic.NormalizeProgramPath("rundll32", Expand, @"C:\Windows\System32", Exists));
        Assert.Equal("unknown.exe", ScheduledTaskLogic.NormalizeProgramPath("unknown.exe", Expand, @"C:\Windows\System32", Exists));
        Assert.Null(ScheduledTaskLogic.NormalizeProgramPath("  ", Expand, @"C:\Windows\System32", Exists));
    }

    [Fact]
    public void Run_times_before_1980_mean_never()
    {
        Assert.Null(ScheduledTaskLogic.ValidRunTime(new DateTime(1899, 12, 30)));
        Assert.NotNull(ScheduledTaskLogic.ValidRunTime(new DateTime(2026, 10, 1)));
        Assert.Equal("Beim Systemstart", ScheduledTaskLogic.TriggerText(8));
        Assert.Equal("Deaktiviert", ScheduledTaskLogic.StateText(1));
    }
}

public class V13PrivacyTests
{
    [Fact]
    public void Table_is_fixed_and_ids_are_safe_tokens()
    {
        Assert.Equal(new[] { "advertising-id", "suggestions", "tailored", "websearch", "activity", "telemetry" }, PrivacyCatalog.Settings.Select(s => s.Id));
        Assert.All(PrivacyCatalog.Settings, s =>
        {
            Assert.True(CommandCatalog.IsSafeToken(s.Id + ":default"));
            Assert.DoesNotContain("..", s.KeyPath);
            Assert.False(s.KeyPath.StartsWith('\\'));
        });
        Assert.All(PrivacyCatalog.Settings.Where(s => s.Scope == PrivacyScope.Machine), s =>
        {
            Assert.Single(s.ValueNames);
            Assert.StartsWith(@"SOFTWARE\Policies\Microsoft\Windows\", s.KeyPath);
        });
    }

    [Theory]
    [InlineData("activity:0", "add", "0")]
    [InlineData("activity:1", "add", "1")]
    [InlineData("telemetry:1", "add", "1")]
    [InlineData("activity:default", "delete", null)]
    [InlineData("telemetry:default", "delete", null)]
    public void Machine_parameters_build_fixed_reg_commands(string parameter, string verb, string? data)
    {
        Assert.True(CommandCatalog.IsSafeToken(parameter));
        var spec = CommandCatalog.Build(AllowedCommand.PrivacySetMachine, parameter);
        Assert.True(spec.RequiresElevation);
        Assert.EndsWith("reg.exe", spec.FilePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(verb, spec.Arguments[0]);
        Assert.StartsWith(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\", spec.Arguments[1]);
        Assert.Equal("/reg:64", spec.Arguments[^1]);
        if (data is null) Assert.DoesNotContain("/d", spec.Arguments);
        else
        {
            Assert.Equal(data, spec.Arguments[spec.Arguments.ToList().IndexOf("/d") + 1]);
            Assert.Contains("REG_DWORD", spec.Arguments);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("telemetry:0")]          // nur „Erforderlich“ (1) oder Standard
    [InlineData("telemetry:3")]
    [InlineData("advertising-id:0")]     // HKCU – nie über den Helper
    [InlineData("websearch:1")]
    [InlineData("activity")]
    [InlineData("activity:0:1")]
    [InlineData("activity:Default")]
    [InlineData("Activity:0")]
    [InlineData("unknown:1")]
    [InlineData("activity:0 /f")]
    public void Invalid_privacy_parameters_are_rejected(string? parameter) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.PrivacySetMachine, parameter));

    [Fact]
    public void State_evaluation()
    {
        var s = PrivacyCatalog.Find("suggestions")!;
        Assert.Equal(PrivacyState.Default, PrivacyCatalog.Evaluate(s, new Dictionary<string, int?>()));
        Assert.Equal(PrivacyState.Partial, PrivacyCatalog.Evaluate(s, new Dictionary<string, int?> { ["SubscribedContent-338388Enabled"] = 0 }));
        Assert.Equal(PrivacyState.Protected, PrivacyCatalog.Evaluate(s, s.ValueNames.ToDictionary(n => n, _ => (int?)0)));
        var web = PrivacyCatalog.Find("websearch")!;
        Assert.Equal(PrivacyState.Protected, PrivacyCatalog.Evaluate(web, new Dictionary<string, int?> { ["DisableSearchBoxSuggestions"] = 1 }));
    }

    [Fact]
    public void Restore_parameters()
    {
        var activity = PrivacyCatalog.Find("activity")!;
        var telemetry = PrivacyCatalog.Find("telemetry")!;
        Assert.Equal("activity:default", PrivacyCatalog.MachineRestoreParameter(activity, null));
        Assert.Equal("activity:1", PrivacyCatalog.MachineRestoreParameter(activity, 1));
        Assert.Equal("telemetry:default", PrivacyCatalog.MachineRestoreParameter(telemetry, null));
        Assert.Null(PrivacyCatalog.MachineRestoreParameter(telemetry, 3)); // nicht darstellbar → nicht raten
        Assert.Null(PrivacyCatalog.MachineRestoreParameter(PrivacyCatalog.Find("tailored")!, 1));
    }

    [Fact]
    public void Backup_remembers_only_the_first_state_and_roundtrips()
    {
        var b = new PrivacyBackup();
        Assert.True(b.Remember("advertising-id", new Dictionary<string, int?> { ["Enabled"] = 1 }));
        Assert.False(b.Remember("advertising-id", new Dictionary<string, int?> { ["Enabled"] = 0 }));
        Assert.True(b.Remember("activity", new Dictionary<string, int?> { ["PublishUserActivities"] = null }));
        var copy = PrivacyBackup.Deserialize(b.Serialize());
        Assert.Equal(1, copy.Original["advertising-id"]["Enabled"]);
        Assert.True(copy.Original["activity"].ContainsKey("PublishUserActivities"));
        Assert.Null(copy.Original["activity"]["PublishUserActivities"]);
        Assert.True(copy.Forget("activity"));
        Assert.False(copy.Has("activity"));
    }

    [Fact]
    public void Backup_ignores_foreign_ids_and_values()
    {
        const string json = """{"Version":1,"Original":{"evil":{"Shell":1},"tailored":{"TailoredExperiencesWithDiagnosticDataEnabled":1,"Other":5}}}""";
        var b = PrivacyBackup.Deserialize(json);
        Assert.False(b.Has("evil"));
        Assert.Equal(new[] { "TailoredExperiencesWithDiagnosticDataEnabled" }, b.Original["tailored"].Keys);
        Assert.Empty(PrivacyBackup.Deserialize("not json").Original);
    }
}

public class V13NetSecCommandTests
{
    [Fact]
    public void Fixed_commands_have_exact_arguments()
    {
        Assert.Equal(new[] { "/flushdns" }, CommandCatalog.Build(AllowedCommand.DnsFlush).Arguments);
        Assert.False(CommandCatalog.Build(AllowedCommand.DnsFlush).RequiresElevation);
        Assert.Equal(new[] { "/renew" }, CommandCatalog.Build(AllowedCommand.IpRenew).Arguments);
        Assert.False(CommandCatalog.Build(AllowedCommand.IpRenew).RequiresElevation);
        Assert.Equal(new[] { "winsock", "reset" }, CommandCatalog.Build(AllowedCommand.NetworkResetWinsock).Arguments);
        Assert.Equal(new[] { "interface", "ip", "reset" }, CommandCatalog.Build(AllowedCommand.NetworkResetIp).Arguments);
        Assert.Equal(new[] { "/Online", "/Disable-Feature", "/FeatureName:SMB1Protocol", "/NoRestart" }, CommandCatalog.Build(AllowedCommand.Smb1Disable).Arguments);
        Assert.Equal(new[] { "add", @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "/v", "fDenyTSConnections", "/t", "REG_DWORD", "/d", "1", "/f", "/reg:64" },
            CommandCatalog.Build(AllowedCommand.RdpDisable).Arguments);
        Assert.Equal(new[] { "advfirewall", "set", "allprofiles", "state", "on" }, CommandCatalog.Build(AllowedCommand.FirewallEnableAll).Arguments);
        foreach (var cmd in new[] { AllowedCommand.NetworkResetWinsock, AllowedCommand.NetworkResetIp, AllowedCommand.Smb1Disable, AllowedCommand.RdpDisable,
                     AllowedCommand.FirewallEnableAll, AllowedCommand.DnsSetStatic, AllowedCommand.DnsSetDhcp, AllowedCommand.ScheduledTaskDisable,
                     AllowedCommand.ScheduledTaskEnable, AllowedCommand.PrivacySetMachine })
        {
            Assert.True(CommandSamples.TryGet(cmd, out var p), cmd.ToString());
            var spec = CommandCatalog.Build(cmd, p);
            Assert.True(spec.RequiresElevation, cmd.ToString());
            Assert.False(spec.IsInteractive, cmd.ToString());
        }
    }

    [Theory]
    [InlineData(AllowedCommand.DnsFlush)]
    [InlineData(AllowedCommand.IpRenew)]
    [InlineData(AllowedCommand.NetworkResetWinsock)]
    [InlineData(AllowedCommand.Smb1Disable)]
    [InlineData(AllowedCommand.RdpDisable)]
    [InlineData(AllowedCommand.FirewallEnableAll)]
    public void Parameterless_commands_reject_parameters(AllowedCommand cmd) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(cmd, "x"));

    [Fact]
    public void Firewall_is_only_ever_switched_on()
    {
        foreach (var cmd in Enum.GetValues<AllowedCommand>())
        {
            if (!CommandSamples.TryGet(cmd, out var p) && cmd is not (AllowedCommand.FirewallEnableAll)) continue;
            CommandSpec spec;
            try { spec = CommandCatalog.Build(cmd, p); }
            catch (ArgumentException) { continue; }
            if (spec.FilePath.EndsWith("netsh.exe", StringComparison.OrdinalIgnoreCase) && spec.Arguments.Contains("advfirewall"))
                Assert.DoesNotContain("off", spec.Arguments);
        }
    }
}
