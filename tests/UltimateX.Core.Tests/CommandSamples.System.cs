using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

internal static partial class CommandSamples
{
    private static bool SystemCommands(AllowedCommand cmd, ref string? parameter)
    {
        switch (cmd)
        {
            case AllowedCommand.ShutdownAt:
            case AllowedCommand.RestartAt:
                parameter = "3600"; // in einer Stunde
                return true;
            default:
                return false;
        }
    }
}
