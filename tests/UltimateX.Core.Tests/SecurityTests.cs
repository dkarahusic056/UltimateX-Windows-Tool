using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

/// <summary>Security-Tests aus Prompt §105: Path Traversal, Command Injection, ungültige Eingaben.</summary>
public class PathGuardTests
{
    private static readonly string Desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    [Fact]
    public void Traversal_out_of_allowed_root_is_blocked()
    {
        var evil = Path.Combine(Desktop, @"..\..\..\..\Windows\System32\drivers\etc\hosts");
        Assert.False(PathGuard.IsSafeForModification(evil, [Desktop], out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32\kernel32.dll")]
    [InlineData(@"C:\Program Files\Something")]
    public void System_folders_are_protected(string path)
    {
        Assert.True(PathGuard.IsProtected(path));
    }

    [Theory]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\Windows")]
    [InlineData("relative\\path.txt")]
    [InlineData("")]
    public void Unc_device_relative_and_empty_paths_are_rejected(string path)
    {
        Assert.False(PathGuard.IsSafeForModification(path, [Desktop], out _));
    }

    [Fact]
    public void File_inside_allowed_root_is_accepted()
    {
        var file = Path.Combine(Desktop, "report-that-does-not-exist.pdf");
        Assert.True(PathGuard.IsSafeForModification(file, [Desktop], out _));
    }

    [Fact]
    public void Windows_temp_content_is_cleanable_but_folder_itself_is_protected()
    {
        var winTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        Assert.False(PathGuard.IsProtected(Path.Combine(winTemp, "old.tmp")));
        Assert.True(PathGuard.IsProtected(winTemp));
        Assert.True(PathGuard.IsProtected(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "drivers")));
    }

    [Fact]
    public void Sibling_folder_with_common_prefix_is_not_inside()
    {
        Assert.False(PathGuard.IsWithin(@"C:\Users\Alice2\file.txt", @"C:\Users\Alice"));
        Assert.True(PathGuard.IsWithin(@"C:\Users\Alice\file.txt", @"C:\Users\Alice"));
    }
}

public class CommandCatalogTests
{
    [Theory]
    [InlineData("C:")]
    [InlineData("c")]
    [InlineData("D:\\")]
    public void Valid_drive_letters_are_accepted(string drive)
    {
        var spec = CommandCatalog.Build(AllowedCommand.ChkdskScan, drive);
        Assert.Equal(2, spec.Arguments.Count);
        Assert.Matches("^[A-Z]:$", spec.Arguments[0]);
        Assert.Equal("/scan", spec.Arguments[1]);
    }

    [Theory]
    [InlineData("C: & del /q C:\\*")]
    [InlineData("C:\" /f")]
    [InlineData("C; shutdown -s")]
    [InlineData("..")]
    [InlineData("CD")]
    [InlineData("1")]
    [InlineData(null)]
    public void Injection_attempts_in_drive_parameter_are_rejected(string? drive)
    {
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.ChkdskScan, drive));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("601")]
    [InlineData("30 /f")]
    [InlineData("abc")]
    public void Invalid_countdowns_are_rejected(string seconds)
    {
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.Shutdown, seconds));
    }

    [Theory]
    [InlineData("..\\..\\Windows\\System32\\config\\SAM")]
    [InlineData("C:\\x.dmp")]
    [InlineData("a b.dmp")]
    [InlineData("x.dmp & calc")]
    public void Dump_names_reject_paths_and_injection(string name) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.AnalyzeMinidump, name));

    [Fact]
    public void All_external_commands_resolve_to_absolute_trusted_paths()
    {
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        foreach (var cmd in Enum.GetValues<AllowedCommand>())
        {
            var spec = cmd switch
            {
                AllowedCommand.ChkdskScan or AllowedCommand.ChkdskSpotFix or AllowedCommand.ScheduleBootDiskCheck => CommandCatalog.Build(cmd, "C"),
                AllowedCommand.Shutdown or AllowedCommand.Restart => CommandCatalog.Build(cmd, "30"),
                AllowedCommand.WingetUpgrade => CommandCatalog.Build(cmd, "Mozilla.Firefox"),
                AllowedCommand.SetMachineStartup => CommandCatalog.Build(cmd, CommandCatalog.EncodeStartupToken(true, 1, "OneDrive")),
                AllowedCommand.InstallWindowsUpdate => CommandCatalog.Build(cmd, Guid.NewGuid().ToString("D")),
                AllowedCommand.AnalyzeMinidump => CommandCatalog.Build(cmd, "101026-12345-01.dmp"),
                _ => CommandCatalog.Build(cmd),
            };
            if (spec.IsInternal)
            {
                Assert.True(spec.RequiresElevation, cmd.ToString()); // interne Aktionen laufen nur im Admin-Helper
                Assert.Equal("", spec.FilePath);
                continue;
            }
            Assert.True(Path.IsPathFullyQualified(spec.FilePath), cmd.ToString());
            var trusted = spec.FilePath.StartsWith(system32, StringComparison.OrdinalIgnoreCase)
                          || spec.FilePath.Equals(CommandCatalog.WingetPath, StringComparison.OrdinalIgnoreCase);
            Assert.True(trusted, cmd.ToString());
        }
    }

    [Theory]
    [InlineData("Mozilla.Firefox", true)]
    [InlineData("Microsoft.VisualStudioCode", true)]
    [InlineData("Notepad++.Notepad++", true)]
    [InlineData("foo --override \"/S\"", false)]
    [InlineData("a;calc", false)]
    [InlineData("-silent", false)]
    [InlineData("", false)]
    public void Winget_package_ids_are_validated(string id, bool valid)
    {
        if (valid) Assert.Contains(id, CommandCatalog.Build(AllowedCommand.WingetUpgrade, id).Arguments);
        else Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.WingetUpgrade, id));
    }

    [Theory]
    [InlineData("OneDrive")]
    [InlineData("Steam Client Bootstrapper")]
    [InlineData("Ünïcödé & Sonderzeichen \"quoted\"")]
    public void Startup_tokens_roundtrip_and_are_safe(string name)
    {
        var token = CommandCatalog.EncodeStartupToken(false, 2, name);
        Assert.True(CommandCatalog.IsSafeToken(token));
        Assert.True(CommandCatalog.TryDecodeStartupToken(token, out var enable, out var source, out var decoded));
        Assert.False(enable);
        Assert.Equal(2, source);
        Assert.Equal(name, decoded);
    }

    [Theory]
    [InlineData("X1abc")]
    [InlineData("E4abc")]
    [InlineData("E1***")]
    [InlineData("E1")]
    public void Invalid_startup_tokens_are_rejected(string token)
        => Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.SetMachineStartup, token));

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345678-1234-1234-1234-1234567890ab' or '1'='1")]
    public void Update_ids_must_be_guids(string id)
        => Assert.Throws<ArgumentException>(() => CommandCatalog.Build(AllowedCommand.InstallWindowsUpdate, id));

    [Theory]
    [InlineData("SfcScan", true)]
    [InlineData("0123456789abcdef0123456789abcdef", true)]
    [InlineData("C:", true)]
    [InlineData("a b", false)]
    [InlineData("x\"y", false)]
    [InlineData("&calc", false)]
    [InlineData("", false)]
    public void Helper_tokens_are_strict(string token, bool expected)
    {
        Assert.Equal(expected, CommandCatalog.IsSafeToken(token));
    }
}

public class InputValidatorTests
{
    [Theory]
    [InlineData("1.1.1.1", "1.1.1.1")]
    [InlineData("2606:4700:4700::1111", "2606:4700:4700::1111")]
    [InlineData("Example.COM", "example.com")]
    [InlineData("sub.domain-x.de.", "sub.domain-x.de")]
    public void Valid_hosts_are_normalized(string input, string expected)
    {
        Assert.True(InputValidator.TryNormalizeHost(input, out var host));
        Assert.Equal(expected, host);
    }

    [Theory]
    [InlineData("1.1.1.1 && calc")]
    [InlineData("-n 1000 1.1.1.1")]
    [InlineData("exa mple.com")]
    [InlineData("-bad.com")]
    [InlineData("a..b")]
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_hosts_are_rejected(string? input)
    {
        Assert.False(InputValidator.TryNormalizeHost(input, out _));
    }
}

public class MaskerTests
{
    [Fact]
    public void Mac_keeps_vendor_prefix_only()
        => Assert.Equal("AA-BB-CC-XX-XX-XX", SensitiveMasker.MaskMac("AA-BB-CC-DD-EE-FF"));

    [Fact]
    public void Ipv4_is_masked()
        => Assert.Equal("203.0.x.x", SensitiveMasker.MaskIp("203.0.113.45"));

    [Fact]
    public void Serial_shows_last_four()
        => Assert.EndsWith("1234", SensitiveMasker.MaskGeneric("ABCDEFGH1234"));
}
