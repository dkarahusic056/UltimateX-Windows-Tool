using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.NetSec;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Security;

namespace UltimateX.Services.NetSec;

/// <summary>
/// v1.3 Sicherheits-Check (IT-Audit). Erhebung ausschließlich LESEND (Registry, WMI, SSDP im lokalen Netz);
/// die Bewertung liegt testbar in <see cref="SecurityAudit"/>. Behebungen nur über die Allowlist nach Bestätigung in der UI.
/// Defender, UAC und Windows Update werden nie verändert.
/// </summary>
public sealed class SecurityAuditService(SecurityService security)
{
    public async Task<AuditFacts> CollectAsync(CancellationToken ct)
    {
        var upnpTask = ProbeUpnpAsync(ct);
        var facts = await Task.Run(() => CollectLocal(ct), ct).ConfigureAwait(false);
        var (found, device) = await upnpTask.ConfigureAwait(false);
        return facts with { UpnpGatewayFound = found, UpnpDevice = device };
    }

    private AuditFacts CollectLocal(CancellationToken ct)
    {
        // --- Windows-Features (SMB1, PowerShell 2.0) über WMI; ohne Rechte → N/A ---
        bool? smb1 = null, ps2 = null;
        var featuresNeedAdmin = false;
        var features = Wmi.Query("SELECT Name, InstallState FROM Win32_OptionalFeature WHERE Name='SMB1Protocol' OR Name='MicrosoftWindowsPowerShellV2Root' OR Name='MicrosoftWindowsPowerShellV2'");
        if (features.Ok)
        {
            bool? Installed(string name)
            {
                var row = features.Rows.FirstOrDefault(r => string.Equals(r.Str("Name"), name, StringComparison.OrdinalIgnoreCase));
                if (row is null) return false; // Feature existiert in dieser Windows-Version nicht
                return row.U64("InstallState") switch { 1 => true, 2 or 3 => false, _ => null };
            }
            smb1 = Installed("SMB1Protocol");
            var psRoot = Installed("MicrosoftWindowsPowerShellV2Root");
            var psEngine = Installed("MicrosoftWindowsPowerShellV2");
            if (psRoot == true || psEngine == true) ps2 = true;
            else if (psRoot is null) ps2 = null;
            else ps2 = false;
        }
        else
        {
            featuresNeedAdmin = features.State == DataState.RequiresAdmin;
        }
        ct.ThrowIfCancellationRequested();

        var smb1Reg = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1");
        var deny = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
        var nla = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp", "UserAuthentication");

        // --- Firewall: vorhandenen SecurityService wiederverwenden ---
        var firewall = new List<FirewallProfileState>();
        try
        {
            foreach (var item in security.GetStatus().Where(i => i.Area.StartsWith("Firewall (", StringComparison.Ordinal)))
            {
                var profile = item.Area["Firewall (".Length..].TrimEnd(')');
                firewall.Add(new FirewallProfileState(profile, item.State switch { DataState.Ok => true, DataState.Problem => false, _ => null }));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            AppLog.Warn("SecurityAudit", "Firewall-Status nicht lesbar", ex.Message);
        }
        ct.ThrowIfCancellationRequested();

        var accounts = ReadLocalAccounts();
        var admins = ReadAdministrators();

        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        int? build = cv?.GetValue("CurrentBuildNumber") is string bs && int.TryParse(bs, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : null;

        return new AuditFacts
        {
            Smb1FeatureEnabled = smb1,
            Smb1NeedsAdmin = featuresNeedAdmin,
            Smb1ServerRegistry = smb1Reg,
            RdpEnabled = deny switch { 0 => true, 1 => false, _ => null },
            RdpNla = nla switch { 1 => true, 0 => false, _ => null },
            Firewall = firewall,
            Administrators = admins,
            LocalAccounts = accounts,
            NoDriveTypeAutoRun = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun")
                                 ?? ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer", "NoDriveTypeAutoRun"),
            AutoplayDisabled = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers", "DisableAutoplay") is { } ap ? ap == 1 : null,
            PowerShell2Enabled = ps2,
            PowerShell2NeedsAdmin = featuresNeedAdmin,
            WindowsBuild = build,
            WindowsEdition = cv?.GetValue("EditionID") as string,
            WindowsDisplayVersion = cv?.GetValue("DisplayVersion") as string,
            Today = DateOnly.FromDateTime(DateTime.Today),
        };
    }

    private static int? ReadDword(RegistryKey hive, string path, string name)
    {
        try
        {
            using var k = hive.OpenSubKey(path);
            return k?.GetValue(name) is int i ? i : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static IReadOnlyList<LocalAccountInfo>? ReadLocalAccounts()
    {
        var r = Wmi.Query("SELECT Name, Disabled, PasswordRequired, SID FROM Win32_UserAccount WHERE LocalAccount=TRUE");
        if (!r.Ok) return null;
        return r.Rows.Where(x => x.Str("Name") is not null)
            .Select(x => new LocalAccountInfo(x.Str("Name")!, x.Bool("Disabled") ?? false, x.Bool("PasswordRequired") ?? true, x.Str("SID")))
            .ToList();
    }

    /// <summary>
    /// Mitglieder der lokalen Administratorengruppe per WMI. Die Gruppe wird über die feste SID S-1-5-32-544 gefunden
    /// (sprachunabhängig). Der zweite WQL-Teil verwendet ausschließlich von WMI gelieferte Werte (escaped), nie Benutzereingaben.
    /// </summary>
    private static IReadOnlyList<string>? ReadAdministrators()
    {
        var group = Wmi.Query("SELECT Name, Domain FROM Win32_Group WHERE LocalAccount=TRUE AND SID='S-1-5-32-544'");
        var g = group.Rows.FirstOrDefault();
        if (!group.Ok || g?.Str("Name") is not { } name || g.Str("Domain") is not { } domain) return null;
        var members = Wmi.Query(
            $"ASSOCIATORS OF {{Win32_Group.Domain='{WqlEscape(domain)}',Name='{WqlEscape(name)}'}} WHERE AssocClass=Win32_GroupUser Role=GroupComponent ResultRole=PartComponent",
            timeoutSeconds: 30);
        if (!members.Ok) return null;
        return members.Rows
            .Where(m => m.Str("Name") is not null)
            .Select(m => m.Str("Domain") is { } d && !string.Equals(d, Environment.MachineName, StringComparison.OrdinalIgnoreCase) ? $"{d}\\{m.Str("Name")}" : m.Str("Name")!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string WqlEscape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    /// <summary>
    /// SSDP-Suche (M-SEARCH an 239.255.255.250:1900, Multicast-TTL 1 → bleibt im lokalen Netz) nach einem
    /// InternetGatewayDevice. Es wird nichts am Router abgefragt oder geändert – nur die Antwort ausgewertet.
    /// </summary>
    public static async Task<(bool? Found, string? Device)> ProbeUpnpAsync(CancellationToken ct)
    {
        const string request = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\n" +
                               "ST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\n\r\n";
        try
        {
            using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
            udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
            var bytes = Encoding.ASCII.GetBytes(request);
            await udp.SendAsync(bytes, new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900), ct).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            while (true)
            {
                UdpReceiveResult res;
                try { res = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return (false, null); }
                if (res.Buffer.Length > 4096) continue;
                var text = Encoding.ASCII.GetString(res.Buffer);
                if (!text.Contains("InternetGatewayDevice", StringComparison.OrdinalIgnoreCase)) continue;
                var server = text.Split('\n').Select(l => l.Trim())
                    .FirstOrDefault(l => l.StartsWith("SERVER:", StringComparison.OrdinalIgnoreCase))?["SERVER:".Length..].Trim();
                var device = res.RemoteEndPoint.Address + (string.IsNullOrWhiteSpace(server) ? "" : " · " + Sanitize(server));
                return (true, device);
            }
        }
        catch (SocketException ex)
        {
            AppLog.Warn("SecurityAudit", "UPnP-Suche nicht möglich", ex.Message);
            return (null, null);
        }
    }

    private static string Sanitize(string s)
    {
        var clean = new string(s.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 80 ? clean[..80] : clean;
    }

    // ---------------------------------------------------------------- Behebungen (nur nach Bestätigung in der UI)

    public static async Task<OperationResult> FixAsync(AuditFix fix, IProgress<string>? progress, CancellationToken ct)
    {
        var cmd = fix switch
        {
            AuditFix.Smb1Disable => AllowedCommand.Smb1Disable,
            AuditFix.RdpDisable => AllowedCommand.RdpDisable,
            AuditFix.FirewallEnableAll => AllowedCommand.FirewallEnableAll,
            _ => throw new ArgumentOutOfRangeException(nameof(fix)),
        };
        var r = await ElevatedCommandRunner.RunAsync(cmd, null, progress, ct).ConfigureAwait(false);
        if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
        AppLog.Info("SecurityAudit", $"Behebung {fix}", $"ExitCode={r.ExitCode}");
        // DISM: 3010 = erfolgreich, Neustart erforderlich.
        return r.ExitCode switch
        {
            0 => OperationResult.Ok(fix == AuditFix.Smb1Disable ? "SMB1 deaktiviert." : "Erledigt.", r.Output),
            3010 => OperationResult.Ok("Erledigt – ein Neustart ist erforderlich.", r.Output),
            _ => OperationResult.Fail($"Fehlgeschlagen (Exit {r.ExitCode}).", r.Output),
        };
    }
}
