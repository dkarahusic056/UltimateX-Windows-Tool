using System.Diagnostics;
using System.Security.Cryptography;
using UltimateX.Core.Common;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;

namespace UltimateX.Services.Diagnostics;

public enum DiskKind { Nvme, Ssd, Hdd, Other }

/// <summary>Physischer Datenträger mit zugeordneten Laufwerksbuchstaben (MSFT_PhysicalDisk + MSFT_Partition).</summary>
public sealed record DiskEntry(int Number, string Name, DiskKind Kind, string Bus, ulong? Size, string Health, DataState HealthState, IReadOnlyList<char> Letters);

public sealed partial class ComponentTestService
{
    /// <summary>Datenträger für den optionalen Schreibtest (UI-Auswahl).</summary>
    public IReadOnlyList<DiskEntry> GetDisks() => ReadDisks();

    private static IReadOnlyList<DiskEntry> ReadDisks()
    {
        var disks = Wmi.Query("SELECT DeviceId, FriendlyName, MediaType, BusType, Size, HealthStatus FROM MSFT_PhysicalDisk", Wmi.Storage);
        var parts = Wmi.Query("SELECT DiskNumber, DriveLetter FROM MSFT_Partition", Wmi.Storage);
        var letters = parts.Rows
            .Where(p => p.U64("DiskNumber") is not null && p["DriveLetter"] is char or ushort or string)
            .Select(p => (Disk: (int)p.U64("DiskNumber")!.Value, Letter: ToLetter(p["DriveLetter"])))
            .Where(x => x.Letter is >= 'A' and <= 'Z')
            .GroupBy(x => x.Disk).ToDictionary(g => g.Key, g => (IReadOnlyList<char>)g.Select(x => x.Letter).Distinct().OrderBy(c => c).ToList());

        var list = new List<DiskEntry>();
        foreach (var d in disks.Rows)
        {
            if (!int.TryParse(d.Str("DeviceId"), out var number)) continue;
            var bus = d.U64("BusType");
            var media = d.U64("MediaType");
            var kind = bus == 17 ? DiskKind.Nvme : media == 4 ? DiskKind.Ssd : media == 3 ? DiskKind.Hdd : DiskKind.Other;
            var (health, state) = d.U64("HealthStatus") switch
            {
                0 => ("Fehlerfrei", DataState.Ok),
                1 => ("Warnung", DataState.Warning),
                2 => ("Fehlerhaft", DataState.Problem),
                _ => ("Unbekannt", DataState.NotAvailable),
            };
            var busText = bus switch { 17 => "NVMe", 11 => "SATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 3 => "ATA", 12 => "SD", 13 => "MMC", _ => "Sonstige" };
            list.Add(new DiskEntry(number, d.Str("FriendlyName") ?? $"Datenträger {number}", kind, busText, d.U64("Size"), health, state,
                letters.TryGetValue(number, out var l) ? l : []));
        }
        return list.OrderBy(x => x.Number).ToList();
    }

    private static char ToLetter(object? v) => v switch
    {
        char c => char.ToUpperInvariant(c),
        ushort u => char.ToUpperInvariant((char)u),
        string { Length: > 0 } s => char.ToUpperInvariant(s[0]),
        _ => '\0',
    };

    private ComponentTestResult TestStorage(ComponentTestDefinition d, DiskKind kind, CancellationToken ct)
    {
        var all = Context.Disks.Value;
        var disks = all.Where(x => x.Kind == kind).ToList();
        if (disks.Count == 0)
        {
            var what = kind switch { DiskKind.Nvme => "NVMe-SSD", DiskKind.Ssd => "SATA-SSD", _ => "Festplatte (HDD)" };
            return R(d, TestStatus.NotAvailable, $"Keine {what} verbaut.");
        }

        var details = new List<string>();
        var statuses = new List<TestStatus>();
        var smart = SmartService.LoadCached();
        var events = Context.DiskEvents.Value;
        var systemLetter = char.ToUpperInvariant((Path.GetPathRoot(Environment.SystemDirectory) ?? "C")[0]);

        foreach (var disk in disks)
        {
            ct.ThrowIfCancellationRequested();
            details.Add($"{disk.Name} ({disk.Bus}, {Format.Bytes(disk.Size)}) · Laufwerke: {(disk.Letters.Count == 0 ? "–" : string.Join(", ", disk.Letters.Select(c => c + ":")))}");
            details.Add($"  Windows-Gesundheitsstatus: {disk.Health}");
            statuses.Add(disk.HealthState switch { DataState.Ok => TestStatus.Pass, DataState.Warning => TestStatus.Warning, DataState.Problem => TestStatus.Fail, _ => TestStatus.NotAvailable });

            var sm = smart?.Disks.FirstOrDefault(s => s.Name.Equals(disk.Name, StringComparison.OrdinalIgnoreCase));
            if (sm is null) details.Add("  SMART: noch nicht gelesen („SMART lesen“ auf der Seite Hardware, Adminrechte)");
            else
            {
                details.Add($"  SMART: Temperatur {(sm.TemperatureC is { } t ? t + " °C" : "–")}, Betriebsstunden {sm.PowerOnHours?.ToString() ?? "–"}, " +
                            $"Restlebensdauer {(sm.RemainingLifePercent is { } rl ? rl + " %" : "–")}, nicht korrigierbare Lesefehler {sm.ReadErrorsUncorrected?.ToString() ?? "–"}");
                if (sm.ReadErrorsUncorrected is > 0 || sm.WriteErrorsUncorrected is > 0) statuses.Add(TestStatus.Fail);
                if (sm.RemainingLifePercent is < 10) statuses.Add(TestStatus.Fail);
                else if (sm.RemainingLifePercent is < 25) statuses.Add(TestStatus.Warning);
                if (sm.TemperatureC is >= 70) statuses.Add(TestStatus.Warning);
            }

            if (events is not null)
            {
                var mine = events.Where(e => e.Message?.Contains($"Harddisk{disk.Number}\\", StringComparison.OrdinalIgnoreCase) == true).ToList();
                if (mine.Count > 0)
                {
                    details.Add($"  Fehlerereignisse (30 Tage): {mine.Count} (z. B. Ereignis {mine[0].Id}, {mine[0].Time:dd.MM. HH:mm})");
                    statuses.Add(mine.Any(e => e.Id is 7 or 55) ? TestStatus.Fail : TestStatus.Warning);
                }
            }

            foreach (var letter in disk.Letters)
            {
                try
                {
                    var di = new DriveInfo(letter + ":\\");
                    if (!di.IsReady) { details.Add($"  {letter}: nicht bereit"); continue; }
                    var freePct = 100.0 * di.AvailableFreeSpace / Math.Max(1, di.TotalSize);
                    details.Add($"  {letter}: {di.DriveFormat}, {Format.Bytes(di.AvailableFreeSpace)} frei ({freePct:0} %)");
                    if (freePct < 5) statuses.Add(TestStatus.Warning);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    details.Add($"  {letter}: Dateisystem nicht lesbar ({ex.Message})");
                    statuses.Add(TestStatus.Warning);
                }
            }

            if (disk.Letters.Contains(systemLetter))
            {
                var (ok, mb, mbps, error) = ReadTest(ct);
                details.Add(ok ? $"  Lesetest: {mb:0} MB fehlerfrei gelesen, {mbps:0} MB/s (Systemdateien, Cache kann den Wert erhöhen)"
                               : $"  Lesetest: FEHLER – {error}");
                statuses.Add(ok ? TestStatus.Pass : TestStatus.Fail);
            }
        }
        details.Add("Optionaler Schreibtest nur nach Bestätigung (eigene Temp-Datei, wird danach gelöscht).");

        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Datenträgerproblem erkannt – Daten sichern und Laufwerk prüfen!",
            TestStatus.Warning => "Datenträger funktioniert, Hinweise beachten.",
            TestStatus.Pass => $"{disks.Count} Datenträger fehlerfrei.",
            _ => "Status nicht ermittelbar.",
        }, details);
    }

    /// <summary>Liest bis zu 256 MB aus vorhandenen System32-Dateien (nur lesend) und misst den Durchsatz.</summary>
    private static (bool Ok, double Mb, double MbPerSec, string? Error) ReadTest(CancellationToken ct)
    {
        const long budget = 256L * 1024 * 1024;
        var buffer = new byte[1024 * 1024];
        long total = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            var files = new DirectoryInfo(Environment.SystemDirectory).EnumerateFiles("*.dll")
                .Where(f => (f.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) == 0)
                .OrderByDescending(f => f.Length).Take(200);
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                    int n;
                    while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += n;
                        if (total >= budget) break;
                    }
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException ex) when (ex is not FileNotFoundException)
                {
                    // Ein echter Lesefehler (z. B. CRC) ist relevant; gesperrte Dateien werden übersprungen.
                    if (ex.HResult is unchecked((int)0x80070017) or unchecked((int)0x80070483)) return (false, total / 1048576.0, 0, ex.Message);
                }
                if (total >= budget) break;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return (false, total / 1048576.0, 0, ex.Message);
        }
        var sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
        return total == 0 ? (false, 0, 0, "Keine Datei lesbar") : (true, total / 1048576.0, total / 1048576.0 / sec, null);
    }

    /// <summary>
    /// Optionaler Schreibtest – NUR nach ausdrücklicher Bestätigung in der Oberfläche.
    /// Schreibt 128 MB Zufallsdaten in eine neue, eindeutig benannte Temp-Datei (Systemlaufwerk: %TEMP%,
    /// sonst Laufwerkswurzel), liest sie zurück, vergleicht SHA-256 und löscht die Datei. Keine bestehende Datei wird berührt.
    /// </summary>
    public static async Task<ComponentTestResult> RunWriteTestAsync(string testId, char driveLetter, CancellationToken ct)
    {
        var def = Definition(testId);
        driveLetter = char.ToUpperInvariant(driveLetter);
        if (driveLetter is < 'A' or > 'Z') return R(def, TestStatus.NotTested, "Ungültiges Laufwerk.");
        var systemLetter = char.ToUpperInvariant((Path.GetPathRoot(Environment.SystemDirectory) ?? "C")[0]);
        var dir = driveLetter == systemLetter ? Path.GetTempPath() : $"{driveLetter}:\\";
        var root = new DriveInfo(driveLetter + ":\\");
        if (!root.IsReady) return R(def, TestStatus.NotAvailable, $"Laufwerk {driveLetter}: ist nicht bereit.");
        const int sizeMb = 128;
        if (root.AvailableFreeSpace < (sizeMb + 512) * 1024L * 1024) return R(def, TestStatus.NotTested, $"Zu wenig freier Speicher auf {driveLetter}: für den Schreibtest.");

        var path = Path.Combine(dir, $"UltimateX-WriteTest-{Guid.NewGuid():N}.tmp");
        var details = new List<string> { $"Testdatei: {path}" };
        var block = new byte[4 * 1024 * 1024];
        try
        {
            using var hashW = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var sw = Stopwatch.StartNew();
            await using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.WriteThrough | FileOptions.Asynchronous))
            {
                for (var i = 0; i < sizeMb / 4; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    RandomNumberGenerator.Fill(block);
                    hashW.AppendData(block);
                    await fs.WriteAsync(block, ct).ConfigureAwait(false);
                }
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            var writeSec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
            var expected = hashW.GetHashAndReset();

            using var hashR = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            sw.Restart();
            await using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.SequentialScan | FileOptions.Asynchronous))
            {
                int n;
                while ((n = await fs.ReadAsync(block, ct).ConfigureAwait(false)) > 0) hashR.AppendData(block, 0, n);
            }
            var readSec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
            var ok = CryptographicOperations.FixedTimeEquals(expected, hashR.GetHashAndReset());
            details.Add($"Schreiben: {sizeMb / writeSec:0} MB/s (Write-Through) · Zurücklesen: {sizeMb / readSec:0} MB/s");
            details.Add(ok ? "Prüfsumme (SHA-256) identisch – Daten korrekt geschrieben." : "Prüfsumme ABWEICHEND – Daten wurden fehlerhaft gespeichert!");
            return R(def, ok ? TestStatus.Pass : TestStatus.Fail, ok ? $"Schreibtest auf {driveLetter}: erfolgreich." : $"Schreibtest auf {driveLetter}: Datenfehler!", details);
        }
        catch (OperationCanceledException)
        {
            return R(def, TestStatus.NotTested, "Schreibtest abgebrochen.", details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            details.Add(ex.Message);
            return R(def, TestStatus.Warning, $"Schreibtest auf {driveLetter}: nicht möglich (keine Schreibrechte oder E/A-Fehler).", details);
        }
        finally
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private ComponentTestResult TestStorageController(ComponentTestDefinition d)
    {
        var ctrls = PnpOf("SCSIAdapter", "HDC");
        if (ctrls.Count == 0) return R(d, TestStatus.NotAvailable, "Keine Speichercontroller gefunden.");
        var details = ctrls.Select(c => $"{c.Name}: {c.ErrorText}").ToList();
        var statuses = ctrls.Select(c => c.ErrorCode is null or 0 ? TestStatus.Pass : c.ErrorCode == 22 ? TestStatus.Warning : TestStatus.Fail).ToList();
        var events = Context.DiskEvents.Value;
        if (events is null) details.Add("Datenträger-Ereignisse: Protokoll nicht lesbar");
        else
        {
            var byId = events.GroupBy(e => e.Id).Select(g => $"Ereignis {g.Key}: {g.Count()}×").ToList();
            details.Add("Datenträger-/Dateisystem-Ereignisse (30 Tage): " + (byId.Count == 0 ? "keine" : string.Join(", ", byId)));
            if (events.Any(e => e.Id is 11 or 7 or 55)) statuses.Add(TestStatus.Warning);
            else if (events.Count > 0) statuses.Add(TestStatus.Warning);
        }
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Speichercontroller meldet einen Fehler im Geräte-Manager.",
            TestStatus.Warning => "Controller arbeiten, aber es gibt Datenträger-Fehlerereignisse.",
            _ => $"{ctrls.Count} Speichercontroller ohne Fehler.",
        }, details);
    }
}
