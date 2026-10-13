using System.Diagnostics;
using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Sensors;

namespace UltimateX.Services.Diagnostics;

public sealed partial class ComponentTestService
{
    // ------------------------------------------------------------------ CPU

    private ComponentTestResult TestCpu(ComponentTestDefinition d, CancellationToken ct)
    {
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        var cpu = hardware.GetCpu();
        if (cpu is null) return R(d, TestStatus.NotAvailable, "Prozessor konnte nicht abgefragt werden (WMI).");
        details.Add($"{cpu.Name} · {cpu.Cores} Kerne / {cpu.Threads} Threads · max. {cpu.MaxClockMHz} MHz");

        var proc = Wmi.Query("SELECT Status, LoadPercentage, CurrentClockSpeed FROM Win32_Processor").Rows.FirstOrDefault();
        if (proc?.Str("Status") is { } st)
        {
            details.Add($"Gerätestatus: {st}");
            if (!st.Equals("OK", StringComparison.OrdinalIgnoreCase)) statuses.Add(TestStatus.Warning);
        }

        // Kurzer Rechentest: identische Aufgaben auf allen Threads müssen identische Ergebnisse liefern.
        var (intOk, intMs) = CpuIntegerTest(ct);
        var (fltOk, fltMs) = CpuFloatTest(ct);
        var (mcOk, mcMs, threads) = CpuMulticoreTest(ct);
        details.Add($"Integer-Test: {(intOk ? "korrekt" : "FEHLER")} ({intMs:0} ms)");
        details.Add($"Gleitkomma-Test: {(fltOk ? "korrekt" : "FEHLER")} ({fltMs:0} ms)");
        details.Add($"Mehrkern-Test: {threads} Threads {(mcOk ? "identische Ergebnisse" : "ABWEICHENDE Ergebnisse")} ({mcMs:0} ms)");
        statuses.Add(intOk && fltOk && mcOk ? TestStatus.Pass : TestStatus.Fail);

        // WHEA (Windows Hardware Error Architecture) der letzten 30 Tage
        var whea = EventProbe.Query("System", "Provider[@Name='Microsoft-Windows-WHEA-Logger']", 30, 200);
        if (whea is null) details.Add("WHEA-Ereignisse: Protokoll nicht lesbar");
        else
        {
            var fatal = whea.Count(e => e.Level is 1 or 2);
            var corrected = whea.Count(e => e.Level == 3);
            details.Add($"WHEA-Hardwarefehler (30 Tage): {fatal} Fehler, {corrected} korrigierte Fehler");
            if (fatal > 0) statuses.Add(TestStatus.Fail);
            else if (corrected > 0) statuses.Add(TestStatus.Warning);
        }

        var temp = sensors.Best(UnifiedSensorId.CpuPackageTemp) ?? sensors.Best(UnifiedSensorId.CpuCoreTemp);
        if (temp is null) details.Add("Temperatur: keine CPU-Sensorquelle (HWiNFO oder LibreHardwareMonitor starten)");
        else
        {
            details.Add($"Temperatur: {temp.Current:0} °C (max. seit Start {temp.Max:0} °C, Quelle {temp.SourceName})");
            if (temp.Current >= 95) statuses.Add(TestStatus.Warning);
        }

        var status = Worst(statuses);
        var summary = status switch
        {
            TestStatus.Fail when !(intOk && fltOk && mcOk) => "Rechenfehler erkannt – Hinweis auf Instabilität (Übertaktung, Spannung, Kühlung).",
            TestStatus.Fail => "Hardwarefehler (WHEA) im Ereignisprotokoll.",
            TestStatus.Warning => "Rechentest korrekt, aber Hinweise vorhanden (siehe Details).",
            _ => "Rechentests korrekt, keine Hardwarefehler protokolliert.",
        };
        return R(d, status, summary, details);
    }

    private static (bool Ok, double Ms) CpuIntegerTest(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var a = IntegerKernel(0x9E3779B97F4A7C15UL, 4_000_000);
        ct.ThrowIfCancellationRequested();
        var b = IntegerKernel(0x9E3779B97F4A7C15UL, 4_000_000);
        return (a == b && a != 0, sw.Elapsed.TotalMilliseconds);
    }

    internal static ulong IntegerKernel(ulong seed, int rounds)
    {
        ulong x = seed, acc = 0;
        for (var i = 0; i < rounds; i++)
        {
            x ^= x << 13; x ^= x >> 7; x ^= x << 17;
            acc = unchecked(acc * 31 + (x % 1_000_003) + (ulong)(i & 0xFF));
        }
        return acc;
    }

    private static (bool Ok, double Ms) CpuFloatTest(CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var a = FloatKernel(2_000_000);
        ct.ThrowIfCancellationRequested();
        var b = FloatKernel(2_000_000);
        return (a.Equals(b) && double.IsFinite(a), sw.Elapsed.TotalMilliseconds);
    }

    internal static double FloatKernel(int rounds)
    {
        double acc = 0;
        for (var i = 1; i <= rounds; i++)
            acc += Math.Sqrt(i) * Math.Sin(i * 0.001) / (1.0 + Math.Log(i));
        return acc;
    }

    private static (bool Ok, double Ms, int Threads) CpuMulticoreTest(CancellationToken ct)
    {
        var threads = Math.Clamp(Environment.ProcessorCount, 1, 128);
        var results = new ulong[threads];
        var sw = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = threads },
            i => results[i] = IntegerKernel(0xC2B2AE3D27D4EB4FUL, 3_000_000));
        return (results.All(r => r == results[0]) && results[0] != 0, sw.Elapsed.TotalMilliseconds, threads);
    }

    // ------------------------------------------------------------------ RAM

    private ComponentTestResult TestRam(ComponentTestDefinition d, CancellationToken ct)
    {
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        var mem = hardware.GetMemory();
        if (mem.TotalBytes is null) return R(d, TestStatus.NotAvailable, "Arbeitsspeicher konnte nicht abgefragt werden.");
        details.Add($"Installiert: {Format.Bytes(mem.TotalBytes)} · frei {Format.Bytes(mem.AvailableBytes)}");
        details.Add($"Module: {mem.Modules.Count}" + (mem.SlotCount is { } s ? $" von {s} Steckplätzen" : ""));
        foreach (var m in mem.Modules.Take(8))
            details.Add($"  {m.Slot}: {Format.Bytes(m.CapacityBytes)}" + (m.SpeedMTs is { } mts ? $" · {mts} MT/s" : "") + (m.Manufacturer is { } mf ? $" · {mf}" : ""));
        var speeds = mem.Modules.Select(m => m.SpeedMTs).Where(x => x is not null).Distinct().Count();
        if (speeds > 1)
        {
            details.Add("Hinweis: Module laufen mit unterschiedlicher Geschwindigkeit.");
            statuses.Add(TestStatus.Warning);
        }

        // Kurzer Mustertest in einem eigenen Speicherblock (nur dieser Block wird geprüft, nicht der gesamte RAM).
        var free = mem.AvailableBytes ?? 0;
        var size = free > 2UL * 1024 * 1024 * 1024 ? 256 : free > 1UL * 1024 * 1024 * 1024 ? 128 : 0;
        if (size == 0)
        {
            details.Add("Mustertest übersprungen: weniger als 1 GB frei.");
        }
        else
        {
            var (ok, ms) = MemoryPatternTest(size, ct);
            details.Add($"Mustertest {size} MB (0x55/0xAA/Adressmuster): {(ok ? "keine Fehler" : "FEHLER")} ({ms:0} ms)");
            statuses.Add(ok ? TestStatus.Pass : TestStatus.Fail);
        }

        var diag = EventProbe.Query("System", "Provider[@Name='Microsoft-Windows-MemoryDiagnostics-Results']", 365, 5);
        var last = diag?.FirstOrDefault();
        if (last is null) details.Add("Windows-Speicherdiagnose: kein Ergebnis im letzten Jahr – für einen vollständigen Test empfohlen.");
        else
        {
            var bad = last.Id == 1202;
            details.Add($"Windows-Speicherdiagnose vom {last.Time:dd.MM.yyyy}: {(bad ? "FEHLER gefunden" : "keine Fehler")}");
            statuses.Add(bad ? TestStatus.Fail : TestStatus.Pass);
        }

        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Speicherfehler festgestellt – Windows-Speicherdiagnose ausführen und Module prüfen.",
            TestStatus.Warning => "Speicher funktioniert, Hinweise beachten.",
            TestStatus.Pass => "Kein Speicherfehler im Kurztest.",
            _ => "Keine Prüfung möglich.",
        }, details);
    }

    internal static (bool Ok, double Ms) MemoryPatternTest(int megabytes, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var count = megabytes * 1024 * 1024 / sizeof(ulong);
        ulong[] buffer;
        try { buffer = GC.AllocateUninitializedArray<ulong>(count); }
        catch (OutOfMemoryException) { return (true, 0); }
        try
        {
            foreach (var pattern in new ulong[] { 0x5555555555555555UL, 0xAAAAAAAAAAAAAAAAUL })
            {
                ct.ThrowIfCancellationRequested();
                buffer.AsSpan().Fill(pattern);
                foreach (var v in buffer) if (v != pattern) return (false, sw.Elapsed.TotalMilliseconds);
            }
            ct.ThrowIfCancellationRequested();
            for (var i = 0; i < buffer.Length; i++) buffer[i] = (ulong)i * 0x9E3779B97F4A7C15UL;
            for (var i = 0; i < buffer.Length; i++) if (buffer[i] != (ulong)i * 0x9E3779B97F4A7C15UL) return (false, sw.Elapsed.TotalMilliseconds);
            return (true, sw.Elapsed.TotalMilliseconds);
        }
        finally
        {
            buffer = [];
            GC.Collect(2, GCCollectionMode.Optimized, false);
        }
    }

    // ------------------------------------------------------------------ GPU / VRAM

    private ComponentTestResult TestGpu(ComponentTestDefinition d)
    {
        var rows = Wmi.Query("SELECT Name, Status, ConfigManagerErrorCode, DriverVersion, VideoProcessor FROM Win32_VideoController");
        if (!rows.Ok || rows.Rows.Count == 0) return R(d, TestStatus.NotAvailable, "Keine Grafikkarte über WMI gefunden.");
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        foreach (var g in rows.Rows)
        {
            var name = g.Str("Name") ?? "GPU";
            var code = (int?)g.U64("ConfigManagerErrorCode");
            details.Add($"{name}: Status {g.Str("Status") ?? "?"}" + (code is > 0 ? $", Geräte-Manager-Code {code}" : ""));
            if (code is > 0) statuses.Add(code == 22 ? TestStatus.Warning : TestStatus.Fail);
            if (name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase) || name.Contains("Standard-VGA", StringComparison.OrdinalIgnoreCase))
            {
                details.Add("  → Nur der Windows-Basistreiber ist aktiv (kein Herstellertreiber).");
                statuses.Add(TestStatus.Warning);
            }
        }
        foreach (var (adapter, wddm) in WddmInfo.Read()) details.Add($"{adapter}: {wddm}");

        var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var dx = new[] { ("d3d12.dll", "Direct3D 12"), ("d3d11.dll", "Direct3D 11"), ("dxgi.dll", "DXGI") }
            .Where(x => File.Exists(Path.Combine(sys32, x.Item1))).Select(x => x.Item2).ToList();
        details.Add("DirectX-Laufzeit: " + (dx.Count > 0 ? string.Join(", ", dx) : "nicht gefunden"));
        if (dx.Count == 0) statuses.Add(TestStatus.Warning);

        var tdr = EventProbe.Query("System", "Provider[@Name='Display'] and EventID=4101", 30, 100);
        if (tdr is not null)
        {
            details.Add($"Treiber-Neustarts (TDR, Ereignis 4101) in 30 Tagen: {tdr.Count}");
            if (tdr.Count >= 5) statuses.Add(TestStatus.Fail);
            else if (tdr.Count > 0) statuses.Add(TestStatus.Warning);
        }

        var load = sensors.Best(UnifiedSensorId.GpuLoad);
        details.Add(load is null ? "Grafik-Engine-Zähler: nicht verfügbar" : $"Grafik-Engine aktiv, Last {load.Current:0} %");

        var core = sensors.Best(UnifiedSensorId.GpuCoreTemp);
        var hot = sensors.Best(UnifiedSensorId.GpuHotspotTemp);
        if (core is null) details.Add("Temperatur: vom Treiber nicht gemeldet");
        else
        {
            details.Add($"Temperatur: {core.Current:0} °C (Quelle {core.SourceName})" + (hot is null ? "" : $", Hotspot {hot.Current:0} °C"));
            if (core.Current >= 90 || hot?.Current >= 105) statuses.Add(TestStatus.Warning);
        }

        if (statuses.Count == 0) statuses.Add(TestStatus.Pass);
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Grafikproblem erkannt (Gerätefehler oder häufige Treiber-Neustarts).",
            TestStatus.Warning => "Grafik funktioniert, Hinweise beachten.",
            _ => "Grafikkarte und Treiber funktionieren.",
        }, details);
    }

    private ComponentTestResult TestVram(ComponentTestDefinition d)
    {
        var gpus = hardware.GetGpus().Where(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)).ToList();
        if (gpus.Count == 0 || gpus.All(g => g.VramBytes is null)) return R(d, TestStatus.NotAvailable, "Grafikspeichergröße nicht ermittelbar.");
        var details = gpus.Select(g => $"{g.Name}: {Format.Bytes(g.VramBytes)}").ToList();
        ulong? used = null;
        try
        {
            var data = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
            if (data.Contains("Dedicated Usage"))
                foreach (InstanceData inst in data["Dedicated Usage"].Values)
                    used = Math.Max(used ?? 0, (ulong)Math.Max(0, inst.RawValue));
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
        }
        var total = gpus.Max(g => g.VramBytes ?? 0);
        if (used is null || total == 0)
        {
            details.Add("Belegung: Leistungsindikator nicht verfügbar");
            return R(d, TestStatus.Pass, $"{Format.Bytes(total)} Grafikspeicher erkannt.", details);
        }
        var pct = 100.0 * used.Value / total;
        details.Add($"Belegt: {Format.Bytes(used)} ({pct:0} %)");
        return pct >= 95
            ? R(d, TestStatus.Warning, "Grafikspeicher fast voll – Spiele/Anwendungen können ruckeln.", details)
            : R(d, TestStatus.Pass, $"{Format.Bytes(total)} Grafikspeicher, {pct:0} % belegt.", details);
    }

    // ------------------------------------------------------------------ Mainboard / BIOS / TPM / Secure Boot

    private ComponentTestResult TestMainboard(ComponentTestDefinition d)
    {
        var r = Wmi.Query("SELECT Manufacturer, Product, Version, Status FROM Win32_BaseBoard").Rows.FirstOrDefault();
        if (r is null) return R(d, TestStatus.NotAvailable, "Mainboard-Daten nicht verfügbar.");
        var details = new List<string> { $"{r.Str("Manufacturer")} {r.Str("Product")}".Trim(), $"Revision: {r.Str("Version") ?? "–"}" };
        var errors = PnpErrors("System");
        foreach (var e in errors.Take(5)) details.Add($"Systemgerät mit Fehler: {e.Name} – {e.ErrorText}");
        var st = r.Str("Status");
        if (st is not null) details.Add($"Status: {st}");
        if (errors.Count > 0) return R(d, TestStatus.Warning, $"{errors.Count} Systemgerät(e) mit Fehler im Geräte-Manager.", details);
        return R(d, TestStatus.Pass, "Mainboard erkannt, keine Systemgerätefehler.", details);
    }

    private ComponentTestResult TestBios(ComponentTestDefinition d)
    {
        var b = hardware.GetBoard();
        var details = new List<string>
        {
            $"Firmware: {b.Firmware}",
            $"Hersteller: {b.BiosVendor ?? "–"}",
            $"Version: {b.BiosVersion ?? "–"}",
            $"Datum: {(b.BiosDate is { } dt ? dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "–")}",
        };
        if (b.Firmware == "Legacy BIOS")
            return R(d, TestStatus.Warning, "Legacy-BIOS-Modus: Secure Boot und Windows-11-Sicherheitsfunktionen nicht nutzbar.", details);
        if (b.BiosDate is { } date && date < DateTime.Now.AddYears(-6))
            details.Add("Hinweis: Firmware ist älter als 6 Jahre – Herstellerseite auf Updates prüfen.");
        return R(d, b.BiosVersion is null ? TestStatus.NotAvailable : TestStatus.Pass,
            b.BiosVersion is null ? "Firmware-Version nicht ermittelbar." : $"{b.Firmware}-Firmware {b.BiosVersion}.", details);
    }

    private static ComponentTestResult TestTpm(ComponentTestDefinition d)
    {
        var (text, state) = HardwareService.GetTpm();
        return R(d, state == DataState.Warning && text.StartsWith("Kein", StringComparison.Ordinal) ? TestStatus.Warning : FromState(state), text,
            ["Nur lesende Abfrage über TBS (TPM Base Services). Es werden keine TPM-Einstellungen geändert."]);
    }

    private static ComponentTestResult TestSecureBoot(ComponentTestDefinition d)
    {
        var firmware = Native.NativeMethods.GetFirmwareType(out var ft) && ft == Native.NativeMethods.FirmwareType.Uefi ? "UEFI" : "Legacy BIOS";
        var (text, state) = HardwareService.GetSecureBoot(firmware);
        return R(d, FromState(state), text, [$"Firmware: {firmware}", "Nur lesend – Secure Boot wird nur im UEFI-Setup geändert."]);
    }

    // ------------------------------------------------------------------ Sensoren / Lüfter / Akku

    private ComponentTestResult TestSensors(ComponentTestDefinition d)
    {
        var src = sensors.Sources;
        var details = src.Select(s => $"{s.Headline} · {s.SensorCount} Werte").ToList();
        var temps = sensors.Sensors.Where(s => s.Unit == "°C").ToList();
        foreach (var t in temps.Take(10)) details.Add($"{t.Code} {t.Label}: {t.Current:0} °C ({t.SourceName})");
        if (sensors.LastUpdate is null) return R(d, TestStatus.NotAvailable, "Sensoren wurden noch nicht gelesen.", details);
        if (temps.Count == 0) return R(d, TestStatus.NotAvailable, "Keine Temperatursensoren verfügbar. Optional HWiNFO oder LibreHardwareMonitor starten.", details);
        var hot = temps.Where(t => t.Current >= 90).ToList();
        if (hot.Count > 0) return R(d, TestStatus.Warning, $"Hohe Temperatur: {string.Join(", ", hot.Select(h => $"{h.Label} {h.Current:0} °C"))}", details);
        var onlyAcpi = temps.All(t => t.Source == SensorSourceKind.AcpiThermal);
        return onlyAcpi
            ? R(d, TestStatus.Warning, "Nur ACPI-Firmwaresensoren verfügbar – keine CPU-Kerntemperatur.", details)
            : R(d, TestStatus.Pass, $"{temps.Count} Temperatursensoren, alle im normalen Bereich.", details);
    }

    private ComponentTestResult TestFans(ComponentTestDefinition d)
    {
        var fans = sensors.Sensors.Where(s => s.Id is UnifiedSensorId.CpuFan or UnifiedSensorId.GpuFan or UnifiedSensorId.SystemFan).ToList();
        if (fans.Count == 0) return R(d, TestStatus.NotAvailable, "Keine Lüfterdaten. Mainboard-Lüfter liefern HWiNFO oder LibreHardwareMonitor.");
        var details = fans.Select(f => $"{f.Label}: {f.Current:0} {f.Unit} ({f.SourceName})").ToList();
        var stopped = fans.Where(f => f.Unit == "RPM" && f.Current <= 0 && f.Id == UnifiedSensorId.CpuFan).ToList();
        if (stopped.Count > 0) return R(d, TestStatus.Warning, "CPU-Lüfter meldet 0 U/min – Anschluss und Lüfter prüfen.", details);
        return R(d, TestStatus.Pass, $"{fans.Count} Lüfterwerte verfügbar.", details);
    }

    private ComponentTestResult TestBattery(ComponentTestDefinition d)
    {
        var b = inventory.GetBattery();
        if (b is null) return R(d, TestStatus.NotAvailable, "Kein Akku vorhanden (Desktop-PC).");
        var details = new List<string> { $"Ladezustand: {(b.ChargePercent is { } c ? c + " %" : "–")} · {b.Status}" };
        if (b.DesignCapacity is { } dc) details.Add($"Designkapazität: {dc} mWh");
        if (b.FullChargeCapacity is { } fc) details.Add($"Volle Ladekapazität: {fc} mWh");
        if (b.HealthPercent is not { } health) return R(d, TestStatus.Pass, "Akku erkannt (Gesundheit wird nicht gemeldet).", details);
        details.Add($"Akkugesundheit: {health} %");
        return health < 50
            ? R(d, TestStatus.Warning, $"Akku stark gealtert ({health} % der Designkapazität).", details)
            : R(d, TestStatus.Pass, $"Akkugesundheit {health} %.", details);
    }

    // ------------------------------------------------------------------ Treiber

    private ComponentTestResult TestGpuDriver(ComponentTestDefinition d)
    {
        var gpus = hardware.GetGpus();
        if (gpus.Count == 0) return R(d, TestStatus.NotAvailable, "Keine Grafikkarte gefunden.");
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        foreach (var g in gpus)
        {
            details.Add($"{g.Name}: Version {g.DriverVersion ?? "–"}, Datum {(g.DriverDate is { } dt ? dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "–")}");
            if (g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase))
            {
                statuses.Add(TestStatus.Fail);
                details.Add("  → Kein Herstellertreiber installiert (Microsoft-Basisanzeigetreiber).");
            }
            else if (g.DriverDate is { } date && date < DateTime.Now.AddYears(-2))
            {
                statuses.Add(TestStatus.Warning);
                details.Add("  → Treiber ist älter als 2 Jahre.");
            }
            else statuses.Add(TestStatus.Pass);
        }
        foreach (var e in PnpErrors("Display")) { details.Add($"{e.Name}: {e.ErrorText}"); statuses.Add(TestStatus.Fail); }
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Grafiktreiber fehlt oder meldet einen Fehler.",
            TestStatus.Warning => "Grafiktreiber veraltet – Update beim Hersteller empfohlen.",
            _ => "Grafiktreiber installiert und aktuell genug.",
        }, details);
    }

    private ComponentTestResult TestChipsetDriver(ComponentTestDefinition d)
    {
        string[] keys = ["Chipset", "Chipsatz", "SMBus", "PCI Express Root", "PCI-Express-Stamm", "LPC", "Management Engine", "PSP", "GPIO", "Serial IO", "PCI-to-PCI"];
        var r = Wmi.Query("SELECT DeviceName, DriverVersion, DriverDate, Manufacturer FROM Win32_PnPSignedDriver WHERE DeviceClass = 'SYSTEM'", timeoutSeconds: 60);
        var details = new List<string>();
        if (r.Ok)
        {
            var hits = r.Rows.Where(x => x.Str("DeviceName") is { } n && keys.Any(k => n.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Str("DeviceName")).Select(g => g.First()).Take(8).ToList();
            foreach (var h in hits)
                details.Add($"{h.Str("DeviceName")}: {h.Str("DriverVersion") ?? "–"} ({h.Str("Manufacturer") ?? "?"}, {h.Date("DriverDate")?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "–"})");
            if (hits.Count == 0) details.Add("Keine typischen Chipsatz-Geräte gefunden (Windows-Standardtreiber).");
        }
        else details.Add("Treiberliste nicht abfragbar: " + (r.Error ?? "WMI"));

        var errors = PnpErrors("System");
        foreach (var e in errors.Take(6)) details.Add($"FEHLER {e.Name}: {e.ErrorText}");
        if (errors.Count > 0) return R(d, TestStatus.Fail, $"{errors.Count} Systemgerät(e) ohne funktionierenden Treiber.", details);
        return R(d, r.Ok ? TestStatus.Pass : TestStatus.NotAvailable, r.Ok ? "Chipsatz-/Systemgeräte arbeiten ohne Fehler." : "Chipsatztreiber nicht abfragbar.", details);
    }
}
