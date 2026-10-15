using System.Globalization;
using System.Text.RegularExpressions;
using UltimateX.Core.Common;

namespace UltimateX.Core.Security;

/// <summary>v1.3: Erweiterungen der Allowlist, je Bereich in einer eigenen Partial-Datei (gleiche Sicherheitsregeln wie <see cref="Build"/>).</summary>
public static partial class CommandCatalog
{
    private static CommandSpec? BuildExtension(AllowedCommand id, string? parameter)
        => BuildBackupCommand(id, parameter) ?? BuildNetworkCommand(id, parameter) ?? BuildSystemCommand(id, parameter) ?? BuildToolCommand(id, parameter);
}
