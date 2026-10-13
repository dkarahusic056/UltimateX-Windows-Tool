using System.Text.Json;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.AdminHelper;

/// <summary>
/// Absturzabbilder (§103). Minidumps (C:\Windows\Minidump) sind nur für Administratoren lesbar.
///
/// ReadHeaders: liest NUR den Dateikopf (BugCheck-Code + 4 Parameter) – der Dumpinhalt (Speicher) verlässt
///              den Helper nie; es werden keine Dumps kopiert (enthalten u. U. sensible Speicherinhalte).
/// Analyze:     startet – falls installiert – den Microsoft-Kernel-Debugger kd.exe (Debugging Tools for Windows)
///              mit „!analyze -v“. kd.exe muss gültig von Microsoft signiert sein; „-noshell“ verhindert Shell-Befehle.
///              Symbole kommen ausschließlich vom Microsoft-Symbolserver (HTTPS).
/// </summary>
internal static class CrashDumps
{
    private sealed record DumpHeader(string File, DateTime Time, long Size, uint Code, ulong P1, ulong P2, ulong P3, ulong P4, bool Is64Bit, bool Valid);

    public static int ReadHeaders(IProgress<string> output)
    {
        var files = new List<FileInfo>();
        if (Directory.Exists(CommandCatalog.MinidumpDirectory))
            files.AddRange(new DirectoryInfo(CommandCatalog.MinidumpDirectory).GetFiles("*.dmp")
                .Where(f => !f.Attributes.HasFlag(FileAttributes.ReparsePoint)));
        var memory = new FileInfo(CommandCatalog.MemoryDumpPath);
        if (memory.Exists && !memory.Attributes.HasFlag(FileAttributes.ReparsePoint)) files.Add(memory);

        var result = new List<DumpHeader>();
        foreach (var f in files.OrderByDescending(f => f.LastWriteTimeUtc).Take(40))
        {
            var h = ReadHeader(f);
            result.Add(h);
            output.Report($"{f.Name}: {(h.Valid ? $"BugCheck 0x{h.Code:X8}" : "Kopf nicht lesbar")}\n");
        }
        if (result.Count == 0) output.Report("Keine Absturzabbilder vorhanden.\n");
        output.Report(InternalActions.ResultPrefix + JsonSerializer.Serialize(result) + "\n");
        return 0;
    }

    /// <summary>
    /// DUMP_HEADER64: "PAGE" + "DU64", BugCheckCode @0x38 (ULONG), Parameter 1–4 @0x40..0x58 (ULONG64).
    /// DUMP_HEADER32: "PAGE" + "DUMP", BugCheckCode @0x40, Parameter @0x44..0x50 (ULONG).
    /// </summary>
    private static DumpHeader ReadHeader(FileInfo f)
    {
        try
        {
            using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var b = new byte[0x60];
            if (fs.Read(b, 0, b.Length) < b.Length) return Invalid(f);
            var sig = System.Text.Encoding.ASCII.GetString(b, 0, 4);
            var valid = System.Text.Encoding.ASCII.GetString(b, 4, 4);
            if (sig != "PAGE") return Invalid(f);
            if (valid == "DU64")
                return new DumpHeader(f.Name, f.LastWriteTime, f.Length, BitConverter.ToUInt32(b, 0x38),
                    BitConverter.ToUInt64(b, 0x40), BitConverter.ToUInt64(b, 0x48), BitConverter.ToUInt64(b, 0x50), BitConverter.ToUInt64(b, 0x58), true, true);
            if (valid == "DUMP")
                return new DumpHeader(f.Name, f.LastWriteTime, f.Length, BitConverter.ToUInt32(b, 0x40),
                    BitConverter.ToUInt32(b, 0x44), BitConverter.ToUInt32(b, 0x48), BitConverter.ToUInt32(b, 0x4C), BitConverter.ToUInt32(b, 0x50), false, true);
            return Invalid(f);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Invalid(f);
        }
    }

    private static DumpHeader Invalid(FileInfo f) => new(f.Name, f.LastWriteTime, f.Length, 0, 0, 0, 0, 0, false, false);

    /// <summary>Bekannte Installationsorte von kd.exe (Windows SDK / WDK, x64). Nur unter Program Files (admin-geschützt).</summary>
    public static string? FindKd()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x64", "kd.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "Debuggers", "x64", "kd.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static int Analyze(string dumpName, IProgress<string> output)
    {
        var path = CommandCatalog.ResolveDumpPath(dumpName); // validiert erneut
        var info = new FileInfo(path);
        if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            output.Report("Absturzabbild nicht gefunden.\n");
            return 3;
        }

        var kd = FindKd();
        if (kd is null)
        {
            output.Report("Der Microsoft-Debugger (kd.exe, „Debugging Tools for Windows“) ist nicht installiert.\n");
            output.Report(InternalActions.ResultPrefix + "{\"debugger\":false}\n");
            return 4;
        }
        var sig = SignatureVerifier.Verify(kd);
        if (sig.Status != SignatureStatus.Valid || sig.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) != true)
        {
            output.Report($"kd.exe wurde nicht ausgeführt: Signatur ungültig oder nicht von Microsoft ({sig.StatusText}).\n");
            return 5;
        }

        var symbols = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UltimateX", "Symbols");
        Directory.CreateDirectory(symbols);
        output.Report("Analysiere mit kd.exe (!analyze -v). Beim ersten Mal werden Symbole von Microsoft geladen – das kann einige Minuten dauern …\n");

        var spec = new CommandSpec(AllowedCommand.AnalyzeMinidump, kd,
            ["-z", path, "-noshell", "-y", $"srv*{symbols}*https://msdl.microsoft.com/download/symbols", "-c", "!analyze -v; q"],
            true, false, "kd !analyze");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var r = ProcessRunner.RunAsync(spec, output, cts.Token).GetAwaiter().GetResult();
        if (r.Cancelled)
        {
            output.Report("Zeitüberschreitung bei der Analyse (6 Minuten).\n");
            return 6;
        }
        output.Report(InternalActions.ResultPrefix + JsonSerializer.Serialize(new { debugger = true, exitCode = r.ExitCode }) + "\n");
        return 0;
    }
}
