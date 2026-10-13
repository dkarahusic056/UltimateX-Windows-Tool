using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Health;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

/// <summary>Windows Health &amp; Repair Center (§39–§48, §51, §87).</summary>
public sealed partial class HealthViewModel : PageViewModel
{
    public override string Title => "Windows Health";
    public override string Glyph => "\uE90F";
    public override string Keywords => "windows health repair reparatur sfc dism chkdsk systemdateien datenträger dienste services ereignisse events bluescreen bsod absturz crash speicherdiagnose neustart";

    public ObservableCollection<ServiceStatus> Services { get; } = [];
    public ObservableCollection<EventProblem> Problems { get; } = [];
    public ObservableCollection<string> Drives { get; } = [];

    [ObservableProperty] private RebootStatus? _reboot;
    [ObservableProperty] private string? _activeHours;
    [ObservableProperty] private CrashSummary? _crashes;
    [ObservableProperty] private string _selectedDrive = "C";

    [ObservableProperty] private bool _isRepairRunning;
    [ObservableProperty] private string? _repairTitle;
    [ObservableProperty] private string? _repairSummary;
    [ObservableProperty] private DataState _repairState = DataState.NotAvailable;
    [ObservableProperty] private string _repairOutput = string.Empty;
    [ObservableProperty] private bool _showTechnicalOutput;

    private CancellationTokenSource? _cts;

    public string RebootText => Reboot is null ? "–" : Reboot.RebootRequired ? "Neustart erforderlich" : "Kein Neustart erforderlich";

    partial void OnRebootChanged(RebootStatus? value) => OnPropertyChanged(nameof(RebootText));

    [RelayCommand]
    private void SelectDrive(string? drive)
    {
        if (drive is not null && Drives.Contains(drive)) SelectedDrive = drive;
    }

    protected override async Task LoadAsync()
    {
        var rebootTask = Task.Run(() => AppServices.Health.GetRebootStatus());
        var svcTask = Task.Run(() => AppServices.Health.GetServiceStatus());
        var problemsTask = Task.Run(() => AppServices.Health.GetTopProblems());
        var crashTask = Task.Run(() => AppServices.Health.GetCrashSummary());

        Reboot = await rebootTask;
        var (start, end) = AppServices.Health.GetActiveHours();
        ActiveHours = start is not null && end is not null ? $"Nutzungszeit: {start}:00 – {end}:00 Uhr" : "Nutzungszeit: nicht konfiguriert";

        Services.Clear();
        foreach (var s in await svcTask) Services.Add(s);

        Drives.Clear();
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed)) Drives.Add(d.Name[..1]);
        if (Drives.Count > 0 && !Drives.Contains(SelectedDrive)) SelectedDrive = Drives[0];

        Problems.Clear();
        foreach (var p in await problemsTask) Problems.Add(p);
        Crashes = await crashTask;
    }

    private async Task RunRepairAsync(AllowedCommand command, string? parameter, string title, string explanation)
    {
        if (IsRepairRunning) return;

        RepairResult? result = null;
        var ok = await AppServices.Guard.RunAsync(title, explanation, async () =>
        {
            IsRepairRunning = true;
            RepairTitle = title;
            RepairSummary = "Läuft … Windows fragt gleich nach Administratorrechten (UAC).";
            RepairState = DataState.NotAvailable;
            RepairOutput = string.Empty;
            _cts = new CancellationTokenSource();
            var sb = new StringBuilder();
            var progress = new Progress<string>(chunk =>
            {
                sb.Append(chunk);
                if (sb.Length > 200_000) sb.Remove(0, sb.Length - 200_000);
                RepairOutput = sb.ToString();
            });
            try
            {
                // Größere Systemreparaturen: vorher Wiederherstellungspunkt (§86), wenn in den Einstellungen aktiv.
                if (AppServices.Settings.AutoRestorePoint && command is AllowedCommand.DismRestoreHealth or AllowedCommand.ChkdskSpotFix)
                {
                    RepairSummary = "Wiederherstellungspunkt wird angefordert …";
                    var rp = await AppServices.RestorePoints.CreateAsync();
                    sb.AppendLine(rp.Message);
                }
                result = await AppServices.Health.RunAsync(command, parameter, progress, _cts.Token);
                if (result.Outcome is not (RepairOutcome.Cancelled or RepairOutcome.CouldNotRun))
                    AppServices.Notifications.Notify(NotificationKind.RepairCompleted, "Windows Repair abgeschlossen", $"{title}: {result.Summary}");
                return result.Outcome is not (RepairOutcome.CouldNotRun or RepairOutcome.Cancelled);
            }
            finally
            {
                IsRepairRunning = false;
                _cts.Dispose();
                _cts = null;
            }
        }, warning: command is AllowedCommand.DismRestoreHealth or AllowedCommand.ChkdskSpotFix or AllowedCommand.ScheduleBootDiskCheck);

        if (result is not null)
        {
            RepairSummary = result.Summary;
            RepairState = result.State;
            if (result.RawOutput.Length > 0) RepairOutput = result.RawOutput;
        }
        else if (!ok)
        {
            RepairSummary = "Nicht ausgeführt.";
        }
    }

    [RelayCommand]
    private Task SfcAsync() => RunRepairAsync(AllowedCommand.SfcScan, null, "Systemdateien prüfen (SFC)",
        "SFC prüft alle geschützten Windows-Systemdateien und ersetzt beschädigte Dateien aus dem Komponentenspeicher.\nDauer: ca. 5–20 Minuten. Erfordert Administratorrechte (UAC).");

    [RelayCommand]
    private Task DismCheckAsync() => RunRepairAsync(AllowedCommand.DismCheckHealth, null, "DISM Quick Check",
        "Schnelle Prüfung, ob der Windows-Komponentenspeicher als beschädigt markiert ist. Nimmt keine Änderungen vor.");

    [RelayCommand]
    private Task DismScanAsync() => RunRepairAsync(AllowedCommand.DismScanHealth, null, "DISM Deep Scan",
        "Gründliche Prüfung des Komponentenspeichers (ca. 5–15 Minuten). Nimmt keine Änderungen vor.");

    [RelayCommand]
    private Task DismRepairAsync() => RunRepairAsync(AllowedCommand.DismRestoreHealth, null, "Windows Repair (DISM RestoreHealth)",
        "Repariert den Komponentenspeicher mit Originaldateien von Windows Update. Benötigt Internet und kann 10–30 Minuten dauern.\nEmpfehlung: Vorher einen Wiederherstellungspunkt erstellen.");

    /// <summary>Reparatur-Workflow (§42): DISM prüfen → ggf. reparieren → SFC → Ergebnis. Jeder Schritt mit eigener Bestätigung.</summary>
    [RelayCommand]
    private async Task RepairWorkflowAsync()
    {
        await DismScanAsync();
        if (RepairState == DataState.Warning || RepairSummary?.Contains("repariert werden", StringComparison.OrdinalIgnoreCase) == true)
            await DismRepairAsync();
        await SfcAsync();
    }

    [RelayCommand]
    private Task ChkdskScanAsync() => RunRepairAsync(AllowedCommand.ChkdskScan, SelectedDrive, $"Datenträger {SelectedDrive}: prüfen",
        $"CHKDSK prüft das Dateisystem von {SelectedDrive}: online (nur lesend, keine Reparatur).");

    [RelayCommand]
    private Task ChkdskFixAsync()
    {
        var isSystem = string.Equals(SelectedDrive + ":", Environment.GetEnvironmentVariable("SystemDrive"), StringComparison.OrdinalIgnoreCase);
        return isSystem
            ? RunRepairAsync(AllowedCommand.ScheduleBootDiskCheck, SelectedDrive, $"Reparatur von {SelectedDrive}: beim Neustart planen",
                $"{SelectedDrive}: ist das Systemlaufwerk und kann nur beim nächsten Start repariert werden.\nWindows prüft und repariert das Laufwerk dann automatisch vor der Anmeldung.")
            : RunRepairAsync(AllowedCommand.ChkdskSpotFix, SelectedDrive, $"Datenträger {SelectedDrive}: reparieren",
                $"CHKDSK /spotfix repariert gefundene Fehler auf {SelectedDrive}:. Das Laufwerk kann dabei kurz nicht verfügbar sein. Bitte offene Dateien auf diesem Laufwerk vorher schließen.");
    }

    [RelayCommand]
    private Task MemoryDiagnosticAsync() => RunRepairAsync(AllowedCommand.MemoryDiagnostic, null, "Windows-Speicherdiagnose",
        "Die Windows-Speicherdiagnose testet den Arbeitsspeicher. Dafür ist ein NEUSTART nötig – Sie wählen im folgenden Windows-Dialog, ob sofort oder beim nächsten Start.\nBitte vorher alle Arbeiten speichern.");

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    public string RestorePointState => RestorePointService.IsSystemRestoreEnabled() switch
    {
        true => "Computerschutz ist aktiv.",
        false => "Computerschutz ist deaktiviert – Wiederherstellungspunkte sind nicht möglich.",
        _ => "Status des Computerschutzes nicht ermittelbar.",
    };

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        await AppServices.Guard.RunAsync("Wiederherstellungspunkt erstellen",
            "Windows legt einen Wiederherstellungspunkt an, mit dem Systemdateien, Treiber und Registry auf den jetzigen Stand zurückgesetzt werden können. Persönliche Dateien sind nicht betroffen.",
            async () =>
            {
                var r = await AppServices.RestorePoints.CreateAsync();
                StatusMessage = r.Message;
                return r.Success;
            }, warning: false);
    }

    [RelayCommand]
    private void OpenSystemRestore() => ShowResult(AppServices.Tools.Launch(SystemTool.SystemRestore));

    [RelayCommand]
    private void OpenWindowsUpdate() => ShowResult(AppServices.Tools.Launch(SystemTool.WindowsUpdate));
}
