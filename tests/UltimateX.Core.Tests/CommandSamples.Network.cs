using UltimateX.Core.NetSec;
using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

internal static partial class CommandSamples
{
    private static bool Network(AllowedCommand cmd, ref string? parameter)
    {
        switch (cmd)
        {
            // Befehle ohne Parameter
            case AllowedCommand.DnsFlush:
            case AllowedCommand.IpRenew:
            case AllowedCommand.NetworkResetWinsock:
            case AllowedCommand.NetworkResetIp:
            case AllowedCommand.Smb1Disable:
            case AllowedCommand.RdpDisable:
            case AllowedCommand.FirewallEnableAll:
                parameter = null;
                return true;

            case AllowedCommand.DnsSetStatic:
                parameter = "12:cloudflare";
                return true;
            case AllowedCommand.DnsSetDhcp:
                parameter = "12";
                return true;
            case AllowedCommand.ScheduledTaskDisable:
            case AllowedCommand.ScheduledTaskEnable:
                parameter = ScheduledTaskLogic.EncodeToken(@"\Contoso\Update Task");
                return true;
            case AllowedCommand.PrivacySetMachine:
                parameter = "telemetry:1";
                return true;

            default:
                return false;
        }
    }
}
