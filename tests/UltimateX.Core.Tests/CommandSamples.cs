using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

/// <summary>
/// Gültige Beispielparameter für die v1.3-Befehle (für <c>All_external_commands_resolve_to_absolute_trusted_paths</c>).
/// Je Bereich eine Partial-Datei: CommandSamples.Backup.cs, .Network.cs, .System.cs, .Tools.cs.
/// </summary>
internal static partial class CommandSamples
{
    public static bool TryGet(AllowedCommand cmd, out string? parameter)
    {
        parameter = null;
        return Backup(cmd, ref parameter) || Network(cmd, ref parameter) || SystemCommands(cmd, ref parameter) || Tools(cmd, ref parameter);
    }

    /// <summary>Zusätzliche vertrauenswürdige Programmpfade (nur von Administratoren beschreibbar, z. B. Program Files).</summary>
    public static bool IsTrustedExtraPath(AllowedCommand cmd, string path) => TrustedTools(cmd, path);
}
