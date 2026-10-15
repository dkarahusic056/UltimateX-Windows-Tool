using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.NetSec;

namespace UltimateX.Core.Security;

/// <summary>
/// v1.3 Netzwerk/Sicherheit/Aufgaben/Datenschutz. Alle Argumente sind fest; variable Teile kommen ausschließlich aus
/// streng geprüften Parametern (Schnittstellen-Index als Zahl, Anbieter-ID aus fester Liste, Base64url-Aufgabentoken,
/// Datenschutz-ID aus fester Tabelle). Übergabe immer über ArgumentList, nie über eine Shell.
/// </summary>
public static partial class CommandCatalog
{
    private static CommandSpec? BuildNetworkCommand(AllowedCommand id, string? parameter)
    {
        string Sys(string exe) => Path.Combine(AppPaths.System32, exe);

        return id switch
        {
            // --- Benutzer ---
            AllowedCommand.DnsFlush => NoParameter(parameter, new(id, Sys("ipconfig.exe"), ["/flushdns"], false, false, "DNS-Cache leeren")),
            AllowedCommand.IpRenew => NoParameter(parameter, new(id, Sys("ipconfig.exe"), ["/renew"], false, false, "IP-Adresse neu anfordern")),

            // --- Admin (Helper) ---
            AllowedCommand.NetworkResetWinsock => NoParameter(parameter, new(id, Sys("netsh.exe"), ["winsock", "reset"], true, false, "Winsock zurücksetzen")),
            AllowedCommand.NetworkResetIp => NoParameter(parameter, new(id, Sys("netsh.exe"), ["interface", "ip", "reset"], true, false, "TCP/IP-Stack zurücksetzen")),
            AllowedCommand.DnsSetStatic => new(id, Sys("netsh.exe"), DnsStaticArguments(parameter), true, false, "DNS-Server festlegen"),
            AllowedCommand.DnsSetDhcp => new(id, Sys("netsh.exe"), DnsDhcpArguments(parameter), true, false, "DNS-Server automatisch (DHCP)"),

            AllowedCommand.Smb1Disable => NoParameter(parameter, new(id, Sys("dism.exe"),
                ["/Online", "/Disable-Feature", "/FeatureName:SMB1Protocol", "/NoRestart"], true, false, "SMB1-Protokoll deaktivieren")),
            AllowedCommand.RdpDisable => NoParameter(parameter, new(id, Sys("reg.exe"),
                ["add", @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server", "/v", "fDenyTSConnections", "/t", "REG_DWORD", "/d", "1", "/f", "/reg:64"],
                true, false, "Remotedesktop ausschalten")),
            AllowedCommand.FirewallEnableAll => NoParameter(parameter, new(id, Sys("netsh.exe"),
                ["advfirewall", "set", "allprofiles", "state", "on"], true, false, "Firewall in allen Profilen einschalten")),

            AllowedCommand.ScheduledTaskDisable => new(id, Sys("schtasks.exe"), ["/Change", "/TN", TaskPath(parameter), "/Disable"], true, false, "Geplante Aufgabe deaktivieren"),
            AllowedCommand.ScheduledTaskEnable => new(id, Sys("schtasks.exe"), ["/Change", "/TN", TaskPath(parameter), "/Enable"], true, false, "Geplante Aufgabe aktivieren"),

            AllowedCommand.PrivacySetMachine => new(id, Sys("reg.exe"), PrivacyArguments(parameter), true, false, "Datenschutz-Einstellung (alle Benutzer) ändern"),

            _ => null,
        };
    }

    private static CommandSpec NoParameter(string? parameter, CommandSpec spec)
    {
        if (!string.IsNullOrEmpty(parameter)) throw new ArgumentException("Dieser Befehl erwartet keinen Parameter.", nameof(parameter));
        return spec;
    }

    /// <summary>netsh interface ipv4|ipv6 set|add dnsservers name=&lt;Index&gt; … – Adresse nur aus der festen Anbieterliste.</summary>
    private static string[] DnsStaticArguments(string? parameter)
    {
        if (!DnsPresets.TryParseStatic(parameter, out var p) || p is null)
            throw new ArgumentException("Ungültiger DNS-Parameter (erwartet <Index>:<cloudflare|quad9|google>[:<4|6>:<1|2>]).", nameof(parameter));
        var family = p.Family == IpFamily.V6 ? "ipv6" : "ipv4";
        var name = "name=" + p.InterfaceIndex.ToString(CultureInfo.InvariantCulture);
        return p.Slot == 1
            ? ["interface", family, "set", "dnsservers", name, "source=static", "address=" + p.Address, "register=primary", "validate=no"]
            : ["interface", family, "add", "dnsservers", name, "address=" + p.Address, "index=2", "validate=no"];
    }

    private static string[] DnsDhcpArguments(string? parameter)
    {
        if (!DnsPresets.TryParseDhcp(parameter, out var index, out var family))
            throw new ArgumentException("Ungültiger DNS-Parameter (erwartet <Index>[:6]).", nameof(parameter));
        return ["interface", family == IpFamily.V6 ? "ipv6" : "ipv4", "set", "dnsservers", "name=" + index.ToString(CultureInfo.InvariantCulture), "source=dhcp"];
    }

    /// <summary>Aufgabenpfad aus dem Base64url-Token; streng dekodiert und geprüft (nur „\“-getrennte Segmente, keine Steuerzeichen).</summary>
    private static string TaskPath(string? parameter)
    {
        if (!ScheduledTaskLogic.TryDecodeToken(parameter, out var path))
            throw new ArgumentException("Ungültiger Aufgaben-Parameter.", nameof(parameter));
        return path;
    }

    private static IReadOnlyList<string> PrivacyArguments(string? parameter)
    {
        if (!PrivacyCatalog.TryParseMachineParameter(parameter, out var setting, out var value) || setting is null)
            throw new ArgumentException("Ungültiger Datenschutz-Parameter (erwartet <id>:<0|1|default>).", nameof(parameter));
        return PrivacyCatalog.RegArguments(setting, value);
    }
}
