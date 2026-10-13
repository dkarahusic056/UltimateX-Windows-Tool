using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;
using UltimateX.Services.Native;

namespace UltimateX.Services.SystemTools;

/// <summary>Windows Power Control (§36). Shutdown/Neustart über shutdown.exe aus der Allowlist (mit Countdown, abbrechbar).</summary>
public sealed class PowerService
{
    public OperationResult Lock()
        => NativeMethods.LockWorkStation() ? OperationResult.Ok("PC gesperrt.") : OperationResult.Fail("Sperren fehlgeschlagen.");

    public OperationResult Sleep()
    {
        AppLog.Info("Power", "Energiesparmodus");
        return NativeMethods.SetSuspendState(false, false, false)
            ? OperationResult.Ok("Energiesparmodus.")
            : OperationResult.Fail("Energiesparmodus wird auf diesem System nicht unterstützt oder ist deaktiviert.");
    }

    public OperationResult SignOut()
    {
        AppLog.Info("Power", "Abmelden");
        return NativeMethods.ExitWindowsEx(NativeMethods.EWX_LOGOFF, NativeMethods.SHTDN_REASON_FLAG_PLANNED)
            ? OperationResult.Ok("Abmeldung läuft.")
            : OperationResult.Fail("Abmelden fehlgeschlagen.");
    }

    public Task<OperationResult> ShutdownAsync(int countdownSeconds) => RunAsync(AllowedCommand.Shutdown, countdownSeconds);

    public Task<OperationResult> RestartAsync(int countdownSeconds) => RunAsync(AllowedCommand.Restart, countdownSeconds);

    public async Task<OperationResult> AbortAsync()
    {
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.AbortShutdown)).ConfigureAwait(false);
        AppLog.Info("Power", "Geplantes Herunterfahren abgebrochen", $"ExitCode={r.ExitCode}");
        return r.ExitCode == 0 ? OperationResult.Ok("Abgebrochen.") : OperationResult.Fail("Kein geplantes Herunterfahren aktiv.");
    }

    private static async Task<OperationResult> RunAsync(AllowedCommand cmd, int seconds)
    {
        var spec = CommandCatalog.Build(cmd, seconds.ToString(CultureInfo.InvariantCulture));
        var r = await ProcessRunner.RunAsync(spec).ConfigureAwait(false);
        AppLog.Info("Power", spec.DisplayName, $"Countdown={seconds}s, ExitCode={r.ExitCode}");
        return r.ExitCode == 0
            ? OperationResult.Ok(seconds > 0 ? $"{spec.DisplayName} in {seconds} Sekunden." : $"{spec.DisplayName} wird ausgeführt.")
            : OperationResult.Fail($"{spec.DisplayName} fehlgeschlagen.", r.Output);
    }
}
