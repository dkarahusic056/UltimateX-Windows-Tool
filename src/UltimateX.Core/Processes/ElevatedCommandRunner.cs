using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;

namespace UltimateX.Core.Processes;

/// <summary>
/// Führt Allowlist-Befehle mit Adminrechten aus – ausschließlich über Windows UAC (§55).
///
/// Ablauf:
///  1. Die (nicht erhöhte) App startet <c>UltimateX.AdminHelper.exe</c> mit Verb "runas" → UAC-Dialog.
///  2. Übergeben werden nur: Befehlsname (Enum), Run-ID (GUID), optionaler Parameter – alles als einfache Tokens.
///  3. Der Helper validiert ALLES erneut gegen <see cref="CommandCatalog"/> und sendet die Ausgabe über eine
///     abgesicherte Named Pipe zurück (<see cref="HelperPipeProtocol"/>: ACL, Peer-Prüfung, Nonce, Sequenz, Größenlimits).
///  4. Schlägt eine Prüfung fehl, wird die Ausgabe verworfen (fail closed) und ein Sicherheitsereignis protokolliert.
/// So läuft nie die ganze App erhöht, und der Helper kann nichts außerhalb der Allowlist ausführen.
/// </summary>
public static class ElevatedCommandRunner
{
    public const string HelperFileName = "UltimateX.AdminHelper.exe";
    public const int HelperExitInvalidArguments = 9001;
    public const int HelperExitCommandNotFound = 9002;
    public const int HelperExitPeerVerificationFailed = 9003;
    private const int ERROR_CANCELLED = 1223;

    public static string HelperPath => Path.Combine(AppContext.BaseDirectory, HelperFileName);

    /// <summary>Wird nach jedem Admin-Befehl ausgelöst (Technician Mode: Exit Codes und Originalausgabe, §7, §87).</summary>
    public static event Action<AllowedCommand, int, string>? CommandCompleted;

    public static async Task<CommandResult> RunAsync(AllowedCommand command, string? parameter = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Parameter bereits hier validieren (wirft bei ungültigen Werten) – der Helper prüft erneut.
        var spec = CommandCatalog.Build(command, parameter);

        if (!File.Exists(HelperPath))
            return new CommandResult(-1, "Admin-Helper nicht gefunden. Installation beschädigt?", Started: false);

        var integrity = CheckHelperIntegrity();
        if (!integrity.Success)
        {
            AppLog.Security("IntegrityFailure", integrity.Message, LogLevel.Error);
            return new CommandResult(-1, integrity.Message, Started: false);
        }

        var runId = Guid.NewGuid().ToString("N");
        var args = new List<string> { command.ToString(), runId };
        if (parameter is not null) args.Add(parameter.Trim());
        if (!args.All(CommandCatalog.IsSafeToken))
            return new CommandResult(-1, "Ungültiger Parameter.", Started: false);

        // Pipe VOR dem Start des Helpers anlegen (FirstPipeInstance) – der Name ist damit reserviert.
        System.IO.Pipes.NamedPipeServerStream server;
        try
        {
            server = HelperPipeProtocol.CreateServer(runId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            AppLog.Security("HelperIpc", "Pipe konnte nicht angelegt werden", LogLevel.Error, ex.Message);
            return new CommandResult(-1, "Sichere Verbindung zum Admin-Helper konnte nicht hergestellt werden.", Started: false);
        }

        var psi = new ProcessStartInfo
        {
            FileName = HelperPath,
            Arguments = string.Join(' ', args),
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        AppLog.Security("AdminOperation", $"UAC-Anforderung für: {spec.DisplayName}");

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ERROR_CANCELLED)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            AppLog.Security("AdminOperation", $"UAC abgelehnt: {spec.DisplayName}", LogLevel.Warning);
            return new CommandResult(-1, "Die Administratorfreigabe wurde abgelehnt.", Cancelled: true, Started: false);
        }

        if (process is null)
        {
            await server.DisposeAsync().ConfigureAwait(false);
            return new CommandResult(-1, "Prozess konnte nicht gestartet werden.", Started: false);
        }

        using (process)
        await using (server)
        {
            var collected = new StringBuilder();
            int? reportedExit = null;
            string? ipcError = null;
            using var ipcCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var ipc = ReceiveAsync(server, process.Id, runId, collected, progress, code => reportedExit = code, ipcCts.Token);

            while (!process.HasExited)
            {
                if (ct.IsCancellationRequested)
                {
                    // Der erhöhte Prozess kann von einem nicht erhöhten Prozess nicht beendet werden – das ist gewollt.
                    return new CommandResult(-1, "Anzeige abgebrochen. Der Vorgang läuft im Hintergrund weiter.", Cancelled: true);
                }
                await Task.WhenAny(ipc, Task.Delay(500, CancellationToken.None)).ConfigureAwait(false);
            }
            // Restliche Rahmen nach Prozessende kurz abwarten, dann abbrechen.
            if (await Task.WhenAny(ipc, Task.Delay(3000, CancellationToken.None)).ConfigureAwait(false) != ipc) ipcCts.Cancel();
            try { ipcError = await ipc.ConfigureAwait(false); }
            catch (OperationCanceledException) { ipcError = null; }

            var exitCode = process.ExitCode;
            if (ipcError is not null)
            {
                AppLog.Security("HelperIpc", "Ausgabe des Admin-Helpers verworfen", LogLevel.Error, ipcError);
                collected.Clear();
                collected.Append("Die Ausgabe des Admin-Helpers wurde aus Sicherheitsgründen verworfen (" + ipcError + ").");
            }
            else if (reportedExit is { } rc && rc != exitCode)
            {
                AppLog.Security("HelperIpc", "Exit-Code der Pipe weicht vom Prozess ab", LogLevel.Warning, $"Pipe={rc} Prozess={exitCode}");
            }

            var message = exitCode switch
            {
                HelperExitInvalidArguments => "Der Admin-Helper hat die Anfrage abgelehnt (ungültige Parameter).",
                HelperExitCommandNotFound => "Das Windows-Programm wurde nicht gefunden.",
                HelperExitPeerVerificationFailed => "Der Admin-Helper hat die Verbindung abgelehnt (Gegenstelle nicht vertrauenswürdig).",
                _ => collected.ToString(),
            };
            AppLog.Security("AdminOperation", $"{spec.DisplayName} beendet", exitCode == 0 ? LogLevel.Info : LogLevel.Warning, $"ExitCode={exitCode}");
            CommandCompleted?.Invoke(command, exitCode, message);
            return new CommandResult(exitCode, message);
        }
    }

    /// <summary>
    /// Empfängt die Ausgabe des Helpers. Liefert null bei Erfolg oder eine Fehlerbeschreibung (Protokoll-/Peer-Verletzung).
    /// </summary>
    private static async Task<string?> ReceiveAsync(System.IO.Pipes.NamedPipeServerStream server, int expectedPid, string runId,
        StringBuilder collected, IProgress<string>? progress, Action<int> onExit, CancellationToken ct)
    {
        try
        {
            await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return null; // Helper hat sich nicht verbunden (z. B. interaktiver Befehl) – keine Ausgabe.
        }

        // Nur der selbst gestartete Prozess darf sich verbinden.
        var pid = HelperPipeProtocol.ClientProcessId(server);
        if (pid != expectedPid)
        {
            server.Disconnect();
            return $"unerwartete Gegenstelle (PID {pid?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"})";
        }

        var nonce = HelperPipeProtocol.NewNonce();
        await HelperPipeProtocol.WriteAsync(server, new HelperFrame(HelperPipeProtocol.Version, "hello", runId, nonce, 0, null, null), ct).ConfigureAwait(false);

        long seq = 0, total = 0;
        try
        {
            while (true)
            {
                var frame = await HelperPipeProtocol.ReadAsync(server, ct).ConfigureAwait(false);
                if (frame is null) return null;
                var error = HelperPipeProtocol.Validate(frame, runId, nonce, seq, "out", "exit");
                if (error is not null) return "Protokollverletzung: " + error;
                seq = frame.Seq;
                if (frame.Type == "exit")
                {
                    onExit(frame.Code!.Value);
                    return null;
                }
                total += frame.Data?.Length ?? 0;
                if (total > HelperPipeProtocol.MaxTotalBytes) return "Ausgabe zu groß";
                if (frame.Data is { Length: > 0 } d)
                {
                    collected.Append(d);
                    progress?.Report(d);
                }
            }
        }
        catch (InvalidDataException ex)
        {
            return "Protokollverletzung: " + ex.Message;
        }
        catch (IOException)
        {
            return null; // Helper beendet – vorhandene Ausgabe bleibt gültig.
        }
    }

    /// <summary>
    /// Ist die App selbst signiert, muss der Helper vom selben Zertifikat gültig signiert sein (§62).
    /// Unsignierte Entwicklungs-Builds werden erlaubt, aber protokolliert.
    /// </summary>
    public static OperationResult CheckHelperIntegrity()
    {
        var self = Environment.ProcessPath;
        if (self is null) return OperationResult.Fail("Eigener Programmpfad nicht ermittelbar.");

        var selfSig = SignatureVerifier.Verify(self);
        if (selfSig.Status != SignatureStatus.Valid)
        {
            AppLog.Security("IntegrityCheck", "Entwicklungs-Build: Signaturprüfung des Helpers übersprungen.", LogLevel.Warning);
            return OperationResult.Ok("Entwicklungs-Build");
        }

        var helperSig = SignatureVerifier.Verify(HelperPath);
        if (helperSig.Status != SignatureStatus.Valid)
            return OperationResult.Fail($"Admin-Helper-Signatur ungültig: {helperSig.StatusText}");
        if (!string.Equals(helperSig.Thumbprint, selfSig.Thumbprint, StringComparison.OrdinalIgnoreCase))
            return OperationResult.Fail("Admin-Helper ist von einem anderen Zertifikat signiert.");
        return OperationResult.Ok("Helper verifiziert");
    }
}
