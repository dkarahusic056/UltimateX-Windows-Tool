using System.Diagnostics;
using System.Text;
using UltimateX.Core.Common;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.AdminHelper;

/// <summary>
/// Erhöhter Helper. Aufruf: UltimateX.AdminHelper.exe &lt;AllowedCommand&gt; &lt;RunId&gt; [Parameter]
///
/// Sicherheitsregeln:
///  - Nimmt KEINE Befehlszeilen, Skripte oder Pfade entgegen – nur einen Enum-Namen aus der Allowlist.
///  - Validiert jeden Parameter erneut (Defense in Depth – der Aufrufer ist nicht vertrauenswürdig).
///  - Gibt die Ausgabe nur über die abgesicherte Named Pipe der App zurück (<see cref="HelperPipeProtocol"/>):
///    Verbindung mit Impersonation-Level „Identification“, Prüfung des Servers (UltimateX.exe aus dem eigenen
///    Installationsordner), Sitzungs-Nonce, Sequenznummern, Größenlimits. Schlägt die Prüfung fehl → Abbruch (fail closed).
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length is < 2 or > 3) return ElevatedCommandRunner.HelperExitInvalidArguments;
        if (!args.All(CommandCatalog.IsSafeToken)) return ElevatedCommandRunner.HelperExitInvalidArguments;
        if (!Enum.TryParse<AllowedCommand>(args[0], ignoreCase: false, out var command) || !Enum.IsDefined(command))
            return ElevatedCommandRunner.HelperExitInvalidArguments;
        if (!InputValidator.IsValidRunId(args[1])) return ElevatedCommandRunner.HelperExitInvalidArguments;

        CommandSpec spec;
        try
        {
            spec = CommandCatalog.Build(command, args.Length == 3 ? args[2] : null);
        }
        catch (ArgumentException)
        {
            return ElevatedCommandRunner.HelperExitInvalidArguments;
        }

        if (!spec.RequiresElevation) return ElevatedCommandRunner.HelperExitInvalidArguments; // Helper nur für Admin-Befehle
        if (!spec.IsInternal && !File.Exists(spec.FilePath)) return ElevatedCommandRunner.HelperExitCommandNotFound;

        if (spec.IsInteractive)
        {
            using var p = Process.Start(new ProcessStartInfo(spec.FilePath) { UseShellExecute = true });
            return 0;
        }

        var channel = OutputChannel.Connect(args[1]);
        if (channel is { Trusted: false }) return ElevatedCommandRunner.HelperExitPeerVerificationFailed;
        try
        {
            var progress = new SyncProgress(chunk => channel?.Write(chunk));
            var exit = spec.IsInternal ? InternalActions.Run(spec, progress) : ProcessRunner.RunAsync(spec, progress).GetAwaiter().GetResult().ExitCode;
            channel?.Exit(exit);
            return exit;
        }
        finally
        {
            channel?.Dispose();
            CleanupOldRuns();
        }
    }

    private static void CleanupOldRuns()
    {
        try
        {
            if (!Directory.Exists(AppPaths.HelperRunDirectory)) return;
            foreach (var f in Directory.GetFiles(AppPaths.HelperRunDirectory, "*.log"))
            {
                var info = new FileInfo(f);
                if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.CreationTimeUtc < DateTime.UtcNow.AddMinutes(-10))
                    info.Delete();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Rückkanal zur App über die Named Pipe. Ohne Verbindung (App wartet nicht mehr) läuft der Befehl trotzdem –
    /// nur ohne sichtbare Ausgabe. Mit Verbindung zu einem nicht vertrauenswürdigen Server wird abgebrochen.
    /// </summary>
    internal sealed class OutputChannel : IDisposable
    {
        private readonly System.IO.Pipes.NamedPipeClientStream? _pipe;
        private readonly string _runId;
        private readonly string _nonce;
        private readonly object _gate = new();
        private long _seq;
        private bool _broken;

        public bool Trusted { get; }

        private OutputChannel(System.IO.Pipes.NamedPipeClientStream? pipe, string runId, string nonce, bool trusted)
        {
            _pipe = pipe;
            _runId = runId;
            _nonce = nonce;
            Trusted = trusted;
        }

        public static OutputChannel? Connect(string runId)
        {
            var pipe = new System.IO.Pipes.NamedPipeClientStream(".", HelperPipeProtocol.PipeName(runId), System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.None, System.Security.Principal.TokenImpersonationLevel.Identification);
            try
            {
                pipe.Connect(10_000);
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
            {
                pipe.Dispose();
                return null;
            }
            if (!HelperPipeProtocol.IsTrustedServer(pipe))
            {
                pipe.Dispose();
                return new OutputChannel(null, runId, "", trusted: false);
            }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var hello = HelperPipeProtocol.ReadAsync(pipe, cts.Token).GetAwaiter().GetResult();
                if (hello is null || hello.V != HelperPipeProtocol.Version || hello.Type != "hello" || hello.Run != runId
                    || hello.Nonce is not { Length: 64 } || !hello.Nonce.All(Uri.IsHexDigit))
                {
                    pipe.Dispose();
                    return new OutputChannel(null, runId, "", trusted: false);
                }
                return new OutputChannel(pipe, runId, hello.Nonce, trusted: true);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
            {
                pipe.Dispose();
                return null;
            }
        }

        public void Write(string text)
        {
            for (var i = 0; i < text.Length; i += HelperPipeProtocol.MaxDataChars)
                Send("out", text.Substring(i, Math.Min(HelperPipeProtocol.MaxDataChars, text.Length - i)), null);
        }

        public void Exit(int code) => Send("exit", null, code);

        private void Send(string type, string? data, int? code)
        {
            lock (_gate)
            {
                if (_broken || !Trusted || _pipe is null) return;
                try
                {
                    var bytes = HelperPipeProtocol.Encode(new HelperFrame(HelperPipeProtocol.Version, type, _runId, _nonce, ++_seq, data, code));
                    _pipe.Write(bytes, 0, bytes.Length);
                    _pipe.Flush();
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidDataException)
                {
                    _broken = true; // App hat die Verbindung beendet – Befehl läuft weiter.
                }
            }
        }

        public void Dispose() => _pipe?.Dispose();
    }

    /// <summary>IProgress ohne SynchronizationContext – Ausgabe wird sofort geschrieben.</summary>
    internal sealed class SyncProgress(Action<string> handler) : IProgress<string>
    {
        public void Report(string value) => handler(value);
    }
}
