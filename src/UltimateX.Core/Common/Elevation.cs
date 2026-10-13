using System.Security.Principal;

namespace UltimateX.Core.Common;

/// <summary>Ermittelt die Rechte des aktuellen Prozesses. Die App läuft standardmäßig OHNE Adminrechte (§55).</summary>
public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static string CurrentUserName => Environment.UserName;
}
