using System.Management;
using UltimateX.Core.Common;
using UltimateX.Services.Common;

namespace UltimateX.Services.Diagnostics;

public sealed partial class ComponentTestService
{
    // ------------------------------------------------------------------ Bluetooth (WinRT Radios, nur lesend)

    private async Task<ComponentTestResult> TestBluetoothAsync(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var bt = PnpOf("Bluetooth");
        var errors = bt.Where(p => p.ErrorCode is not null and not 0).ToList();
        string? radioState = null;
        try
        {
            var radios = await Windows.Devices.Radios.Radio.GetRadiosAsync();
            var r = radios.FirstOrDefault(x => x.Kind == Windows.Devices.Radios.RadioKind.Bluetooth);
            if (r is not null) radioState = r.State.ToString();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            details.Add("Funkstatus nicht abfragbar: " + ex.Message);
        }
        if (bt.Count == 0 && radioState is null) return R(d, TestStatus.NotAvailable, "Kein Bluetooth-Adapter vorhanden.", details);
        if (radioState is not null) details.Add($"Funkmodul: {radioState switch { "On" => "eingeschaltet", "Off" => "ausgeschaltet", "Disabled" => "deaktiviert", _ => radioState }}");
        details.Add($"Bluetooth-Geräte im Geräte-Manager: {bt.Count}");
        foreach (var e in errors.Take(6)) details.Add($"Fehler: {e.Name} – {e.ErrorText}");
        if (errors.Count > 0) return R(d, TestStatus.Fail, $"{errors.Count} Bluetooth-Gerät(e) mit Fehler.", details);
        if (radioState is "Off" or "Disabled") return R(d, TestStatus.Warning, "Bluetooth ist ausgeschaltet.", details);
        return R(d, TestStatus.Pass, "Bluetooth betriebsbereit.", details);
    }

    // ------------------------------------------------------------------ USB

    private ComponentTestResult TestUsb(ComponentTestDefinition d)
    {
        var usb = Context.Pnp.Value.Where(p => p.Class.Equals("USB", StringComparison.OrdinalIgnoreCase)
                                               || p.DeviceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (usb.Count == 0) return R(d, TestStatus.NotAvailable, "Keine USB-Geräte gefunden.");
        var errors = usb.Where(p => p.ErrorCode is not null and not 0).ToList();
        var details = new List<string>
        {
            $"USB-Controller/Hubs: {usb.Count(p => p.Class.Equals("USB", StringComparison.OrdinalIgnoreCase))}",
            $"USB-Geräte insgesamt: {usb.Count}",
        };
        foreach (var e in errors.Take(10)) details.Add($"FEHLER {e.Name}: {e.ErrorText}");
        if (errors.Any(e => e.ErrorCode is 10 or 43 or 28 or 31 or 18))
            return R(d, TestStatus.Fail, $"{errors.Count} USB-Gerät(e) mit Fehler (z. B. Code 43: Gerät meldet Fehler, Code 10: startet nicht).", details);
        if (errors.Count > 0) return R(d, TestStatus.Warning, $"{errors.Count} USB-Gerät(e) deaktiviert oder nicht konfiguriert.", details);
        return R(d, TestStatus.Pass, "Alle USB-Controller und -Geräte arbeiten ohne Fehler.", details);
    }

    // ------------------------------------------------------------------ Audio

    private ComponentTestResult TestAudio(ComponentTestDefinition d)
    {
        var devs = Wmi.Query("SELECT Name, Status, ConfigManagerErrorCode FROM Win32_SoundDevice").Rows;
        var details = devs.Select(x => $"{x.Str("Name")}: {x.Str("Status") ?? "?"}").ToList();
        details.Add($"Wiedergabegeräte: {AudioTest.OutputDeviceCount} · Aufnahmegeräte: {AudioTest.InputDeviceCount}");
        var errors = PnpErrors("MEDIA", "AudioEndpoint");
        foreach (var e in errors) details.Add($"Fehler: {e.Name} – {e.ErrorText}");
        if (devs.Count == 0 && AudioTest.OutputDeviceCount == 0) return R(d, TestStatus.NotAvailable, "Kein Audiogerät gefunden.", details);
        if (errors.Count > 0) return R(d, TestStatus.Fail, "Audiotreiber meldet einen Fehler.", details);
        if (!(Svc("Audiosrv")?.Running ?? false)) return R(d, TestStatus.Fail, "Windows-Audiodienst läuft nicht.", details);
        return R(d, TestStatus.Pass, "Audiogeräte und Audiodienst betriebsbereit.", details);
    }

    private static ComponentTestResult TestSpeakers(ComponentTestDefinition d)
    {
        var n = AudioTest.OutputDeviceCount;
        return n == 0
            ? R(d, TestStatus.NotAvailable, "Kein Wiedergabegerät vorhanden.")
            : R(d, TestStatus.UserActionRequired, "Wiedergabegerät erkannt – „PLAY TEST SOUND“ und bestätigen, ob der Ton links/rechts hörbar war.",
                [$"{n} Wiedergabegerät(e)", "Der Testton läuft über das Windows-Standardausgabegerät."]);
    }

    private static ComponentTestResult TestMicrophone(ComponentTestDefinition d)
    {
        var n = AudioTest.InputDeviceCount;
        return n == 0
            ? R(d, TestStatus.NotAvailable, "Kein Mikrofon vorhanden.")
            : R(d, TestStatus.UserActionRequired, "Mikrofon erkannt – Live-Pegel per Klick prüfen (es wird nichts aufgenommen oder gespeichert).",
                [$"{n} Aufnahmegerät(e)"]);
    }

    // ------------------------------------------------------------------ Kamera (WinRT, nur auf Benutzeraktion geöffnet)

    private async Task<ComponentTestResult> TestCameraPresenceAsync(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var count = 0;
        try
        {
            var cams = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);
            count = cams.Count;
            details.AddRange(cams.Select(c => $"{c.Name}{(c.IsEnabled ? "" : " (deaktiviert)")}"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            details.Add("Kameraliste nicht abfragbar: " + ex.Message);
        }
        var errors = PnpErrors("Camera", "Image");
        foreach (var e in errors) details.Add($"Fehler: {e.Name} – {e.ErrorText}");
        if (errors.Count > 0) return R(d, TestStatus.Fail, "Kameratreiber meldet einen Fehler.", details);
        if (count == 0) return R(d, TestStatus.NotAvailable, "Keine Kamera vorhanden.", details);
        return R(d, TestStatus.UserActionRequired, "Kamera erkannt – Funktionstest nur per Klick (kein Bild wird gespeichert).", details);
    }

    /// <summary>
    /// Öffnet die erste Kamera kurz im Nur-Lese-Freigabemodus und schließt sie wieder (nur auf Benutzeraktion).
    /// Es wird kein Bild aufgenommen oder gespeichert. Prüft, ob Treiber und Datenschutzfreigabe den Zugriff erlauben.
    /// </summary>
    public static async Task<ComponentTestResult> RunCameraAccessTestAsync()
    {
        var d = Definition("camera");
        try
        {
            var cams = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(Windows.Devices.Enumeration.DeviceClass.VideoCapture);
            var cam = cams.FirstOrDefault(c => c.IsEnabled);
            if (cam is null) return R(d, TestStatus.NotAvailable, "Keine aktivierte Kamera gefunden.");
            using var capture = new Windows.Media.Capture.MediaCapture();
            await capture.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
            {
                VideoDeviceId = cam.Id,
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Video,
                SharingMode = Windows.Media.Capture.MediaCaptureSharingMode.SharedReadOnly,
            });
            return R(d, TestStatus.Pass, $"Kamera „{cam.Name}“ lässt sich öffnen.", ["Kein Bild aufgenommen oder gespeichert; die Kamera wurde sofort wieder freigegeben."]);
        }
        catch (UnauthorizedAccessException)
        {
            return R(d, TestStatus.Warning, "Kamerazugriff blockiert – Windows-Einstellungen → Datenschutz → Kamera → „Desktop-Apps Zugriff erlauben“.");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            return R(d, TestStatus.Fail, "Kamera konnte nicht geöffnet werden (belegt oder Treiberproblem).", [ex.Message]);
        }
    }

    // ------------------------------------------------------------------ Monitore

    private ComponentTestResult TestMonitors(ComponentTestDefinition d)
    {
        var mons = displays.GetMonitors();
        var details = mons.Select(m => $"{m.FriendlyName}{(m.IsPrimary ? " (Hauptbildschirm)" : "")}: {m.Width} × {m.Height} @ {m.RefreshHz} Hz, Skalierung {m.ScalingPercent} %, HDR {m.Hdr}").ToList();
        var errors = PnpErrors("Monitor");
        foreach (var e in errors) details.Add($"Fehler: {e.Name} – {e.ErrorText}");
        details.Add("Optional: Farbtest (Rot/Grün/Blau/Weiß/Schwarz/Grau) für Pixelfehler und Ausleuchtung.");
        if (mons.Count == 0) return R(d, TestStatus.NotAvailable, "Kein aktiver Bildschirm ermittelbar.", details);
        if (errors.Count > 0) return R(d, TestStatus.Warning, "Monitortreiber meldet einen Fehler.", details);
        return R(d, TestStatus.Pass, $"{mons.Count} Bildschirm(e) aktiv.", details);
    }

    // ------------------------------------------------------------------ Tastatur / Maus

    private static ComponentTestResult TestInputDevice(ComponentTestDefinition d, string wmiClass, string label)
    {
        var wql = wmiClass == "Win32_Keyboard"
            ? "SELECT Name, Status, ConfigManagerErrorCode FROM Win32_Keyboard"
            : "SELECT Name, Status, ConfigManagerErrorCode FROM Win32_PointingDevice";
        var rows = Wmi.Query(wql).Rows;
        if (rows.Count == 0) return R(d, TestStatus.NotAvailable, $"Keine {label} gefunden.");
        var details = rows.Select(r => $"{r.Str("Name")}: {r.Str("Status") ?? "?"}" + (r.U64("ConfigManagerErrorCode") is > 0 and var c ? $" (Code {c})" : "")).ToList();
        if (rows.Any(r => r.U64("ConfigManagerErrorCode") is > 0)) return R(d, TestStatus.Fail, $"{label} meldet einen Gerätefehler.", details);
        details.Add("Tests laufen nur im Testfenster von ULTIMATE X – Eingaben außerhalb werden nicht erfasst.");
        return R(d, TestStatus.UserActionRequired, $"{rows.Count} Gerät(e) erkannt – Funktion im Testfenster prüfen.", details);
    }

    // ------------------------------------------------------------------ Drucker

    private ComponentTestResult TestPrinter(ComponentTestDefinition d)
    {
        var printers = Wmi.Query("SELECT Name, Default, WorkOffline, PrinterStatus, DetectedErrorState, Local, Network FROM Win32_Printer").Rows;
        var spooler = Svc("Spooler");
        var details = new List<string> { $"Druckwarteschlange (Spooler): {(spooler is null ? "nicht vorhanden" : spooler.Running ? "läuft" : spooler.StartMode == "Disabled" ? "deaktiviert" : "beendet")}" };
        if (printers.Count == 0) return R(d, spooler?.Running == false ? TestStatus.Warning : TestStatus.NotAvailable, "Kein Drucker eingerichtet.", details);
        foreach (var p in printers)
        {
            var offline = p.Bool("WorkOffline") == true;
            var err = p.U64("DetectedErrorState") is { } e and > 2 ? $", Fehlerstatus {e}" : "";
            details.Add($"{p.Str("Name")}{(p.Bool("Default") == true ? " (Standard)" : "")}: {(offline ? "offline" : "bereit")}{err}");
        }
        if (spooler is { Running: false }) return R(d, TestStatus.Fail, "Druckwarteschlange läuft nicht – Drucken nicht möglich.", details);
        var def = printers.FirstOrDefault(p => p.Bool("Default") == true);
        if (def is not null && def.Bool("WorkOffline") == true) return R(d, TestStatus.Warning, "Standarddrucker ist offline.", details);
        details.Add("Optional: Windows-Testseite auf einem Drucker ausgeben.");
        return R(d, TestStatus.Pass, $"{printers.Count} Drucker eingerichtet.", details);
    }

    /// <summary>Namen der eingerichteten Drucker (für die Testseiten-Auswahl).</summary>
    public static IReadOnlyList<string> GetPrinterNames() =>
        Wmi.Query("SELECT Name FROM Win32_Printer").Rows.Select(r => r.Str("Name")).OfType<string>().ToList();

    /// <summary>
    /// Druckt die Windows-Testseite über die WMI-Methode Win32_Printer.PrintTestPage (nur auf Benutzeraktion).
    /// Der Drucker wird aus der aufgezählten Liste gewählt – es wird kein Pfad/Befehl aus dem Namen zusammengesetzt.
    /// </summary>
    public static OperationResult PrintTestPage(string printerName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Printer");
            using var all = searcher.Get();
            foreach (ManagementObject p in all)
            {
                using (p)
                {
                    if (!string.Equals(p["Name"] as string, printerName, StringComparison.Ordinal)) continue;
                    var rc = p.InvokeMethod("PrintTestPage", null);
                    return rc is uint u && u == 0
                        ? OperationResult.Ok($"Testseite an „{printerName}“ gesendet.")
                        : OperationResult.Fail($"Der Drucker hat die Testseite abgelehnt (Code {rc}).");
                }
            }
            return OperationResult.Fail("Drucker nicht gefunden.");
        }
        catch (Exception ex) when (ex is ManagementException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return OperationResult.Fail("Testseite konnte nicht gedruckt werden: " + ex.Message);
        }
    }
}
