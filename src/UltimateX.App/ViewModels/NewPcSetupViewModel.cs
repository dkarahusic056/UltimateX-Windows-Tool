using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Services.Benchmark;
using UltimateX.Services.Common;
using UltimateX.Services.Security;
using UltimateX.Services.Setup;

namespace UltimateX.App.ViewModels;

public sealed record InstallLogRow(string Time, string Software, string Version, string Source, string Signature, string Sha256, string Exit, string Result, DataState State);

/// <summary>
/// NEW PC SETUP / PC INSTALL (v1.2 §19–§33): Prüfungen, Profile, Empfehlungen, Installation aus offiziellen Quellen
/// (winget / Microsoft Store / Office Deployment Tool), Installationsprotokoll und „SET UP THIS PC“.
/// Es wird nichts ohne Auswahl und Bestätigung installiert.
/// </summary>
public sealed partial class NewPcSetupViewModel(INavigator nav) : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "New PC Setup";
    public override string Glyph => "";
    public override string Keywords => "new pc setup install installieren software programme office firefox 7-zip vlc powertoys bitwarden winget profil neuer pc einrichten";
    public override string? Subtitle => "Frisch installierten PC sicher einrichten – nur offizielle Quellen";

    public ObservableCollection<Finding> EnvironmentChecks { get; } = [];
    public ObservableCollection<SoftwareChoice> Software { get; } = [];
    public ObservableCollection<StepRow> SetupSteps { get; } = [];
    public ObservableCollection<InstallLogRow> InstallLog { get; } = [];
    public ObservableCollection<string> RecommendationNotes { get; } = [];

    public IReadOnlyList<Choice<SetupProfile>> Profiles { get; } =
    [
        new(SetupProfile.Basic, "BASIC – Firefox, Adobe Reader, 7-Zip, VLC, PowerToys"),
        new(SetupProfile.Office, "OFFICE – Basic + Microsoft 365 Apps + Teams"),
        new(SetupProfile.Gaming, "GAMING – Basic + Steam, Epic, Discord (+ Gaming Check)"),
        new(SetupProfile.Developer, "DEVELOPER – Basic + Git, VS Code, Python, Node.js, .NET SDK, Terminal"),
        new(SetupProfile.Custom, "CUSTOM – alles selbst auswählen"),
    ];

    public IReadOnlyList<Choice<OfficeLanguage>> OfficeLanguages { get; } =
        [new(OfficeLanguage.SystemLanguage, "Systemsprache"), new(OfficeLanguage.German, "Deutsch"), new(OfficeLanguage.English, "Englisch")];

    public IReadOnlyList<string> SoftwareCategories => SearchQueries.SoftwareCategories;

    [ObservableProperty] private Choice<SetupProfile>? _selectedProfile;
    [ObservableProperty] private string? _recommendedKind;
    [ObservableProperty] private bool _installOffice;
    [ObservableProperty] private bool _officeWord = true;
    [ObservableProperty] private bool _officeExcel = true;
    [ObservableProperty] private bool _officePowerPoint = true;
    [ObservableProperty] private bool _officeOutlook = true;
    [ObservableProperty] private bool _officeOneNote;
    [ObservableProperty] private bool _officeAccess;
    [ObservableProperty] private Choice<OfficeLanguage>? _selectedOfficeLanguage;
    [ObservableProperty] private bool _includeClamAv;
    [ObservableProperty] private bool _includeBenchmark;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string? _workingText;
    [ObservableProperty] private string? _output;

    // v1.3 Grafiktreiber (NVIDIA automatisch, AMD/Intel über Herstellerseite bzw. Intel DSA)
    [ObservableProperty] private string? _gpuDriverTitle;
    [ObservableProperty] private string? _gpuDriverDetail;
    [ObservableProperty] private DataState _gpuDriverState = DataState.NotAvailable;
    [ObservableProperty] private bool _installNvidiaDriver;
    [ObservableProperty] private bool _nvidiaUpdateAvailable;
    [ObservableProperty] private bool _showAmdDriverLink;
    [ObservableProperty] private double _driverProgress;
    private NvidiaDriverCheck? _nvidia;

    private CollectedFacts? _facts;

    protected override async Task LoadAsync()
    {
        SelectedOfficeLanguage ??= OfficeLanguages[0];
        SelectedProfile ??= Profiles[^1];
        if (Software.Count == 0)
            foreach (var p in SoftwareCatalog.All) Software.Add(new SoftwareChoice(p));
        await CheckEnvironmentAsync();
        await DetectAsync();
        LoadLog();
        await CheckGpuDriverAsync();
    }

    // ---------------------------------------------------------------- Grafiktreiber (v1.3)

    /// <summary>Erkennt die Grafikkarte; bei NVIDIA wird der passende neueste Treiber bei NVIDIA gesucht und – falls neuer – vorgemerkt.</summary>
    [RelayCommand]
    private async Task CheckGpuDriverAsync()
    {
        GpuDriverTitle = "Grafikkarte wird erkannt …";
        GpuDriverDetail = null;
        GpuDriverState = DataState.NotAvailable;
        ShowAmdDriverLink = false;
        _nvidia = await AppServices.NvidiaDrivers.CheckAsync(CancellationToken.None);
        if (_nvidia.Gpu is { } g)
        {
            GpuDriverTitle = $"{g.Name} · installierter Treiber {g.DriverVersion ?? "unbekannt"}";
            var l = _nvidia.Latest;
            GpuDriverDetail = l is null ? _nvidia.Message
                : $"{_nvidia.Message}\nNeuester Game-Ready-Treiber (WHQL): {l.Version}{(l.ReleaseDate is { } d ? " vom " + d : "")}{(l.SizeText is { } sz ? " · " + sz : "")} · Quelle: NVIDIA (download.nvidia.com)";
            NvidiaUpdateAvailable = _nvidia.UpdateAvailable;
            InstallNvidiaDriver = _nvidia.UpdateAvailable; // vorgemerkt – installiert wird erst nach Bestätigung
            GpuDriverState = l is null ? DataState.Warning : _nvidia.UpdateAvailable ? DataState.Warning : DataState.Ok;
            return;
        }
        NvidiaUpdateAvailable = false;
        InstallNvidiaDriver = false;
        var gpu = _facts?.Facts.GpuName ?? AppServices.LastHardware?.Gpus.FirstOrDefault()?.Name;
        if (gpu is not null && (gpu.Contains("AMD", StringComparison.OrdinalIgnoreCase) || gpu.Contains("Radeon", StringComparison.OrdinalIgnoreCase)))
        {
            GpuDriverTitle = gpu;
            GpuDriverDetail = "AMD-Treiber: über „AMD Treiber-Download“ die automatische Erkennung von AMD (amd.com) verwenden. Windows Update liefert ebenfalls Grundtreiber.";
            ShowAmdDriverLink = true;
        }
        else if (gpu is not null && gpu.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            GpuDriverTitle = gpu;
            GpuDriverDetail = "Intel-Grafik: „Intel Driver & Support Assistant“ in der Liste auswählen – er hält Intel-Treiber aktuell.";
            foreach (var s in Software.Where(s => s.Package.Key == "inteldsa" && !s.IsInstalled)) s.IsRecommended = true;
        }
        else
        {
            GpuDriverTitle = gpu ?? "Keine Grafikkarte erkannt";
            GpuDriverDetail = _nvidia.Message;
        }
    }

    [RelayCommand]
    private void OpenAmdDrivers() => ShowResult(UriLauncher.Open(new Uri("https://www.amd.com/en/support/download/drivers.html")));

    [RelayCommand]
    private async Task InstallNvidiaDriverNowAsync()
    {
        if (IsWorking || _nvidia?.Latest is null) return;
        if (!ConfirmNvidia()) return;
        await RunWork(async ct => await InstallNvidiaAsync(ct));
    }

    private bool ConfirmNvidia()
        => _nvidia?.Latest is { } l && Dialogs.Confirm("NVIDIA-Grafiktreiber",
            $"Für {_nvidia.Gpu?.Name} wird der NVIDIA Game-Ready-Treiber {l.Version} ({l.SizeText ?? "ca. 1 GB"}) direkt von NVIDIA (download.nvidia.com) geladen.\n\n" +
            "Vor dem Start wird die digitale Signatur geprüft (Herausgeber „NVIDIA Corporation“). Danach öffnet sich der NVIDIA-Installer (UAC) – " +
            "dort „Express“ wählen. Der Bildschirm kann kurz schwarz werden. Fortfahren?");

    private async Task<bool> InstallNvidiaAsync(CancellationToken ct)
    {
        if (_nvidia?.Latest is not { } l) return false;
        WorkingText = $"NVIDIA-Treiber {l.Version} wird geladen …";
        DriverProgress = 0;
        var dl = await AppServices.NvidiaDrivers.DownloadAndVerifyAsync(l, new Progress<double>(p =>
        {
            DriverProgress = p;
            WorkingText = $"NVIDIA-Treiber {l.Version} wird geladen … {p:0} %";
        }), ct);
        AppendOutput($"NVIDIA-Treiber {l.Version}: {dl.Message} · SHA-256 {dl.Sha256} · {dl.SizeBytes} Bytes\n");
        if (!dl.Ok) { StatusMessage = dl.Message; LoadLog(); return false; }
        WorkingText = "NVIDIA-Installer läuft (Fenster beachten) …";
        var (exit, message) = await AppServices.NvidiaDrivers.InstallAsync(l, ct);
        AppendOutput(message + "\n");
        StatusMessage = message;
        LoadLog();
        if (exit == 0)
        {
            InstallNvidiaDriver = false;
            await CheckGpuDriverAsync();
        }
        return exit == 0;
    }

    [RelayCommand]
    private async Task CheckEnvironmentAsync()
    {
        var list = await AppServices.SoftwareSetup.CheckEnvironmentAsync(CancellationToken.None);
        EnvironmentChecks.Clear();
        foreach (var f in list) EnvironmentChecks.Add(f);
    }

    private async Task DetectAsync()
    {
        var installed = await Task.Run(() => AppServices.SoftwareSetup.DetectInstalled());
        foreach (var s in Software)
        {
            s.InstalledVersion = installed.TryGetValue(s.Package.Key, out var v) ? v ?? "" : null;
            s.Status = s.IsInstalled ? "vorhanden" : "–";
            s.State = s.IsInstalled ? DataState.Ok : DataState.NotAvailable;
        }
        try
        {
            _facts = await FactsCollector.CollectAsync(includeProcesses: false);
            var (kind, keys, notes) = SoftwareCatalog.Recommend(_facts.Facts, installed.Keys.ToList());
            RecommendedKind = kind;
            foreach (var s in Software) s.IsRecommended = keys.Contains(s.Package.Key);
            RecommendationNotes.Clear();
            foreach (var n in notes) RecommendationNotes.Add(n);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            RecommendedKind = "Empfehlung nicht möglich: " + ex.Message;
        }
    }

    private void LoadLog()
    {
        InstallLog.Clear();
        foreach (var e in InstallLogService.Read())
            InstallLog.Add(new InstallLogRow(Format.DateTimeText(e.Time), e.Software, e.Version ?? "–", e.Source, e.SignatureStatus,
                e.Sha256 is { Length: > 16 } h ? h[..16] + "…" : e.Sha256 ?? "–", e.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "–",
                e.Success ? "Erfolg" : "Fehler", e.Success ? DataState.Ok : DataState.Problem));
    }

    /// <summary>Profil anwenden: markiert die Pakete des Profils (bereits installierte nicht) – installiert wird erst nach Bestätigung.</summary>
    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile is null) return;
        var keys = SoftwareCatalog.ForProfile(SelectedProfile.Value).Select(p => p.Key).ToHashSet();
        foreach (var s in Software) s.IsSelected = keys.Contains(s.Package.Key) && !s.IsInstalled;
        InstallOffice = SoftwareCatalog.ProfileIncludesOffice(SelectedProfile.Value);
        StatusMessage = SelectedProfile.Value == SetupProfile.Custom
            ? "Custom: bitte die gewünschten Programme selbst auswählen."
            : $"{Software.Count(s => s.IsSelected)} Programm(e) vorgemerkt{(InstallOffice ? " + Microsoft 365 Apps" : "")}. Bereits installierte wurden nicht ausgewählt.";
    }

    [RelayCommand]
    private void SelectRecommended()
    {
        foreach (var s in Software.Where(s => s.IsRecommended && !s.IsInstalled)) s.IsSelected = true;
        if (NvidiaUpdateAvailable) InstallNvidiaDriver = true;
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var s in Software) s.IsSelected = false;
        InstallOffice = false;
        InstallNvidiaDriver = false;
    }

    private IReadOnlyCollection<OfficeApp> SelectedOfficeApps()
    {
        var list = new List<OfficeApp>();
        if (OfficeWord) list.Add(OfficeApp.Word);
        if (OfficeExcel) list.Add(OfficeApp.Excel);
        if (OfficePowerPoint) list.Add(OfficeApp.PowerPoint);
        if (OfficeOutlook) list.Add(OfficeApp.Outlook);
        if (OfficeOneNote) list.Add(OfficeApp.OneNote);
        if (OfficeAccess) list.Add(OfficeApp.Access);
        return list;
    }

    // ---------------------------------------------------------------- Installation

    [RelayCommand]
    private async Task InstallSelectedAsync()
    {
        if (IsWorking) return;
        var selected = Software.Where(s => s.IsSelected).ToList();
        var nvidia = InstallNvidiaDriver && _nvidia?.Latest is not null;
        if (selected.Count == 0 && !InstallOffice && !nvidia) { StatusMessage = "Bitte zuerst Programme auswählen."; return; }
        var list = string.Join("\n", selected.Select(s => "• " + s.Name)) + (InstallOffice ? "\n• Microsoft 365 Apps (Office Deployment Tool)" : "")
                   + (nvidia ? $"\n• NVIDIA-Grafiktreiber {_nvidia!.Latest!.Version} (download.nvidia.com, Signatur NVIDIA Corporation)" : "");
        if (!Dialogs.Confirm("Software installieren",
                "Folgende Programme werden aus den offiziellen Quellen installiert:\n\n" + list +
                "\n\nVor jeder Installation werden Herstellerdomain (HTTPS) und SHA-256 aus dem Paketmanifest geprüft; bei Abweichungen wird nichts installiert. " +
                "Installer können eine UAC-Abfrage anzeigen. Alles wird im Installationsprotokoll festgehalten.\n\nFortfahren?")) return;
        await RunWork(async ct =>
        {
            await InstallPackagesAsync(selected, ct);
            if (InstallOffice) await InstallOfficeAsync(ct);
            if (nvidia) await InstallNvidiaAsync(ct);
        });
    }

    private async Task InstallPackagesAsync(IEnumerable<SoftwareChoice> choices, CancellationToken ct)
    {
        foreach (var s in choices)
        {
            ct.ThrowIfCancellationRequested();
            s.Status = "Quelle wird geprüft …";
            s.State = DataState.NotAvailable;
            WorkingText = $"{s.Name}: Quelle wird geprüft …";
            var outcome = await AppServices.SoftwareSetup.InstallAsync(s.Package, new Progress<string>(AppendOutput), ct);
            s.Status = outcome.Message;
            s.State = outcome.Success ? DataState.Ok : DataState.Problem;
            if (outcome.Success)
            {
                s.IsSelected = false;
                s.InstalledVersion ??= outcome.Validation?.Version ?? "";
            }
            LoadLog();
        }
    }

    private async Task<bool> InstallOfficeAsync(CancellationToken ct)
    {
        var apps = SelectedOfficeApps();
        if (apps.Count == 0) { StatusMessage = "Microsoft 365: keine App ausgewählt."; return false; }
        WorkingText = "Office Deployment Tool wird von officecdn.microsoft.com geladen …";
        var dl = await AppServices.Office.DownloadAndVerifyAsync(ct);
        AppendOutput($"Office Deployment Tool: {dl.Message} · SHA-256 {dl.Sha256} · {dl.SizeBytes} Bytes\n");
        if (!dl.Ok) { StatusMessage = dl.Message; LoadLog(); return false; }
        WorkingText = "Microsoft 365 Apps werden installiert (Office-Setup-Fenster beachten) …";
        var (exit, message) = await AppServices.Office.InstallAsync(apps, SelectedOfficeLanguage?.Value ?? OfficeLanguage.SystemLanguage, ct);
        AppendOutput(message + "\n");
        StatusMessage = message;
        LoadLog();
        return exit == 0;
    }

    /// <summary>Firefox als Standardbrowser: Windows erlaubt das nur über die Einstellungen – ausschließlich nach Zustimmung.</summary>
    [RelayCommand]
    private void SetFirefoxDefault()
    {
        if (!Dialogs.Confirm("Standardbrowser", "Windows lässt den Standardbrowser nur in den Einstellungen ändern. Soll die Seite „Standard-Apps“ geöffnet werden, um Firefox auszuwählen?")) return;
        ShowResult(UriLauncher.OpenSettings("defaultapps"));
    }

    [RelayCommand]
    private void OpenSoftwareSearch(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;
        var hw = AppServices.LastHardware;
        var q = SearchQueries.SoftwareQuery(category, hw?.Cpu?.Name, _facts?.Facts.GpuName ?? hw?.Gpus.FirstOrDefault()?.Name,
            hw?.Memory.TotalBytes is { } t ? t / 1024.0 / 1024 / 1024 : null, hw?.Os.Caption);
        ShowResult(UriLauncher.Open(SearchQueries.SearchUrl(q)));
    }

    // ---------------------------------------------------------------- ONE CLICK NEW PC SETUP (§33)

    private static readonly string[] StepNames =
    [
        "Hardware Scan", "Windows Health Check", "Windows Update", "Driver Check", "Grafiktreiber", "BIOS/UEFI Check", "Restore Point erstellen",
        "Ausgewählte Software installieren", "Office installieren", "Browser installieren", "PDF Reader installieren", "Security Check",
        "Defender prüfen", "ClamAV (optional)", "RAM Quick Test", "SSD Health", "Network Test", "Complete Benchmark (optional)",
        "Gaming Check", "Final System Scan", "PDF Report",
    ];

    [RelayCommand]
    private async Task SetUpThisPcAsync()
    {
        if (IsWorking) return;
        var sel = Software.Where(s => s.IsSelected).ToList();
        if (!Dialogs.Confirm("SET UP THIS PC",
                "Der Ablauf prüft Hardware, Windows, Updates, Treiber und BIOS" + (InstallNvidiaDriver && _nvidia?.Latest is not null ? $", installiert den NVIDIA-Treiber {_nvidia.Latest.Version}" : "") + ", erstellt einen Wiederherstellungspunkt (UAC), installiert die ausgewählte Software" +
                (InstallOffice ? " und Microsoft 365" : "") + (IncludeClamAv ? " sowie ClamAV" : "") +
                ", prüft Sicherheit, RAM (Schnelltest), Laufwerke und Netzwerk" + (IncludeBenchmark ? ", führt den Complete Benchmark aus" : "") +
                " und speichert am Ende einen PDF-Bericht unter „Dokumente\\ULTIMATE X“.\n\n" +
                $"Ausgewählte Programme: {(sel.Count == 0 ? "keine" : string.Join(", ", sel.Select(s => s.Name)))}\n\n" +
                "Windows-Updates und Treiber werden nur gesucht – installiert wird über die Seite „Updates“ mit Ihrer Bestätigung. Starten?")) return;

        SetupSteps.Clear();
        foreach (var n in StepNames) SetupSteps.Add(new StepRow(n));
        await RunWork(async ct =>
        {
            async Task Step(string name, Func<Task<(CheckStatus, string)>> work)
            {
                var row = SetupSteps.First(s => s.Name == name);
                ct.ThrowIfCancellationRequested();
                row.IsRunning = true;
                WorkingText = name + " …";
                try
                {
                    var (st, detail) = await work();
                    row.Status = st;
                    row.Detail = detail;
                }
                catch (OperationCanceledException) { row.Detail = "Abgebrochen"; throw; }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or HttpRequestException
                                               or TimeoutException or System.Runtime.InteropServices.COMException)
                {
                    row.Status = CheckStatus.NotAvailable;
                    row.Detail = "Nicht möglich: " + ex.Message;
                }
                finally { row.IsRunning = false; }
            }

            Services.Hardware.HardwareSnapshot? hw = null;
            await Step("Hardware Scan", async () =>
            {
                hw = await Task.Run(() => AppServices.Hardware.Collect(), ct);
                AppServices.LastHardware = hw;
                return (hw.Warnings.Count > 0 ? CheckStatus.Warning : CheckStatus.Pass, $"{hw.Cpu?.Name} · {Format.Bytes(hw.Memory.TotalBytes)} RAM · {hw.Gpus.FirstOrDefault()?.Name}");
            });
            await Step("Windows Health Check", async () =>
            {
                var reboot = await Task.Run(() => AppServices.Health.GetRebootStatus(), ct);
                var services = await Task.Run(() => AppServices.Health.GetServiceStatus(), ct);
                var crashes = await Task.Run(() => AppServices.Health.GetCrashSummary(), ct);
                var bad = services.Count(s => s.Health == DataState.Problem);
                var st = bad > 0 || crashes.BugChecks > 0 ? CheckStatus.Fail : reboot.RebootRequired ? CheckStatus.Warning : CheckStatus.Pass;
                return (st, $"{bad} Dienstproblem(e) · {crashes.BugChecks} Bluescreen(s) in 30 Tagen · Neustart {(reboot.RebootRequired ? "ausstehend" : "nicht nötig")}");
            });
            await Step("Windows Update", async () =>
            {
                var (items, error) = await AppServices.WindowsUpdate.SearchAsync(false, ct);
                return error is not null ? (CheckStatus.NotAvailable, error)
                    : items.Count == 0 ? (CheckStatus.Pass, "Keine ausstehenden Windows-Updates")
                    : (CheckStatus.Warning, $"{items.Count} Update(s) verfügbar – Installation unter „Updates“");
            });
            await Step("Driver Check", async () =>
            {
                var (items, error) = await AppServices.WindowsUpdate.SearchAsync(true, ct);
                if (error is null) UpdatesViewModel.SetDriverUpdates(items);
                var (devices, _) = await Task.Run(() => AppServices.Inventory.GetDevices(), ct);
                var problems = devices.Count(d => d.ErrorCode is > 0);
                return error is not null ? (CheckStatus.NotAvailable, error)
                    : (problems > 0 ? CheckStatus.Warning : items.Count > 0 ? CheckStatus.Warning : CheckStatus.Pass,
                        $"{items.Count} Treiber-Update(s) über Windows Update · {problems} Gerät(e) mit Fehlercode");
            });
            await Step("Grafiktreiber", async () =>
            {
                if (_nvidia?.Gpu is null) return (CheckStatus.Pass, GpuDriverTitle ?? "Keine NVIDIA-Grafikkarte");
                if (!_nvidia.UpdateAvailable) return (_nvidia.Latest is null ? CheckStatus.NotAvailable : CheckStatus.Pass, _nvidia.Message);
                if (!InstallNvidiaDriver) return (CheckStatus.Warning, $"Neuer Treiber {_nvidia.Latest?.Version} verfügbar – nicht ausgewählt");
                return await InstallNvidiaAsync(ct) ? (CheckStatus.Pass, $"NVIDIA-Treiber {_nvidia.Latest?.Version} installiert")
                    : (CheckStatus.Warning, StatusMessage ?? "Nicht installiert");
            });
            await Step("BIOS/UEFI Check", async () =>
            {
                var f = await Task.Run(() => AppServices.SystemInfo.GetFirmwareInfo(AppServices.Hardware), ct);
                var ok = f.FirmwareType.Contains("UEFI", StringComparison.OrdinalIgnoreCase) && f.SecureBootState == DataState.Ok && f.TpmState == DataState.Ok;
                return (ok ? CheckStatus.Pass : CheckStatus.Warning, $"{f.FirmwareType} · Secure Boot: {f.SecureBoot} · TPM: {f.Tpm} · BIOS {f.BiosVersion}");
            });
            await Step("Restore Point erstellen", async () =>
            {
                var r = await AppServices.RestorePoints.CreateAsync(ct);
                return (r.Success ? CheckStatus.Pass : CheckStatus.Warning, r.Message);
            });
            var browsers = new[] { "firefox", "chrome", "brave" };
            var others = sel.Where(s => !browsers.Contains(s.Package.Key) && s.Package.Key != "acrobat").ToList();
            await Step("Ausgewählte Software installieren", async () =>
            {
                if (others.Count == 0) return (CheckStatus.Pass, "Keine Auswahl");
                await InstallPackagesAsync(others, ct);
                var failed = others.Count(o => o.State == DataState.Problem);
                return (failed == 0 ? CheckStatus.Pass : CheckStatus.Warning, $"{others.Count - failed} von {others.Count} installiert");
            });
            await Step("Office installieren", async () =>
                !InstallOffice ? (CheckStatus.Pass, "Nicht ausgewählt") : await InstallOfficeAsync(ct) ? (CheckStatus.Pass, "Microsoft 365 Apps installiert") : (CheckStatus.Warning, StatusMessage ?? "Nicht installiert"));
            await Step("Browser installieren", async () =>
            {
                var b = sel.Where(s => browsers.Contains(s.Package.Key)).ToList();
                if (b.Count == 0) return (CheckStatus.Pass, "Nicht ausgewählt");
                await InstallPackagesAsync(b, ct);
                return (b.All(x => x.State == DataState.Ok) ? CheckStatus.Pass : CheckStatus.Warning, string.Join(", ", b.Select(x => $"{x.Name}: {x.Status}")));
            });
            await Step("PDF Reader installieren", async () =>
            {
                var a = sel.Where(s => s.Package.Key == "acrobat").ToList();
                if (a.Count == 0) return (CheckStatus.Pass, "Nicht ausgewählt");
                await InstallPackagesAsync(a, ct);
                return (a[0].State == DataState.Ok ? CheckStatus.Pass : CheckStatus.Warning, a[0].Status);
            });
            IReadOnlyList<Services.Security.SecurityItem> security = [];
            await Step("Security Check", async () =>
            {
                security = await Task.Run(() => AppServices.Security.GetStatus(), ct);
                var p = security.Count(s => s.State == DataState.Problem && s.Area != "Application Integrity");
                var w = security.Count(s => s.State == DataState.Warning);
                return (p > 0 ? CheckStatus.Fail : w > 0 ? CheckStatus.Warning : CheckStatus.Pass, $"{p} Problem(e), {w} Hinweis(e)");
            });
            await Step("Defender prüfen", () =>
            {
                var av = security.FirstOrDefault(s => s.Area.Contains("Antivirus", StringComparison.OrdinalIgnoreCase) || s.Area.Contains("Defender", StringComparison.OrdinalIgnoreCase));
                return Task.FromResult(av is null ? (CheckStatus.NotAvailable, "N/A")
                    : (av.State == DataState.Ok ? CheckStatus.Pass : av.State == DataState.Problem ? CheckStatus.Fail : CheckStatus.Warning, av.Status));
            });
            await Step("ClamAV (optional)", async () =>
            {
                if (!IncludeClamAv) return (CheckStatus.Pass, "Nicht ausgewählt");
                var check = await AppServices.ClamAv.CheckProjectAsync(ct);
                if (check.ActivelyMaintained != true) return (CheckStatus.Warning, "Projektprüfung nicht bestanden – ClamAV wird nicht installiert.");
                if (!ClamAvService.IsInstalled)
                {
                    var o = await AppServices.SoftwareSetup.InstallAsync(SoftwareCatalog.ClamAv, new Progress<string>(AppendOutput), ct);
                    if (!o.Success) return (CheckStatus.Warning, o.Message);
                }
                var (ok, _) = await AppServices.ClamAv.UpdateSignaturesAsync(new Progress<string>(AppendOutput), ct);
                return (ok ? CheckStatus.Pass : CheckStatus.Warning, ok ? $"ClamAV {check.LatestVersion} bereit, Signaturen aktuell (Defender bleibt aktiv)" : "Signatur-Update fehlgeschlagen");
            });
            await Step("RAM Quick Test", async () =>
            {
                var r = await Task.Run(() => AppServices.MemoryTest.Run(Core.Analysis.MemTestMode.Quick, null, ct), ct);
                return (r.Status, $"{r.ErrorCount} Fehler · {Format.Bytes(r.TestedBytes)} getestet");
            });
            await Step("SSD Health", async () =>
            {
                var drives = await Task.Run(() => AppServices.DriveHealth.GetDrives(Services.Hardware.SmartService.LoadCached()), ct);
                var worst = Core.Analysis.DriveHealthEvaluator.Worst(drives.Select(d => d.Verdict.Verdict));
                return (worst switch
                {
                    Core.Analysis.DriveVerdict.Good => CheckStatus.Pass,
                    Core.Analysis.DriveVerdict.Warning => CheckStatus.Warning,
                    Core.Analysis.DriveVerdict.Critical => CheckStatus.Fail,
                    _ => CheckStatus.NotAvailable,
                }, string.Join(" · ", drives.Select(d => $"{d.Name}: {d.Verdict.Label}")));
            });
            await Step("Network Test", async () =>
            {
                var r = await AppServices.NetworkBenchmark.RunAsync(false, null, ct);
                return (ScoreMath.StatusOf(r.Health.Score), $"Network Health Score {r.Health.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}");
            });
            await Step("Complete Benchmark (optional)", async () =>
            {
                if (!IncludeBenchmark) return (CheckStatus.Pass, "Nicht ausgewählt");
                var r = await AppServices.CompleteBenchmark.RunAsync(new CompleteBenchmarkOptions(false, false), null, ct);
                return (CheckStatus.Pass, $"{r.Performance.Total?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}/100 · {r.Performance.TierText}");
            });
            await Step("Gaming Check", async () =>
            {
                if (SelectedProfile?.Value != SetupProfile.Gaming) return (CheckStatus.Pass, "Nur im Profil GAMING");
                var facts = await FactsCollector.CollectAsync();
                var g = GamingCheck.Evaluate(facts.Facts);
                return (ScoreMath.StatusOf(g.Score, 75, 50), $"GAMING SCORE {g.Score?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "N/A"}/100");
            });
            await Step("Final System Scan", async () =>
            {
                var reboot = await Task.Run(() => AppServices.Health.GetRebootStatus(), ct);
                var sec = await Task.Run(() => AppServices.Security.GetStatus(), ct);
                var p = sec.Count(s => s.State == DataState.Problem && s.Area != "Application Integrity");
                return (p > 0 ? CheckStatus.Fail : reboot.RebootRequired ? CheckStatus.Warning : CheckStatus.Pass,
                    reboot.RebootRequired ? "Neustart empfohlen, um Installationen abzuschließen" : "System bereit");
            });
            await Step("PDF Report", async () =>
            {
                var path = await nav.Page<ReportViewModel>().ExportToDocumentsAsync();
                return path is null ? (CheckStatus.Warning, "Bericht konnte nicht erstellt werden") : (CheckStatus.Pass, path);
            });
        });
    }

    private async Task RunWork(Func<CancellationToken, Task> work)
    {
        IsWorking = true;
        StatusMessage = null;
        Output = null;
        _cts = new CancellationTokenSource();
        try
        {
            await work(_cts.Token);
            StatusMessage ??= "Fertig. Details im Installationsprotokoll.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Abgebrochen.";
        }
        finally
        {
            foreach (var s in SetupSteps) s.IsRunning = false;
            IsWorking = false;
            WorkingText = null;
            _cts.Dispose();
            _cts = null;
            LoadLog();
        }
    }

    private void AppendOutput(string text)
    {
        var o = (Output ?? "") + text;
        Output = o.Length > 20_000 ? o[^20_000..] : o;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void OpenUpdates() => nav.Navigate(nav.Page<UpdatesViewModel>());

    [RelayCommand]
    private void OpenOpenSourceSecurity() => nav.Navigate(nav.Page<OpenSourceSecurityViewModel>());
}
