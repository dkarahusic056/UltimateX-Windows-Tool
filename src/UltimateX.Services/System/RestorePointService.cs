using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.SystemTools;

/// <summary>
/// Windows-Wiederherstellungspunkte (§85, §86) über die WMI-Klasse SystemRestore (im Admin-Helper).
/// Wird vor Treiber-Updates und größeren Reparaturen angeboten bzw. automatisch erstellt (Einstellung).
/// </summary>
public sealed class RestorePointService
{
    /// <summary>Ob der Computerschutz grundsätzlich aktiv ist (Registry-Hinweis, nur lesend).</summary>
    public static bool? IsSystemRestoreEnabled()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore");
        return k?.GetValue("RPSessionInterval") switch
        {
            int i => i != 0,
            _ => null,
        };
    }

    public async Task<OperationResult> CreateAsync(CancellationToken ct = default)
    {
        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.CreateRestorePoint, null, null, ct).ConfigureAwait(false);
        if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
        AppLog.Info("RestorePoint", "Wiederherstellungspunkt angefordert", $"ExitCode={r.ExitCode}");
        return r.ExitCode == 0 ? OperationResult.Ok(r.Output.Trim()) : OperationResult.Fail(r.Output.Trim());
    }
}
