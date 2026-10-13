using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.Health;

public sealed record BugCheckInfo(uint Code, string Name, string Meaning, string Advice);

public sealed record BlueScreenEvent(DateTime Time, uint Code, string Parameters, string? DumpFile, string Source)
{
    public BugCheckInfo Info => BugCheckCatalog.Lookup(Code);
}

public sealed record AppCrashGroup(string Application, string Module, string ExceptionCode, int Count, DateTime Last, bool IsHang);

public sealed record HardwareErrorGroup(string Kind, long EventId, int Count, DateTime Last, string Explanation);

public sealed record CrashHint(DataState Severity, string Title, string Detail);

public sealed record CrashReport(
    int Days,
    IReadOnlyList<BlueScreenEvent> BlueScreens,
    int PowerLossResets,
    int DisplayDriverResets,
    IReadOnlyList<HardwareErrorGroup> HardwareErrors,
    IReadOnlyList<AppCrashGroup> AppCrashes,
    IReadOnlyList<CrashHint> Hints);

public sealed record DumpFileInfo(string File, DateTime Time, long Size, uint Code, string Parameters, bool Valid)
{
    public BugCheckInfo Info => BugCheckCatalog.Lookup(Code);
}

public sealed record DebuggerAnalysis(bool DebuggerAvailable, string? ProbablyCausedBy, string? Module, string? Image, string? Process, string? FailureBucket, string? BugCheckText, string RawOutput);

/// <summary>
/// Absturzanalyse (§103): Bluescreens, Hardwarefehler (WHEA), Grafiktreiber-Resets und Programmabstürze.
///
/// Quellen (alle lokal, ohne Upload):
///  - Ereignisprotokolle System/Application (für Standardbenutzer lesbar).
///  - Kopf der Minidumps (BugCheck-Code + Parameter) – erfordert Adminrechte, läuft im Helper.
///  - Optional: Microsoft-Debugger kd.exe („!analyze -v“) für den wahrscheinlich verursachenden Treiber.
///
/// Hinweise sind als Vermutung formuliert; ohne Debugger-Analyse wird kein „Schuldiger“ behauptet.
/// </summary>
public sealed partial class CrashAnalysisService
{
    public CrashReport Analyze(int days = 90)
    {
        var ms = (long)TimeSpan.FromDays(days).TotalMilliseconds;
        string Q(string cond) => $"*[System[{cond} and TimeCreated[timediff(@SystemTime) <= {ms}]]]";

        // --- Bluescreens: WER-SystemErrorReporting 1001 (mit Dumpdatei) + Kernel-Power 41 mit BugcheckCode ---
        var blue = new List<BlueScreenEvent>();
        foreach (var e in Read("System", Q("Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001"), 200))
        {
            var text = string.Join(" ", e.Props);
            var m = BugCheckRegex().Match(text);
            if (!m.Success) continue;
            var code = uint.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var dump = DumpPathRegex().Match(text) is { Success: true } dm ? Path.GetFileName(dm.Value.Trim()) : null;
            blue.Add(new BlueScreenEvent(e.Time, code, m.Groups[2].Value.Trim(), dump, "Windows-Fehlerberichterstattung"));
        }

        var powerLoss = 0;
        foreach (var e in Read("System", Q("Provider[@Name='Microsoft-Windows-Kernel-Power'] and EventID=41"), 500))
        {
            // Kernel-Power 41: Eigenschaft 0 = BugcheckCode (dezimal). 0 = Strom weg / Reset-Taste / Hänger ohne Bluescreen.
            var code = e.Props.Count > 0 && uint.TryParse(e.Props[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var c) ? c : 0;
            if (code == 0) { powerLoss++; continue; }
            if (blue.Any(b => Math.Abs((b.Time - e.Time).TotalMinutes) < 10 && b.Code == code)) continue; // schon erfasst
            var p = e.Props.Count >= 5 ? string.Join(", ", e.Props.Skip(1).Take(4).Select(Hex)) : "";
            blue.Add(new BlueScreenEvent(e.Time, code, p, null, "Kernel-Power 41"));
        }

        // --- Grafiktreiber-Resets (TDR): Display 4101 ---
        var tdr = Read("System", Q("Provider[@Name='Display'] and EventID=4101"), 500).Count;

        // --- Hardwarefehler (WHEA) ---
        var whea = Read("System", Q("Provider[@Name='Microsoft-Windows-WHEA-Logger']"), 1000)
            .GroupBy(e => e.Id)
            .Select(g => new HardwareErrorGroup(WheaKind(g.Key), g.Key, g.Count(), g.Max(x => x.Time), WheaExplanation(g.Key)))
            .OrderByDescending(g => g.Count)
            .ToList();

        // --- Programmabstürze und Hänger ---
        var apps = new List<(string App, string Module, string Code, DateTime Time, bool Hang)>();
        foreach (var e in Read("Application", Q("Provider[@Name='Application Error'] and EventID=1000"), 3000))
        {
            // Eigenschaften: 0 App, 1 Version, 2 Zeitstempel, 3 Modul, 4 Modulversion, 5 Zeitstempel, 6 Ausnahmecode
            if (e.Props.Count < 7) continue;
            apps.Add((e.Props[0], e.Props[3], "0x" + e.Props[6].Replace("0x", "", StringComparison.OrdinalIgnoreCase), e.Time, false));
        }
        foreach (var e in Read("Application", Q("Provider[@Name='Application Hang'] and EventID=1002"), 1000))
        {
            if (e.Props.Count < 1) continue;
            apps.Add((e.Props[0], "—", "Hänger", e.Time, true));
        }
        var appGroups = apps.GroupBy(a => (a.App.ToLowerInvariant(), a.Module.ToLowerInvariant(), a.Hang))
            .Select(g => new AppCrashGroup(g.First().App, g.First().Module, string.Join(", ", g.Select(x => x.Code).Distinct().Take(3)), g.Count(), g.Max(x => x.Time), g.Key.Hang))
            .OrderByDescending(g => g.Count)
            .Take(40)
            .ToList();

        blue = blue.OrderByDescending(b => b.Time).ToList();
        return new CrashReport(days, blue, powerLoss, tdr, whea, appGroups, BuildHints(blue, powerLoss, tdr, whea, appGroups));
    }

    /// <summary>Regelbasierte Hinweise. Jede Regel nennt ihre Grundlage; nichts wird als sicher dargestellt.</summary>
    internal static IReadOnlyList<CrashHint> BuildHints(IReadOnlyList<BlueScreenEvent> blue, int powerLoss, int tdr,
        IReadOnlyList<HardwareErrorGroup> whea, IReadOnlyList<AppCrashGroup> apps)
    {
        var hints = new List<CrashHint>();
        var codes = blue.Select(b => b.Code & 0xFFFF).ToList();

        if (blue.Count == 0 && powerLoss == 0 && whea.Count == 0)
            hints.Add(new(DataState.Ok, "Keine Systemabstürze", "Im Zeitraum wurden keine Bluescreens, unerwarteten Neustarts oder Hardwarefehler protokolliert."));

        if (whea.Any(w => w.EventId is 1 or 18 or 20 or 46) || codes.Contains(0x124) || codes.Contains(0x9C))
            hints.Add(new(DataState.Problem, "Hardwarefehler gemeldet (WHEA)",
                "Der Prozessor oder das Mainboard hat einen nicht korrigierbaren Fehler gemeldet. Häufige Ursachen: Übertaktung/XMP-Profil, " +
                "instabile Spannung, Überhitzung oder ein defektes Bauteil. Übertaktung testweise zurücknehmen, BIOS aktualisieren, Temperaturen prüfen."));
        else if (whea.Any(w => w.EventId is 17 or 19 or 47))
            hints.Add(new(DataState.Warning, "Korrigierte Hardwarefehler",
                "Windows hat Hardwarefehler automatisch korrigiert (z. B. PCIe- oder Speicherfehler). Einzelne Meldungen sind harmlos; " +
                "häufen sie sich, Chipsatz-/BIOS-Updates einspielen und ggf. PCIe-Steckplatz bzw. Kabel prüfen."));

        var gpuCodes = new uint[] { 0x116, 0x117, 0x119, 0x113, 0x10E };
        if (tdr >= 3 || codes.Any(c => gpuCodes.Contains(c)))
            hints.Add(new(DataState.Warning, "Grafiktreiber instabil",
                $"{tdr} Grafiktreiber-Reset(s){(codes.Any(c => gpuCodes.Contains(c)) ? " und grafikbezogene Bluescreens" : "")}. " +
                "Grafiktreiber sauber neu installieren (Herstellerseite), GPU-Übertaktung zurücknehmen, Temperatur und Netzteil prüfen."));

        var memoryCodes = new uint[] { 0x1A, 0x50, 0x4E, 0x19, 0xC2, 0x139, 0x13A };
        if (codes.Count(c => memoryCodes.Contains(c)) >= 2 || blue.Select(b => b.Code & 0xFFFF).Distinct().Count() >= 3)
            hints.Add(new(DataState.Warning, "Wechselnde Speicher-Bluescreens",
                "Mehrere verschiedene oder speicherbezogene Bluescreens deuten oft auf Arbeitsspeicherprobleme hin (auch durch XMP/EXPO). " +
                "Empfehlung: Windows-Speicherdiagnose ausführen (Seite „Windows Health“) und XMP/EXPO testweise deaktivieren."));

        var driverCodes = new uint[] { 0xD1, 0x0A, 0x7E, 0x3B, 0x1E, 0xC4, 0x9F, 0xD5, 0x10D };
        if (codes.Any(c => driverCodes.Contains(c)))
            hints.Add(new(DataState.Warning, "Treiberfehler wahrscheinlich",
                "Die BugCheck-Codes deuten auf einen fehlerhaften Treiber hin. Der genaue Treiber steht im Absturzabbild – " +
                "„Mit Debugger analysieren“ ermittelt ihn, wenn die Debugging Tools for Windows installiert sind."));

        if (codes.Contains(0x133) || codes.Contains(0x101))
            hints.Add(new(DataState.Warning, "Zeitüberschreitung (Watchdog)",
                "Ein Treiber oder Prozessorkern reagierte nicht rechtzeitig. Häufig: SSD-Firmware/Storage-Treiber, Chipsatztreiber oder Übertaktung. " +
                "SSD-Firmware und Chipsatztreiber aktualisieren."));

        if (codes.Contains(0x7A) || codes.Contains(0x7B) || codes.Contains(0x24) || codes.Contains(0x154))
            hints.Add(new(DataState.Warning, "Datenträger beteiligt",
                "Der Bluescreen hängt mit dem Lesen vom Datenträger zusammen. SMART-Werte prüfen (Seite Hardware) und CHKDSK ausführen."));

        if (powerLoss >= 2)
            hints.Add(new(DataState.Warning, "Unerwartete Abschaltungen ohne Bluescreen",
                $"{powerLoss}× wurde der PC ohne sauberes Herunterfahren neu gestartet (Stromausfall, Reset-Taste, Einfrieren). " +
                "Bei Spielen/Last: Netzteil und Temperaturen prüfen; bei Leerlauf: Energiesparzustände (BIOS/Chipsatz) aktualisieren."));

        foreach (var g in apps.Where(a => !a.IsHang && a.Count >= 3).Take(3))
        {
            var system = g.Module.Equals("ntdll.dll", StringComparison.OrdinalIgnoreCase) || g.Module.Equals("KERNELBASE.dll", StringComparison.OrdinalIgnoreCase);
            hints.Add(new(DataState.Warning, $"{g.Application} stürzt wiederholt ab ({g.Count}×)",
                system ? "Absturz in einer Windows-Systembibliothek – meist ein Fehler im Programm selbst oder ein Add-on. Programm aktualisieren oder neu installieren."
                       : $"Fehlerhaftes Modul: {g.Module}. Programm bzw. das zugehörige Plug-in/Treiber aktualisieren."));
        }
        return hints;
    }

    // ---------------------------------------------------------------- Dumps (Helper)

    public async Task<(IReadOnlyList<DumpFileInfo> Dumps, string? Error)> ReadDumpsAsync(CancellationToken ct = default)
    {
        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.ReadMinidumps, null, null, ct).ConfigureAwait(false);
        if (r.Cancelled) return ([], "Die Administratorfreigabe wurde abgelehnt.");
        var line = r.Output.Split('\n').FirstOrDefault(l => l.StartsWith("##RESULT##", StringComparison.Ordinal));
        if (line is null) return ([], "Keine Daten erhalten. " + r.Output.Trim());
        try
        {
            using var doc = JsonDocument.Parse(line["##RESULT##".Length..]);
            var list = new List<DumpFileInfo>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var p = new[] { "P1", "P2", "P3", "P4" }.Select(k => e.GetProperty(k).GetUInt64()).Select(v => "0x" + v.ToString("X", CultureInfo.InvariantCulture));
                list.Add(new DumpFileInfo(e.GetProperty("File").GetString() ?? "?", e.GetProperty("Time").GetDateTime(), e.GetProperty("Size").GetInt64(),
                    e.GetProperty("Code").GetUInt32(), string.Join(", ", p), e.GetProperty("Valid").GetBoolean()));
            }
            return (list, null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return ([], "Antwort des Helpers nicht lesbar: " + ex.Message);
        }
    }

    public async Task<DebuggerAnalysis> AnalyzeDumpAsync(string dumpFile, IProgress<string>? progress, CancellationToken ct = default)
    {
        var r = await ElevatedCommandRunner.RunAsync(AllowedCommand.AnalyzeMinidump, dumpFile, progress, ct).ConfigureAwait(false);
        if (r.Cancelled) return new DebuggerAnalysis(false, null, null, null, null, null, null, "Die Administratorfreigabe wurde abgelehnt.");
        var parsed = ParseDebuggerOutput(r.Output);
        AppLog.Info("CrashAnalysis", "Dump analysiert", $"{dumpFile}: {parsed.ProbablyCausedBy ?? parsed.Image ?? "–"}");
        return parsed;
    }

    /// <summary>Wertet die Ausgabe von „!analyze -v“ aus (sprachunabhängig, feste englische Feldnamen des Debuggers).</summary>
    internal static DebuggerAnalysis ParseDebuggerOutput(string output)
    {
        var noDebugger = output.Contains("\"debugger\":false", StringComparison.Ordinal);
        string? Field(string name)
        {
            var m = Regex.Match(output, "^" + Regex.Escape(name) + @":\s*(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            var v = m.Success ? m.Groups[1].Value.Trim() : null;
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        var caused = Regex.Match(output, @"Probably caused by\s*:\s*(.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var bugcheck = Regex.Match(output, @"^([A-Z_0-9]{6,}) \(([0-9a-f]+)\)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var raw = output.Length > 60_000 ? output[..60_000] + "\n…" : output;
        return new DebuggerAnalysis(!noDebugger,
            caused.Success ? caused.Groups[1].Value.Trim() : null,
            Field("MODULE_NAME"), Field("IMAGE_NAME"), Field("PROCESS_NAME"), Field("FAILURE_BUCKET_ID"),
            bugcheck.Success ? $"{bugcheck.Groups[1].Value} (0x{bugcheck.Groups[2].Value})" : null,
            raw);
    }

    public static bool DebuggerInstalled =>
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10", "Debuggers", "x64", "kd.exe"))
        || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Kits", "10", "Debuggers", "x64", "kd.exe"));

    // ---------------------------------------------------------------- Hilfen

    private sealed record Ev(DateTime Time, long Id, IReadOnlyList<string> Props);

    private static List<Ev> Read(string log, string xpath, int max)
    {
        var list = new List<Ev>();
        try
        {
            var query = new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true };
            using var reader = new EventLogReader(query);
            for (var i = 0; i < max; i++)
            {
                using var rec = reader.ReadEvent();
                if (rec is null) break;
                var props = rec.Properties.Select(p => Convert.ToString(p.Value, CultureInfo.InvariantCulture) ?? "").ToList();
                list.Add(new Ev(rec.TimeCreated ?? DateTime.MinValue, rec.Id, props));
            }
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException)
        {
            AppLog.Warn("CrashAnalysis", $"Protokoll '{log}' nicht lesbar", ex.Message);
        }
        return list;
    }

    private static string Hex(string s) => ulong.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
        ? "0x" + v.ToString("X", CultureInfo.InvariantCulture) : s;

    private static string WheaKind(long id) => id switch
    {
        1 or 18 or 20 or 46 => "Schwerer Hardwarefehler",
        17 => "PCIe-Fehler (korrigiert)",
        19 => "Prozessorfehler (korrigiert)",
        47 => "Speicherfehler (korrigiert)",
        _ => "Hardwareereignis",
    };

    private static string WheaExplanation(long id) => id switch
    {
        1 or 18 or 20 or 46 => "Nicht korrigierbarer Fehler (Machine Check) – führt meist zu Bluescreen 0x124.",
        17 => "Fehler auf dem PCIe-Bus, von der Hardware korrigiert (oft Grafikkarte/NVMe, Energiesparmodus ASPM).",
        19 => "Interner Prozessorfehler, korrigiert. Häufen sich diese, an Übertaktung/Spannung denken.",
        47 => "Arbeitsspeicherfehler, per ECC korrigiert.",
        _ => "Ereignis der Windows Hardware Error Architecture.",
    };

    [GeneratedRegex(@"0x([0-9a-fA-F]{8})\s*\(([^)]*)\)", RegexOptions.CultureInvariant)]
    private static partial Regex BugCheckRegex();

    [GeneratedRegex(@"[A-Za-z]:\\[^\s]+\.(?:dmp|DMP)", RegexOptions.CultureInvariant)]
    private static partial Regex DumpPathRegex();
}

/// <summary>Bekannte BugCheck-Codes mit verständlicher Erklärung (Quelle: Microsoft „Bug Check Code Reference“).</summary>
public static class BugCheckCatalog
{
    private static readonly Dictionary<uint, (string Name, string Meaning, string Advice)> Codes = new()
    {
        [0x0A] = ("IRQL_NOT_LESS_OR_EQUAL", "Ein Treiber hat auf Speicher mit falscher Priorität zugegriffen.", "Kürzlich installierte/aktualisierte Treiber prüfen; RAM testen."),
        [0x19] = ("BAD_POOL_HEADER", "Die Kernel-Speicherverwaltung ist beschädigt.", "Treiber aktualisieren, RAM testen."),
        [0x1A] = ("MEMORY_MANAGEMENT", "Schwerer Fehler der Speicherverwaltung.", "Windows-Speicherdiagnose ausführen, XMP/EXPO testweise deaktivieren."),
        [0x1E] = ("KMODE_EXCEPTION_NOT_HANDLED", "Ein Treiber hat eine unbehandelte Ausnahme ausgelöst.", "Treiber ermitteln (Debugger) und aktualisieren."),
        [0x24] = ("NTFS_FILE_SYSTEM", "Fehler im NTFS-Dateisystem.", "CHKDSK ausführen, SMART-Werte prüfen."),
        [0x3B] = ("SYSTEM_SERVICE_EXCEPTION", "Ausnahme bei einem Systemaufruf – oft Grafik- oder Antivirentreiber.", "Grafik- und Sicherheitssoftware aktualisieren."),
        [0x3D] = ("INTERRUPT_EXCEPTION_NOT_HANDLED", "Ausnahme während einer Interrupt-Verarbeitung.", "Treiber aktualisieren."),
        [0x4E] = ("PFN_LIST_CORRUPT", "Die Seitenverwaltung des Speichers ist beschädigt.", "RAM testen, Treiber aktualisieren."),
        [0x50] = ("PAGE_FAULT_IN_NONPAGED_AREA", "Zugriff auf ungültigen Speicher.", "RAM testen; zuletzt installierte Treiber/Antivirus prüfen."),
        [0x7A] = ("KERNEL_DATA_INPAGE_ERROR", "Daten konnten nicht vom Datenträger geladen werden.", "SMART prüfen, Kabel/SSD-Firmware prüfen, CHKDSK."),
        [0x7B] = ("INACCESSIBLE_BOOT_DEVICE", "Das Startlaufwerk ist nicht erreichbar.", "Speichercontroller-Modus (AHCI/RAID) im BIOS prüfen."),
        [0x7E] = ("SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Ein Systemthread hat eine unbehandelte Ausnahme ausgelöst.", "Treiber ermitteln (Debugger) und aktualisieren."),
        [0x7F] = ("UNEXPECTED_KERNEL_MODE_TRAP", "Prozessor-Ausnahme im Kernel – oft Hardware oder Übertaktung.", "Übertaktung zurücknehmen, RAM testen."),
        [0x9C] = ("MACHINE_CHECK_EXCEPTION", "Der Prozessor meldet einen schweren Hardwarefehler.", "Übertaktung/Spannungen und Kühlung prüfen."),
        [0x9F] = ("DRIVER_POWER_STATE_FAILURE", "Ein Treiber hat beim Wechsel des Energiezustands nicht reagiert.", "Treiber (oft WLAN/Grafik/USB) aktualisieren, Schnellstart testweise deaktivieren."),
        [0xA0] = ("INTERNAL_POWER_ERROR", "Fehler in der Energieverwaltung.", "BIOS und Chipsatztreiber aktualisieren."),
        [0xBE] = ("ATTEMPTED_WRITE_TO_READONLY_MEMORY", "Ein Treiber hat in schreibgeschützten Speicher geschrieben.", "Treiber ermitteln und aktualisieren."),
        [0xC2] = ("BAD_POOL_CALLER", "Ein Treiber hat Speicher fehlerhaft angefordert/freigegeben.", "Treiber ermitteln und aktualisieren."),
        [0xC4] = ("DRIVER_VERIFIER_DETECTED_VIOLATION", "Driver Verifier hat einen Treiberfehler gefunden.", "Den genannten Treiber aktualisieren; Driver Verifier ggf. deaktivieren."),
        [0xC5] = ("DRIVER_CORRUPTED_EXPOOL", "Ein Treiber hat Kernel-Speicher beschädigt.", "Treiber ermitteln und aktualisieren."),
        [0xD1] = ("DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Ein Treiber hat auf ungültigen Speicher zugegriffen – sehr häufig Netzwerk-/WLAN-Treiber.", "Netzwerk-, WLAN- und Grafiktreiber aktualisieren."),
        [0xD5] = ("DRIVER_PAGE_FAULT_IN_FREED_SPECIAL_POOL", "Ein Treiber griff auf bereits freigegebenen Speicher zu.", "Treiber ermitteln und aktualisieren."),
        [0xEF] = ("CRITICAL_PROCESS_DIED", "Ein für Windows unverzichtbarer Prozess wurde beendet.", "SFC und DISM ausführen (Seite Windows Health)."),
        [0xF4] = ("CRITICAL_OBJECT_TERMINATION", "Ein kritischer Prozess wurde beendet – oft Datenträgerprobleme.", "SMART prüfen, SFC/DISM ausführen."),
        [0xFC] = ("ATTEMPTED_EXECUTE_OF_NOEXECUTE_MEMORY", "Ein Treiber wollte nicht ausführbaren Speicher ausführen.", "Treiber ermitteln und aktualisieren."),
        [0x101] = ("CLOCK_WATCHDOG_TIMEOUT", "Ein Prozessorkern hat nicht rechtzeitig reagiert.", "Übertaktung zurücknehmen, BIOS aktualisieren."),
        [0x10D] = ("WDF_VIOLATION", "Ein Treiber auf Basis des Windows Driver Framework hat einen Fehler gemacht.", "Zuletzt angeschlossene USB-Geräte/Treiber prüfen."),
        [0x10E] = ("VIDEO_MEMORY_MANAGEMENT_INTERNAL", "Fehler in der Grafikspeicherverwaltung.", "Grafiktreiber neu installieren."),
        [0x113] = ("VIDEO_DXGKRNL_FATAL_ERROR", "Schwerer Fehler im Windows-Grafikkernel.", "Grafiktreiber neu installieren."),
        [0x116] = ("VIDEO_TDR_FAILURE", "Der Grafiktreiber konnte nach einem Hänger nicht neu gestartet werden.", "Grafiktreiber neu installieren, GPU-Übertaktung zurücknehmen, Temperatur prüfen."),
        [0x117] = ("VIDEO_TDR_TIMEOUT_DETECTED", "Die Grafikkarte hat nicht rechtzeitig reagiert.", "Grafiktreiber aktualisieren, Temperatur/Netzteil prüfen."),
        [0x119] = ("VIDEO_SCHEDULER_INTERNAL_ERROR", "Fehler im Grafik-Scheduler.", "Grafiktreiber neu installieren."),
        [0x124] = ("WHEA_UNCORRECTABLE_ERROR", "Nicht korrigierbarer Hardwarefehler (CPU, RAM, Mainboard).", "Übertaktung/XMP zurücknehmen, Temperaturen prüfen, BIOS aktualisieren."),
        [0x133] = ("DPC_WATCHDOG_VIOLATION", "Ein Treiber hat zu lange blockiert – oft Storage- oder SSD-Firmware.", "SSD-Firmware und Chipsatz-/Storage-Treiber aktualisieren."),
        [0x139] = ("KERNEL_SECURITY_CHECK_FAILURE", "Eine Datenstruktur im Kernel ist beschädigt.", "Treiber aktualisieren, RAM testen, SFC ausführen."),
        [0x13A] = ("KERNEL_MODE_HEAP_CORRUPTION", "Der Kernel-Heap ist beschädigt – meist ein Treiber.", "Grafik- und Audiotreiber aktualisieren."),
        [0x14F] = ("PDC_WATCHDOG_TIMEOUT", "Zeitüberschreitung beim Energiezustandswechsel.", "BIOS/Chipsatz aktualisieren, Modern Standby prüfen."),
        [0x154] = ("UNEXPECTED_STORE_EXCEPTION", "Fehler im Speicherkomprimierungsdienst – oft Datenträger.", "SMART prüfen, SSD-Firmware aktualisieren."),
        [0x15F] = ("CONNECTED_STANDBY_WATCHDOG_TIMEOUT_LIVEDUMP", "Hänger im Modern-Standby.", "BIOS/Chipsatztreiber aktualisieren."),
        [0x1A8] = ("BUGCODE_WIFIADAPTER_DRIVER", "Fehler im WLAN-Treiber.", "WLAN-Treiber aktualisieren."),
        [0xE2] = ("MANUALLY_INITIATED_CRASH", "Der Absturz wurde absichtlich ausgelöst (Tastenkombination/Debugger).", "Kein Fehler, sofern beabsichtigt."),
        [0xC000021A] = ("STATUS_SYSTEM_PROCESS_TERMINATED", "Ein Kernprozess (winlogon/csrss) ist abgestürzt.", "SFC/DISM ausführen, zuletzt installierte Updates prüfen."),
    };

    public static BugCheckInfo Lookup(uint code)
    {
        // 0x1000007E usw. sind Varianten (gleiche Bedeutung, erweiterte Parameter).
        var key = Codes.ContainsKey(code) ? code : (code & 0x10000000) != 0 ? code & 0x0FFFFFFF : code;
        return Codes.TryGetValue(key, out var c)
            ? new BugCheckInfo(code, c.Name, c.Meaning, c.Advice)
            : new BugCheckInfo(code, $"0x{code:X}", "Seltener Fehlercode – Details liefert die Debugger-Analyse.", "Mit Debugger analysieren; Treiber und Windows aktualisieren.");
    }
}
