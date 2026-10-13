using System.Diagnostics;
using System.Globalization;
using System.Text;
using UltimateX.Core.Security;

namespace UltimateX.Core.Processes;

public sealed record CommandResult(int ExitCode, string Output, bool Cancelled = false, bool Started = true);

/// <summary>
/// Führt ausschließlich <see cref="CommandSpec"/> aus der Allowlist aus.
/// Keine Shell (UseShellExecute=false), kein cmd.exe /c, Argumente über ArgumentList – damit ist
/// Command Injection strukturell ausgeschlossen (§67).
/// </summary>
public static class ProcessRunner
{
    static ProcessRunner()
    {
        // OEM-Codepages (z. B. 850) für DISM/CHKDSK-Ausgaben verfügbar machen.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<CommandResult> RunAsync(CommandSpec spec, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (spec.IsInternal) throw new InvalidOperationException("Interne Aktionen werden nur im Admin-Helper ausgeführt.");
        if (!File.Exists(spec.FilePath))
            return new CommandResult(-1, $"Programm nicht gefunden: {spec.FilePath}", Started: false);

        var psi = new ProcessStartInfo
        {
            FileName = spec.FilePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(spec.FilePath)!,
        };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close(); // keine interaktiven Rückfragen möglich

        var stdoutTask = ReadAllAsync(process.StandardOutput.BaseStream, progress, spec.Utf8Output, ct);
        var stderrTask = ReadAllAsync(process.StandardError.BaseStream, null, spec.Utf8Output, ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return new CommandResult(-1, "Abgebrochen.", Cancelled: true);
        }

        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var output = string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + Environment.NewLine + stderr;
        return new CommandResult(process.ExitCode, output);
    }

    /// <summary>
    /// Startet einen interaktiven Allowlist-Befehl (z. B. msiexec mit eigener Oberfläche) ohne auf das Ende zu warten.
    /// Keine Shell; Argumente über ArgumentList.
    /// </summary>
    public static bool StartDetached(CommandSpec spec)
    {
        if (spec.IsInternal || !File.Exists(spec.FilePath)) return false;
        var psi = new ProcessStartInfo { FileName = spec.FilePath, UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(spec.FilePath)! };
        foreach (var a in spec.Arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        return p is not null;
    }

    private static async Task<string> ReadAllAsync(Stream stream, IProgress<string>? progress, bool utf8, CancellationToken ct)
    {
        var all = new StringBuilder();
        var buffer = new byte[4096];
        var chars = new char[8192];
        Decoder? decoder = null;
        int read;
        try
        {
            while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                // Encoding anhand des ersten Blocks festlegen; der Decoder ist zustandsbehaftet und
                // verarbeitet daher auch Zeichen, die über Blockgrenzen hinweg gesplittet sind.
                decoder ??= (utf8 ? new UTF8Encoding(false) : DetectEncoding(buffer.AsSpan(0, read))).GetDecoder();
                var count = decoder.GetChars(buffer, 0, read, chars, 0);
                var text = Clean(new string(chars, 0, count));
                all.Append(text);
                if (text.Length > 0) progress?.Report(text);
            }
        }
        catch (OperationCanceledException)
        {
        }
        return all.ToString();
    }

    /// <summary>
    /// SFC schreibt bei Umleitung UTF-16LE, DISM/CHKDSK schreiben in der OEM-Codepage der Systemsprache.
    /// </summary>
    internal static Encoding DetectEncoding(ReadOnlySpan<byte> sample)
    {
        int zeros = 0;
        for (int i = 1; i < sample.Length; i += 2) if (sample[i] == 0) zeros++;
        if (sample.Length >= 4 && zeros > sample.Length / 4) return Encoding.Unicode;
        try { return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
        catch (ArgumentException) { return Encoding.UTF8; }
    }

    private static string Clean(string text)
    {
        text = text.Replace("\0", string.Empty, StringComparison.Ordinal).Replace("\r\r\n", "\n", StringComparison.Ordinal);
        // Fortschrittsanimationen (z. B. winget-Spinner "\r-\|/") entfernen: nur der Text nach dem letzten \r einer Zeile zählt.
        if (text.Contains('\r'))
        {
            var lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimEnd('\r');
                var idx = l.LastIndexOf('\r');
                lines[i] = idx >= 0 ? l[(idx + 1)..] : l;
            }
            text = string.Join('\n', lines);
        }
        return text;
    }
}
