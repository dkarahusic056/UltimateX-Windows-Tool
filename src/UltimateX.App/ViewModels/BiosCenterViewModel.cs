using System.Collections.ObjectModel;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Services.Common;

namespace UltimateX.App.ViewModels;

/// <summary>
/// BIOS / UEFI CENTER (v1.2 §35): zeigt und erklärt Firmware-, Virtualisierungs- und Sicherheitseinstellungen.
/// ULTIMATE X ändert NIE BIOS-/UEFI-Einstellungen.
/// </summary>
public sealed partial class BiosCenterViewModel : PageViewModel
{
    public override string Title => "BIOS / UEFI";
    public override string Glyph => "";
    public override string Keywords => "bios uefi firmware secure boot tpm virtualisierung vt-x amd-v resizable bar speicherintegrität memory integrity vbs hyper-v";
    public override string? Subtitle => "Firmware und Sicherheitsfunktionen – nur anzeigen und erklären";

    public ObservableCollection<Finding> Findings { get; } = [];

    protected override async Task LoadAsync()
    {
        var f = await Task.Run(() => AppServices.SystemInfo.GetFirmwareInfo(AppServices.Hardware));
        Findings.Clear();
        Findings.Add(new Finding("BIOS-Version", f.BiosVersion is null ? CheckStatus.NotAvailable : CheckStatus.Pass,
            $"{Format.OrNa(f.BiosVendor)} {f.BiosVersion}".Trim(), "Updates nur von der Supportseite des Mainboard-/PC-Herstellers und nach dessen Anleitung."));
        var age = f.BiosDate is { } d ? (DateTime.Now - d).TotalDays / 365 : (double?)null;
        Findings.Add(new Finding("BIOS-Datum", f.BiosDate is null ? CheckStatus.NotAvailable : age > 4 ? CheckStatus.Warning : CheckStatus.Pass,
            Format.DateText(f.BiosDate), age > 4 ? "Ältere Firmware: Beim Hersteller nach Sicherheits- und Stabilitätsupdates sehen." : null));
        Findings.Add(new Finding("UEFI / Legacy", f.FirmwareType.Contains("UEFI", StringComparison.OrdinalIgnoreCase) ? CheckStatus.Pass : CheckStatus.Warning,
            f.FirmwareType, "Windows 11, Secure Boot und moderne Sicherheitsfunktionen setzen UEFI voraus."));
        Findings.Add(new Finding("Secure Boot", f.SecureBootState switch
        {
            Core.Common.DataState.Ok => CheckStatus.Pass,
            Core.Common.DataState.NotAvailable or Core.Common.DataState.RequiresAdmin => CheckStatus.NotAvailable,
            _ => CheckStatus.Warning,
        }, f.SecureBoot, "Schützt den Startvorgang vor manipulierten Bootloadern. Wird im UEFI-Setup aktiviert."));
        Findings.Add(new Finding("TPM", f.TpmState switch
        {
            Core.Common.DataState.Ok => CheckStatus.Pass,
            Core.Common.DataState.NotAvailable or Core.Common.DataState.RequiresAdmin => CheckStatus.NotAvailable,
            _ => CheckStatus.Warning,
        }, f.Tpm, "TPM 2.0 ist Voraussetzung für Windows 11, BitLocker und einige Anti-Cheat-Systeme (Intel PTT / AMD fTPM im UEFI)."));
        Findings.Add(new Finding("Virtualization", f.VirtualizationFirmwareEnabled switch
        {
            true => CheckStatus.Pass,
            false when f.HypervisorPresent == true => CheckStatus.Pass,
            false => CheckStatus.Warning,
            _ => f.HypervisorPresent == true ? CheckStatus.Pass : CheckStatus.NotAvailable,
        }, f.VirtualizationFirmwareEnabled switch
        {
            true => "Im UEFI aktiviert",
            false when f.HypervisorPresent == true => "Aktiv (von Windows-Hypervisor genutzt)",
            false => "Im UEFI deaktiviert",
            _ => f.HypervisorPresent == true ? "Aktiv (Hypervisor läuft)" : "N/A",
        }, "Intel VT-x / AMD-V (SVM) – nötig für WSL2, Hyper-V, Sandbox und Speicherintegrität."));
        Findings.Add(new Finding("Resizable BAR", CheckStatus.NotAvailable, "N/A",
            "Unter Windows ohne Herstellerwerkzeug nicht zuverlässig auslesbar. Prüfen im NVIDIA-Kontrollzentrum (Systeminformationen) bzw. AMD Software; aktivieren im UEFI („Above 4G Decoding“ + „Resizable BAR“)."));
        Findings.Add(new Finding("Memory Integrity", f.MemoryIntegrityRunning switch
        {
            true => CheckStatus.Pass,
            false when f.MemoryIntegrityEnabled == true => CheckStatus.Warning,
            false => CheckStatus.Warning,
            _ => CheckStatus.NotAvailable,
        }, f.MemoryIntegrityRunning switch
        {
            true => "Aktiv (HVCI läuft)",
            false when f.MemoryIntegrityEnabled == true => "Eingeschaltet, aber nicht aktiv (Neustart?)",
            false => "Aus",
            _ => "N/A",
        }, "Kernisolierung / Speicherintegrität schützt Kerntreiber. Einstellung: Windows-Sicherheit → Gerätesicherheit → Kernisolierung."));
        Findings.Add(new Finding("VBS", f.VbsStatus switch
        {
            2 => CheckStatus.Pass,
            1 => CheckStatus.Warning,
            0 => CheckStatus.Warning,
            _ => CheckStatus.NotAvailable,
        }, f.VbsStatus switch { 2 => "Läuft", 1 => "Aktiviert, läuft nicht", 0 => "Aus", _ => "N/A" },
            "Virtualisierungsbasierte Sicherheit. Kann in wenigen Spielen einige Prozent Leistung kosten – Sicherheit hat Vorrang."));
        Findings.Add(new Finding("Hyper-V", f.HyperVFeatureEnabled switch { true => CheckStatus.Pass, false => CheckStatus.Pass, _ => CheckStatus.NotAvailable },
            f.HyperVFeatureEnabled switch { true => "Feature installiert", false => "Feature nicht installiert", _ => "N/A" }
            + (f.HypervisorPresent == true ? " · Windows-Hypervisor aktiv" : ""),
            "Nur nötig für virtuelle Maschinen mit Hyper-V. Keine Bewertung – reine Information."));
    }
}
