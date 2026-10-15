using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

internal static partial class CommandSamples
{
    private static bool Tools(AllowedCommand cmd, ref string? parameter)
    {
        switch (cmd)
        {
            case AllowedCommand.GeekbenchLaunch:
            case AllowedCommand.NvidiaDriverInstall:
                parameter = null; // ohne Parameter
                return true;
            default:
                return false;
        }
    }

    /// <summary>Geekbench: nur die festen Pfade unter %ProgramFiles%\Geekbench 6|5\ (nur von Administratoren beschreibbar).</summary>
    private static bool TrustedTools(AllowedCommand cmd, string path)
    {
        // NVIDIA-Treiber: fester Ablageort der App; Start nur durch die App nach Authenticode-Prüfung (Helper lehnt ab).
        if (cmd == AllowedCommand.NvidiaDriverInstall) return path.Equals(CommandCatalog.NvidiaDriverPath, StringComparison.OrdinalIgnoreCase);
        if (cmd != AllowedCommand.GeekbenchLaunch) return false;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        return path.StartsWith(Path.Combine(pf, "Geekbench"), StringComparison.OrdinalIgnoreCase)
               && CommandCatalog.GeekbenchCandidates.Contains(path, StringComparer.OrdinalIgnoreCase);
    }
}
