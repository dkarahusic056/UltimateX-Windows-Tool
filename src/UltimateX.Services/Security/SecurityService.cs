using Microsoft.Win32;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Native;

namespace UltimateX.Services.Security;

public sealed record SecurityItem(string Area, string Status, DataState State, string? Detail = null);

/// <summary>
/// Security Center (§53, §72–§76). Ausschließlich LESEND:
/// Defender, Firewall, Secure Boot, TPM und BitLocker werden niemals von der App verändert oder deaktiviert.
/// Für Änderungen werden die offiziellen Windows-Einstellungen geöffnet.
/// </summary>
public sealed class SecurityService
{
    public IReadOnlyList<SecurityItem> GetStatus()
    {
        var items = new List<SecurityItem>();
        items.Add(GetAppIntegrity());
        items.AddRange(GetAntivirus());
        items.AddRange(GetFirewall());

        var firmware = NativeMethods.GetFirmwareType(out var ft) && ft == NativeMethods.FirmwareType.Uefi ? "UEFI" : "Legacy BIOS";
        var (sb, sbState) = HardwareService.GetSecureBoot(firmware);
        items.Add(new SecurityItem("Secure Boot", sb, sbState));
        var (tpm, tpmState) = HardwareService.GetTpm();
        items.Add(new SecurityItem("TPM", tpm, tpmState));

        items.Add(GetBitLocker());
        items.Add(GetUac());
        items.Add(GetSecurityEvents());
        return items;
    }

    /// <summary>Application Integrity (§62): Signatur der eigenen EXE/DLLs.</summary>
    public static SecurityItem GetAppIntegrity()
    {
        var dir = AppContext.BaseDirectory;
        var files = Directory.GetFiles(dir, "UltimateX*.dll").Concat(Directory.GetFiles(dir, "UltimateX*.exe")).ToList();
        var results = files.Select(f => (File: Path.GetFileName(f), Sig: SignatureVerifier.Verify(f))).ToList();

        if (results.Any(r => r.Sig.Status == SignatureStatus.Tampered))
        {
            var bad = string.Join(", ", results.Where(r => r.Sig.Status == SignatureStatus.Tampered).Select(r => r.File));
            AppLog.Security("IntegrityFailure", "Manipulierte Programmdateien erkannt", LogLevel.Error, bad);
            return new SecurityItem("Application Integrity", "Manipulation erkannt", DataState.Problem, bad);
        }
        if (results.All(r => r.Sig.Status == SignatureStatus.Valid))
            return new SecurityItem("Application Integrity", "Alle Programmdateien gültig signiert", DataState.Ok, results.FirstOrDefault().Sig?.Publisher);
        return new SecurityItem("Application Integrity", "Entwicklungs-Build (nicht signiert)", DataState.Warning,
            "Release-Versionen werden per Authenticode signiert und beim Start geprüft.");
    }

    private static IEnumerable<SecurityItem> GetAntivirus()
    {
        var defender = Wmi.Query("SELECT AMServiceEnabled, AntivirusEnabled, RealTimeProtectionEnabled, AntivirusSignatureLastUpdated, QuickScanEndTime, FullScanEndTime, IsTamperProtected FROM MSFT_MpComputerStatus", Wmi.Defender);
        var d = defender.Rows.FirstOrDefault();
        if (d is not null && d.Bool("AntivirusEnabled") == true)
        {
            var rtp = d.Bool("RealTimeProtectionEnabled") == true;
            yield return new SecurityItem("Microsoft Defender", rtp ? "Aktiv, Echtzeitschutz an" : "Aktiv, Echtzeitschutz AUS",
                rtp ? DataState.Ok : DataState.Problem);

            var sig = d.Date("AntivirusSignatureLastUpdated");
            var sigState = sig is null ? DataState.NotAvailable : (DateTime.Now - sig.Value).TotalDays > 7 ? DataState.Warning : DataState.Ok;
            yield return new SecurityItem("Virendefinitionen", sig is null ? "nicht verfügbar" : $"Stand {Format.DateTimeText(sig)}", sigState);

            var lastScan = new[] { d.Date("QuickScanEndTime"), d.Date("FullScanEndTime") }.Where(x => x is not null).DefaultIfEmpty(null).Max();
            yield return new SecurityItem("Letzter Scan", lastScan is null ? "Kein Scan protokolliert" : Format.DateTimeText(lastScan),
                lastScan is null ? DataState.Warning : DataState.Ok);

            if (d.Bool("IsTamperProtected") is { } tamper)
                yield return new SecurityItem("Manipulationsschutz", tamper ? "Aktiv" : "Inaktiv", tamper ? DataState.Ok : DataState.Warning);
            yield break;
        }

        // Defender inaktiv/nicht verfügbar → Drittanbieter über Windows-Sicherheitscenter.
        var av = Wmi.Query("SELECT displayName, productState FROM AntiVirusProduct", Wmi.SecurityCenter2);
        var products = av.Rows.Where(r => r.Str("displayName") is not null).ToList();
        if (products.Count == 0)
        {
            yield return new SecurityItem("Virenschutz", "Kein aktiver Virenschutz erkannt", DataState.Problem);
            yield break;
        }
        foreach (var p in products)
        {
            var state = p.U64("productState") ?? 0;
            // productState: Byte 2 = Status (0x10/0x11 = aktiv), Byte 1 = Signaturen (0x00 = aktuell).
            var active = ((state >> 8) & 0xF0) == 0x10;
            var upToDate = (state & 0xF0) == 0x00;
            yield return new SecurityItem("Virenschutz", $"{p.Str("displayName")}: {(active ? "aktiv" : "inaktiv")}{(upToDate ? "" : ", Signaturen veraltet")}",
                active ? (upToDate ? DataState.Ok : DataState.Warning) : DataState.Problem);
        }
    }

    private static IEnumerable<SecurityItem> GetFirewall()
    {
        foreach (var (profile, label) in new[] { ("DomainProfile", "Domäne"), ("StandardProfile", "Privat"), ("PublicProfile", "Öffentlich") })
        {
            // Gruppenrichtlinien haben Vorrang vor lokalen Einstellungen.
            var policy = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{profile}");
            var local = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}");
            object? value;
            using (policy)
            using (local)
            {
                value = policy?.GetValue("EnableFirewall") ?? local?.GetValue("EnableFirewall");
            }
            yield return value switch
            {
                1 => new SecurityItem($"Firewall ({label})", "Aktiv", DataState.Ok),
                0 => new SecurityItem($"Firewall ({label})", "Deaktiviert", DataState.Problem),
                _ => new SecurityItem($"Firewall ({label})", "nicht ermittelbar", DataState.NotAvailable),
            };
        }
    }

    /// <summary>
    /// BitLocker-Status (§76). Vollständige Daten über WMI erfordern Adminrechte; ohne Rechte wird der
    /// Shell-Status gelesen (1/6 = aktiv, 2 = aus). Recovery-Keys werden niemals gelesen.
    /// </summary>
    private static SecurityItem GetBitLocker()
    {
        var systemDrive = (Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? "C:\\").TrimEnd('\\');

        var wmi = Wmi.Query("SELECT DriveLetter, ProtectionStatus FROM Win32_EncryptableVolume", Wmi.BitLocker);
        if (wmi.Ok)
        {
            var row = wmi.Rows.FirstOrDefault(r => string.Equals(r.Str("DriveLetter"), systemDrive, StringComparison.OrdinalIgnoreCase));
            return row?.U64("ProtectionStatus") switch
            {
                1 => new SecurityItem("BitLocker", $"{systemDrive} geschützt", DataState.Ok),
                0 => new SecurityItem("BitLocker", $"{systemDrive} nicht geschützt", DataState.Warning),
                _ => new SecurityItem("BitLocker", "nicht verfügbar", DataState.NotAvailable),
            };
        }

        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is not null)
            {
                dynamic shell = Activator.CreateInstance(shellType)!;
                var value = shell.NameSpace(17).ParseName(systemDrive).ExtendedProperty("System.Volume.BitLockerProtection");
                int? code = value is int i ? i : null;
                return code switch
                {
                    1 or 6 => new SecurityItem("BitLocker", $"{systemDrive} geschützt", DataState.Ok),
                    2 => new SecurityItem("BitLocker", $"{systemDrive} nicht geschützt", DataState.Warning),
                    null => new SecurityItem("BitLocker", "nicht verfügbar", DataState.NotAvailable),
                    _ => new SecurityItem("BitLocker", $"Status-Code {code}", DataState.NotAvailable, "Details mit Adminrechten sichtbar."),
                };
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException)
        {
        }
        return new SecurityItem("BitLocker", "Adminrechte erforderlich", DataState.RequiresAdmin);
    }

    private static SecurityItem GetUac()
    {
        using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
        var lua = k?.GetValue("EnableLUA");
        var consent = k?.GetValue("ConsentPromptBehaviorAdmin");
        if (lua is 0) return new SecurityItem("Benutzerkontensteuerung (UAC)", "Deaktiviert", DataState.Problem, "UAC ist ein zentraler Schutzmechanismus.");
        if (consent is 0) return new SecurityItem("Benutzerkontensteuerung (UAC)", "Aktiv, aber ohne Rückfrage für Admins", DataState.Warning);
        return lua is 1
            ? new SecurityItem("Benutzerkontensteuerung (UAC)", "Aktiv", DataState.Ok)
            : new SecurityItem("Benutzerkontensteuerung (UAC)", "nicht ermittelbar", DataState.NotAvailable);
    }

    private static SecurityItem GetSecurityEvents()
    {
        var events = AppLog.ReadRecent(7, LogCategory.Security);
        var failures = events.Count(e => e.Action is "AuthFailure" or "IntegrityFailure" or "SignatureFailure");
        return new SecurityItem("Security-Ereignisse (7 Tage)", $"{events.Count} Ereignisse, {failures} Fehler",
            failures > 0 ? DataState.Warning : DataState.Ok);
    }
}
