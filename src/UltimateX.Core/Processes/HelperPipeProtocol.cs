using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace UltimateX.Core.Processes;

/// <summary>Ein Nachrichtenrahmen zwischen App und Admin-Helper.</summary>
public sealed record HelperFrame(int V, string Type, string Run, string Nonce, long Seq, string? Data, int? Code);

/// <summary>
/// IPC zwischen der (nicht erhöhten) App und dem erhöhten Admin-Helper über eine Named Pipe (v1.1, THREAT_MODEL T-28).
///
///  • Pipe-Name enthält eine zufällige Run-ID; die App erzeugt die Pipe VOR dem Start des Helpers mit
///    FirstPipeInstance → niemand kann den Namen vorher belegen (Pipe-Squatting).
///  • ACL: nur der aktuelle Benutzer und die Administratorengruppe; Netzwerkzugriff ausdrücklich verweigert.
///  • Peer-Prüfung: Die App akzeptiert nur den Prozess, den sie selbst gestartet hat (PID-Vergleich);
///    der Helper akzeptiert nur einen Server, dessen Programmdatei UltimateX.exe im eigenen Installationsordner ist.
///  • Der Helper verbindet sich mit Impersonation-Level „Identification“ → die App kann den Admin-Token nicht übernehmen.
///  • Sitzungs-Nonce (256 Bit) + fortlaufende Sequenznummer in jedem Rahmen → kein Replay aus anderen Sitzungen.
///  • Längenpräfix, max. 64 KB je Rahmen, max. 16 MB je Lauf, strikte Schema-Prüfung, Zeitlimits.
///  • Über die Pipe werden KEINE Befehle übertragen – nur Ausgabe/Exit-Code vom Helper zur App. Der Befehl selbst
///    kommt weiterhin ausschließlich als Allowlist-Enum über die Kommandozeile und wird im Helper erneut geprüft.
/// </summary>
public static class HelperPipeProtocol
{
    public const int Version = 1;
    public const int MaxFrameBytes = 64 * 1024;
    public const long MaxTotalBytes = 16L * 1024 * 1024;
    public const int MaxDataChars = 16 * 1024;
    public const string AppExeName = "UltimateX.exe";

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string PipeName(string runId) => $"UltimateX.Helper.{runId}";

    public static string NewNonce() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>Server-Pipe mit restriktiver ACL (aktueller Benutzer + Administratoren, kein Netzwerk).</summary>
    public static NamedPipeServerStream CreateServer(string runId)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Benutzer-SID nicht ermittelbar.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        return NamedPipeServerStreamAcl.Create(PipeName(runId), PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, MaxFrameBytes, MaxFrameBytes, security);
    }

    // ------------------------------------------------------------------ Rahmen

    public static byte[] Encode(HelperFrame frame)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(frame, Json);
        if (json.Length > MaxFrameBytes - 4) throw new InvalidDataException("Rahmen zu groß.");
        var buffer = new byte[json.Length + 4];
        BinaryPrimitives.WriteInt32LittleEndian(buffer, json.Length);
        json.CopyTo(buffer, 4);
        return buffer;
    }

    public static async Task WriteAsync(Stream stream, HelperFrame frame, CancellationToken ct)
    {
        var bytes = Encode(frame);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Liest einen Rahmen. null = Verbindung sauber beendet. Wirft bei Protokollverletzung.</summary>
    public static async Task<HelperFrame?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct).ConfigureAwait(false)) return null;
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes - 4) throw new InvalidDataException("Ungültige Rahmenlänge.");
        var body = new byte[length];
        if (!await ReadExactAsync(stream, body, ct).ConfigureAwait(false)) throw new InvalidDataException("Rahmen unvollständig.");
        return Parse(body);
    }

    internal static HelperFrame Parse(byte[] body)
    {
        HelperFrame? f;
        try { f = JsonSerializer.Deserialize<HelperFrame>(body, Json); }
        catch (JsonException ex) { throw new InvalidDataException("Ungültiges JSON im Rahmen.", ex); }
        return f ?? throw new InvalidDataException("Leerer Rahmen.");
    }

    private static async Task<bool> ReadExactAsync(Stream s, byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await s.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) return read == 0 ? false : throw new InvalidDataException("Verbindung mitten im Rahmen getrennt.");
            read += n;
        }
        return true;
    }

    /// <summary>
    /// Schema-Prüfung eines Rahmens: Version, Typ, Run-ID, Nonce und streng steigende Sequenznummer.
    /// Liefert eine Fehlermeldung oder null.
    /// </summary>
    public static string? Validate(HelperFrame f, string runId, string nonce, long lastSeq, params string[] allowedTypes)
    {
        if (f.V != Version) return "Protokollversion";
        if (!allowedTypes.Contains(f.Type, StringComparer.Ordinal)) return "Rahmentyp";
        if (!string.Equals(f.Run, runId, StringComparison.Ordinal)) return "Run-ID";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(f.Nonce ?? ""), Encoding.ASCII.GetBytes(nonce))) return "Nonce";
        if (f.Seq != lastSeq + 1) return "Sequenz (Replay/Verlust)";
        if (f.Data is { Length: > MaxDataChars }) return "Datengröße";
        if (f.Type == "exit" && f.Code is null) return "Exit-Code fehlt";
        return null;
    }

    // ------------------------------------------------------------------ Peer-Prüfung

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);

    public static int? ClientProcessId(NamedPipeServerStream server) =>
        GetNamedPipeClientProcessId(server.SafePipeHandle, out var pid) ? (int)pid : null;

    public static int? ServerProcessId(NamedPipeClientStream client) =>
        GetNamedPipeServerProcessId(client.SafePipeHandle, out var pid) ? (int)pid : null;

    /// <summary>Ist der Pipe-Server die UltimateX.exe aus dem Installationsordner des Helpers?</summary>
    public static bool IsTrustedServer(NamedPipeClientStream client)
    {
        var pid = ServerProcessId(client);
        if (pid is null) return false;
        try
        {
            using var p = Process.GetProcessById(pid.Value);
            var path = p.MainModule?.FileName;
            var expected = Path.Combine(AppContext.BaseDirectory, AppExeName);
            return path is not null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return false;
        }
    }
}
