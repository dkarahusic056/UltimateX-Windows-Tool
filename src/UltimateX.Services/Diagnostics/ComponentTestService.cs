using System.Diagnostics;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Health;
using UltimateX.Services.Network;
using UltimateX.Services.Security;
using UltimateX.Services.Sensors;

namespace UltimateX.Services.Diagnostics;

/// <summary>
/// SYSTEM COMPONENT TEST CENTER (v1.1 Phase 4).
///
/// Grundsätze:
///  • Standardmäßig nur sichere, nicht-destruktive und überwiegend lesende Prüfungen.
///  • Schreibtest auf Datenträgern nur nach ausdrücklicher Bestätigung, ausschließlich mit einer eigenen Temp-Datei.
///  • Kamera/Mikrofon/Tastatur/Maus/Lautsprecher/Bildschirm-Farbtest nur auf Benutzeraktion – nichts wird gespeichert.
///  • Keine Änderung an Sicherheitsfunktionen (nur lesen), kein Shell-Aufruf mit Benutzereingaben.
///  • Was nicht messbar ist, wird als NOT AVAILABLE gemeldet – es wird nichts geschätzt.
/// </summary>
public sealed partial class ComponentTestService(
    HardwareService hardware,
    SecurityService security,
    WindowsHealthService health,
    NetworkService network,
    WlanService wlan,
    DisplayService displays,
    InventoryService inventory,
    SensorManager sensors)
{
    public static IReadOnlyList<ComponentTestDefinition> Definitions { get; } =
    [
        new("cpu", "CPU", TestCategory.Hardware, "Rechentest Integer/Gleitkomma/Mehrkern, WHEA-Fehler, Temperatur"),
        new("ram", "RAM", TestCategory.Hardware, "Kapazität, Module, Mustertest (256 MB), Ergebnis der Windows-Speicherdiagnose"),
        new("gpu", "GPU", TestCategory.Hardware, "Treiberstatus, WDDM, DirectX-Laufzeit, TDR-Ereignisse, Temperatur"),
        new("vram", "VRAM", TestCategory.Hardware, "Größe und aktuelle Belegung des Grafikspeichers"),
        new("mainboard", "Mainboard", TestCategory.Hardware, "Hersteller, Modell, Gerätestatus"),
        new("bios", "BIOS / UEFI", TestCategory.Hardware, "Firmware-Typ, Version, Datum"),
        new("sensors", "Sensoren", TestCategory.Hardware, "Verfügbare Sensorquellen und Temperaturen"),
        new("fans", "Lüfter", TestCategory.Hardware, "Lüfterdrehzahlen aus den Sensorquellen"),
        new("battery", "Akku", TestCategory.Hardware, "Ladezustand und Akkugesundheit"),
        new("nvme", "NVMe", TestCategory.Storage, "Gesundheit, SMART, Temperatur, Fehlerereignisse, Lesetest"),
        new("ssd", "SSD", TestCategory.Storage, "Gesundheit, SMART, Temperatur, Fehlerereignisse, Lesetest"),
        new("hdd", "HDD", TestCategory.Storage, "Gesundheit, SMART, Temperatur, Fehlerereignisse, Lesetest"),
        new("storagecontroller", "Speichercontroller", TestCategory.Storage, "Controller-Status und Datenträger-Fehlerereignisse"),
        new("ethernet", "Ethernet", TestCategory.Network, "Adapter, Treiber, Link, DHCP, IP, Gateway"),
        new("wifi", "Wi-Fi", TestCategory.Network, "WLAN-Adapter, Verbindung, Signal"),
        new("dns", "DNS", TestCategory.Network, "Namensauflösung und Antwortzeit"),
        new("gateway", "Gateway", TestCategory.Network, "Erreichbarkeit des Routers"),
        new("internet", "Internet", TestCategory.Network, "Erreichbarkeit und Paketverlust"),
        new("bluetooth", "Bluetooth", TestCategory.Peripherals, "Funkmodul und Gerätestatus"),
        new("usb", "USB", TestCategory.Peripherals, "USB-Controller und Geräte (Geräte-Manager-Fehlercodes)"),
        new("audio", "Audio", TestCategory.Peripherals, "Audiogeräte und Treiberstatus"),
        new("speakers", "Lautsprecher", TestCategory.Peripherals, "Ausgabegerät – Testton per Klick"),
        new("microphone", "Mikrofon", TestCategory.Peripherals, "Eingabegerät – Live-Pegel per Klick (keine Aufnahme)"),
        new("camera", "Kamera", TestCategory.Peripherals, "Kamera erkannt – Funktionstest nur per Klick"),
        new("monitors", "Monitore", TestCategory.Peripherals, "Erkannte Bildschirme, Auflösung, Hz – optionaler Farbtest"),
        new("keyboard", "Tastatur", TestCategory.Peripherals, "Gerätestatus – Tastentest im Testfenster"),
        new("mouse", "Maus", TestCategory.Peripherals, "Gerätestatus – Tasten-/Radtest im Testfenster"),
        new("printer", "Drucker", TestCategory.Peripherals, "Drucker, Standarddrucker, Status, Spooler"),
        new("tpm", "TPM", TestCategory.Security, "Vorhandensein und Version (nur lesend)"),
        new("secureboot", "Secure Boot", TestCategory.Security, "Status (nur lesend)"),
        new("defender", "Defender", TestCategory.Security, "Virenschutz, Signaturen, Manipulationsschutz (nur lesend)"),
        new("firewall", "Firewall", TestCategory.Security, "Profile Domäne/Privat/Öffentlich (nur lesend)"),
        new("securitycenter", "Sicherheitsfunktionen", TestCategory.Security, "UAC, BitLocker, Windows-Sicherheitscenter (nur lesend)"),
        new("services", "Windows-Dienste", TestCategory.Windows, "Wichtige Dienste: Status und Starttyp"),
        new("windowsupdate", "Windows Update", TestCategory.Windows, "Dienst, letzte Installation, Fehler, Neustart"),
        new("eventlog", "Ereignisprotokoll", TestCategory.Windows, "System/Application/Setup lesbar, kritische Ereignisse"),
        new("wmi", "WMI / CIM", TestCategory.Windows, "Abfragen und Antwortzeit"),
        new("search", "Windows Search", TestCategory.Windows, "Suchdienst"),
        new("gpudriver", "GPU-Treiber", TestCategory.Windows, "Version, Datum, Hersteller-/Basistreiber"),
        new("chipsetdriver", "Chipsatztreiber", TestCategory.Windows, "Chipsatz-/Systemgeräte und Fehler"),
        new("systemtime", "Systemzeit", TestCategory.Windows, "Abweichung zur Internetzeit (SNTP), Zeitdienst"),
        new("winre", "WinRE", TestCategory.Windows, "Windows-Wiederherstellungsumgebung (nur lesend)"),
    ];

    public static ComponentTestDefinition Definition(string id) => Definitions.First(d => d.Id == id);

    /// <summary>Gemeinsam genutzte, teure Abfragen eines Laufs (einmal je Lauf).</summary>
    private sealed class RunContext
    {
        public Lazy<IReadOnlyList<PnpDevice>> Pnp { get; init; } = null!;
        public Lazy<IReadOnlyList<SecurityItem>> Security { get; init; } = null!;
        public Lazy<IReadOnlyList<AdapterInfo>> Adapters { get; init; } = null!;
        public Lazy<IReadOnlyList<DiskEntry>> Disks { get; init; } = null!;
        public Lazy<List<EventHit>?> DiskEvents { get; init; } = null!;
    }

    private RunContext? _ctx;

    private RunContext Context => _ctx ??= NewContext();

    private RunContext NewContext() => new()
    {
        Pnp = new(() => inventory.GetDevices().Devices),
        Security = new(() => security.GetStatus()),
        Adapters = new(() => network.GetAdapters()),
        Disks = new(ReadDisks),
        DiskEvents = new(() => EventProbe.Query("System",
            "(Provider[@Name='disk'] and (EventID=7 or EventID=11 or EventID=51 or EventID=153)) or (Provider[@Name='Ntfs'] and EventID=55) or (Provider[@Name='Microsoft-Windows-Ntfs'] and EventID=55)",
            30, 300, withMessages: 60)),
    };

    /// <summary>Verwirft zwischengespeicherte Abfragen (vor jedem neuen Gesamtlauf).</summary>
    public void ResetContext()
    {
        _ctx = null;
        _services = null;
    }

    /// <summary>Führt einen Test aus. Fehler im Test führen zu NOT AVAILABLE mit Begründung – nie zum Absturz.</summary>
    public async Task<ComponentTestResult> RunAsync(string id, CancellationToken ct)
    {
        var def = Definition(id);
        var sw = Stopwatch.StartNew();
        ComponentTestResult result;
        try
        {
            result = await Dispatch(def, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Warn("ComponentTest", $"Test {id} fehlgeschlagen", ex.Message);
            result = ComponentTestResult.Create(def, TestStatus.NotAvailable, "Test konnte nicht ausgeführt werden: " + ex.Message);
        }
        return result with { DurationMs = sw.Elapsed.TotalMilliseconds, Time = DateTime.Now };
    }

    /// <summary>„TEST ALL SYSTEM COMPONENTS“: alle sicheren Tests nacheinander. Interaktive Teile bleiben USER ACTION REQUIRED.</summary>
    public async Task<ComponentTestRun> RunAllAsync(IProgress<ComponentTestResult>? progress, CancellationToken ct, IEnumerable<string>? only = null)
    {
        ResetContext();
        var started = DateTime.Now;
        var ids = only?.ToHashSet(StringComparer.Ordinal);
        var results = new List<ComponentTestResult>();
        foreach (var def in Definitions.Where(d => !d.Interactive && (ids is null || ids.Contains(d.Id))))
        {
            ct.ThrowIfCancellationRequested();
            var r = await RunAsync(def.Id, ct).ConfigureAwait(false);
            results.Add(r);
            progress?.Report(r);
        }
        return new ComponentTestRun(Guid.NewGuid().ToString("N"), started, DateTime.Now, results);
    }

    /// <summary>Ergebnis eines vom Benutzer bestätigten interaktiven Tests (z. B. „Ton war hörbar“).</summary>
    public static ComponentTestResult UserConfirmed(string id, bool ok, string summary, IEnumerable<string>? details = null)
        => ComponentTestResult.Create(Definition(id), ok ? TestStatus.Pass : TestStatus.Fail, summary, details);

    private Task<ComponentTestResult> Dispatch(ComponentTestDefinition d, CancellationToken ct) => d.Id switch
    {
        "cpu" => Task.Run(() => TestCpu(d, ct), ct),
        "ram" => Task.Run(() => TestRam(d, ct), ct),
        "gpu" => Task.Run(() => TestGpu(d), ct),
        "vram" => Task.Run(() => TestVram(d), ct),
        "mainboard" => Task.Run(() => TestMainboard(d), ct),
        "bios" => Task.Run(() => TestBios(d), ct),
        "sensors" => Task.FromResult(TestSensors(d)),
        "fans" => Task.FromResult(TestFans(d)),
        "battery" => Task.Run(() => TestBattery(d), ct),
        "nvme" => Task.Run(() => TestStorage(d, DiskKind.Nvme, ct), ct),
        "ssd" => Task.Run(() => TestStorage(d, DiskKind.Ssd, ct), ct),
        "hdd" => Task.Run(() => TestStorage(d, DiskKind.Hdd, ct), ct),
        "storagecontroller" => Task.Run(() => TestStorageController(d), ct),
        "ethernet" => Task.Run(() => TestEthernet(d), ct),
        "wifi" => Task.Run(() => TestWifi(d), ct),
        "dns" => TestDnsAsync(d, ct),
        "gateway" => TestGatewayAsync(d, ct),
        "internet" => TestInternetAsync(d, ct),
        "bluetooth" => TestBluetoothAsync(d),
        "usb" => Task.Run(() => TestUsb(d), ct),
        "audio" => Task.Run(() => TestAudio(d), ct),
        "speakers" => Task.Run(() => TestSpeakers(d), ct),
        "microphone" => Task.Run(() => TestMicrophone(d), ct),
        "camera" => TestCameraPresenceAsync(d),
        "monitors" => Task.Run(() => TestMonitors(d), ct),
        "keyboard" => Task.Run(() => TestInputDevice(d, "Win32_Keyboard", "Tastatur"), ct),
        "mouse" => Task.Run(() => TestInputDevice(d, "Win32_PointingDevice", "Zeigegerät"), ct),
        "printer" => Task.Run(() => TestPrinter(d), ct),
        "tpm" => Task.FromResult(TestTpm(d)),
        "secureboot" => Task.FromResult(TestSecureBoot(d)),
        "defender" => Task.Run(() => TestDefender(d), ct),
        "firewall" => Task.Run(() => TestFirewall(d), ct),
        "securitycenter" => Task.Run(() => TestSecurityCenter(d), ct),
        "services" => Task.Run(() => TestServices(d), ct),
        "windowsupdate" => Task.Run(() => TestWindowsUpdate(d), ct),
        "eventlog" => Task.Run(() => TestEventLog(d), ct),
        "wmi" => Task.Run(() => TestWmi(d), ct),
        "search" => Task.Run(() => TestSearch(d), ct),
        "gpudriver" => Task.Run(() => TestGpuDriver(d), ct),
        "chipsetdriver" => Task.Run(() => TestChipsetDriver(d), ct),
        "systemtime" => TestSystemTimeAsync(d, ct),
        "winre" => Task.Run(() => TestWinRe(d), ct),
        _ => Task.FromResult(ComponentTestResult.Create(d, TestStatus.NotTested, "Unbekannter Test")),
    };

    private static ComponentTestResult R(ComponentTestDefinition d, TestStatus s, string summary, IEnumerable<string>? details = null)
        => ComponentTestResult.Create(d, s, summary, details);

    private static TestStatus FromState(DataState s) => s switch
    {
        DataState.Ok => TestStatus.Pass,
        DataState.Warning => TestStatus.Warning,
        DataState.Problem => TestStatus.Fail,
        _ => TestStatus.NotAvailable,
    };

    private static TestStatus Worst(IEnumerable<TestStatus> all)
    {
        var list = all.ToList();
        if (list.Count == 0) return TestStatus.NotAvailable;
        return list.MaxBy(s => s.Severity());
    }

    /// <summary>Geräte einer PnP-Klasse mit Geräte-Manager-Fehlercode.</summary>
    private IReadOnlyList<PnpDevice> PnpErrors(params string[] classes) =>
        Context.Pnp.Value.Where(p => classes.Contains(p.Class, StringComparer.OrdinalIgnoreCase) && p.ErrorCode is not null and not 0).ToList();

    private IReadOnlyList<PnpDevice> PnpOf(params string[] classes) =>
        Context.Pnp.Value.Where(p => classes.Contains(p.Class, StringComparer.OrdinalIgnoreCase)).ToList();
}
