using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.Network;

namespace UltimateX.App.ViewModels;

public sealed record SubnetChoice(ScanSubnet Subnet)
{
    public override string ToString() => $"{Subnet.AdapterName} · {Subnet.Cidr}{(Subnet.Truncated ? " (begrenzt)" : "")}";
}

public sealed partial class LanDeviceRow(LanDevice device) : ObservableObject
{
    public LanDevice Device { get; } = device;
    public string Ip => Device.Ip.ToString();
    public string Mac => Device.Mac ?? "–";
    public string Vendor => Device.Vendor ?? (Device.Mac is null ? "–" : "unbekannt");
    public string Name => Device.HostName ?? "–";
    public string Ping => Device.PingMs is { } p ? $"{p} ms" : "blockiert";
    public bool IsNew => Device.IsNew;
    public bool IsSpecial => Device.IsGateway || Device.IsSelf;

    [ObservableProperty] private string _type = device.TypeGuess;
    [ObservableProperty] private string _label = device.Label ?? "";
    [ObservableProperty] private string? _ports;
    [ObservableProperty] private DataState _portState = DataState.NotAvailable;
    [ObservableProperty] private bool _isChecking;
}

/// <summary>Netzwerk-Scanner (§103): Geräte im eigenen Heimnetz finden, Hersteller/Typ erkennen, neue Geräte melden.</summary>
public sealed partial class NetworkScannerViewModel : PageViewModel
{
    private CancellationTokenSource? _cts;

    public override string Title => "Netzwerk-Scanner";
    public override string Glyph => "";
    public override string Keywords => "netzwerk scanner geräte heimnetz lan wlan ip mac router drucker ports scan wer ist im netz";
    public override string? Subtitle => "Welche Geräte sind in meinem Heimnetz?";

    public ObservableCollection<SubnetChoice> Subnets { get; } = [];
    public ObservableCollection<LanDeviceRow> Devices { get; } = [];

    [ObservableProperty] private SubnetChoice? _selectedSubnet;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanPercent;
    [ObservableProperty] private string? _scanSummary;
    [ObservableProperty] private bool _hasVendorList = NetworkScannerService.HasVendorList;
    [ObservableProperty] private LanDeviceRow? _selectedDevice;

    // ---------------- Ping / Live Ping / Quick Ping (v1.1 Phase 3) ----------------
    private CancellationTokenSource? _pingCts;
    private readonly List<PingReplyInfo> _replies = [];

    public IReadOnlyList<string> PingModes { get; } = ["1×", "4×", "10×", "Dauerhaft"];
    public ObservableCollection<string> PingLog { get; } = [];
    [ObservableProperty] private string _pingMode = "4×";
    [ObservableProperty] private string _quickPingInput = "";
    [ObservableProperty] private string? _pingTarget;
    [ObservableProperty] private bool _isPinging;
    [ObservableProperty] private IReadOnlyList<PingReplyInfo>? _pingReplies;
    [ObservableProperty] private PingEvaluation? _pingResult;
    [ObservableProperty] private LanDeviceRow? _detailDevice;
    [ObservableProperty] private string? _detailText;

    protected override async Task LoadAsync()
    {
        var subnets = await Task.Run(() => NetworkScannerService.GetLocalSubnets());
        Subnets.Clear();
        foreach (var s in subnets) Subnets.Add(new SubnetChoice(s));
        SelectedSubnet ??= Subnets.FirstOrDefault();
        if (Subnets.Count == 0) StatusMessage = "Kein aktives privates Netzwerk gefunden (Scanner arbeitet nur im eigenen Heimnetz).";
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (SelectedSubnet is null || IsScanning) return;
        var subnet = SelectedSubnet.Subnet;
        if (!AppServices.Settings.NetworkScanNoticeAccepted)
        {
            if (!Dialogs.Confirm("Netzwerk-Scanner",
                    $"Es werden die Adressen {subnet.Cidr} per Ping und ARP abgefragt, um Geräte in deinem Heimnetz zu finden. " +
                    "Scanne nur Netzwerke, die dir gehören oder für die du die Erlaubnis hast (z. B. nicht im Firmen- oder Hotel-WLAN).\n\nFortfahren?"))
                return;
            AppServices.Settings.NetworkScanNoticeAccepted = true;
            AppServices.SaveSettings();
        }

        IsScanning = true;
        StatusMessage = null;
        Devices.Clear();
        _cts = new CancellationTokenSource();
        var progress = new Progress<ScanProgress>(p =>
        {
            ScanPercent = 100.0 * p.Done / Math.Max(1, p.Total);
            ScanSummary = $"{p.Done}/{p.Total} Adressen geprüft · {p.Found} Geräte gefunden";
        });
        try
        {
            var token = _cts.Token;
            var devices = await Task.Run(() => AppServices.Scanner.ScanAsync(subnet, progress, token), token);
            foreach (var d in devices) Devices.Add(new LanDeviceRow(d));
            var newCount = devices.Count(d => d.IsNew);
            ScanSummary = $"{devices.Count} Geräte in {subnet.Cidr}" + (newCount > 0 ? $" · {newCount} NEU seit dem letzten Scan" : "");
            if (newCount > 0)
                AppServices.Notifications.Notify(Services.SystemTools.NotificationKind.SecurityAlert, "Neues Gerät im Heimnetz",
                    $"{newCount} bisher unbekannte(s) Gerät(e) im Netzwerk {subnet.Cidr}.");
            if (!HasVendorList)
                StatusMessage = "Tipp: „Herstellerliste laden“ zeigt zu jeder MAC-Adresse den Hersteller (offizielle IEEE-Liste).";
        }
        catch (OperationCanceledException)
        {
            ScanSummary = "Scan abgebrochen.";
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsScanning = false;
            ScanPercent = 0;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private static int? ModeCount(string mode) => mode switch
    {
        "1×" => 1,
        "10×" => 10,
        "Dauerhaft" => null,
        _ => 4,
    };

    [RelayCommand]
    private Task PingDeviceAsync(LanDeviceRow? row) => row is null ? Task.CompletedTask : StartPingAsync(row.Ip, 4);

    [RelayCommand]
    private Task LivePingDeviceAsync(LanDeviceRow? row) => row is null ? Task.CompletedTask : StartPingAsync(row.Ip, null);

    [RelayCommand]
    private Task QuickPingAsync() => StartPingAsync(QuickPingInput, ModeCount(PingMode));

    [RelayCommand]
    private void StopPing() => _pingCts?.Cancel();

    /// <summary>Startet eine Ping-Serie. Ein laufender Ping wird vorher beendet. Kein Shell-Aufruf – nur .NET-Ping.</summary>
    private async Task StartPingAsync(string? input, int? count)
    {
        if (!LivePingService.TryValidateTarget(input, out var host, out var error))
        {
            StatusMessage = error;
            return;
        }
        if (_pingCts is not null)
        {
            await _pingCts.CancelAsync();
            while (IsPinging) await Task.Delay(50);
        }

        var cts = new CancellationTokenSource();
        _pingCts = cts;
        _replies.Clear();
        PingLog.Clear();
        PingReplies = [];
        PingTarget = host;
        PingResult = null;
        IsPinging = true;
        StatusMessage = null;
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        try
        {
            var result = await LivePingService.RunAsync(host, count, reply => dispatcher.InvokeAsync(() =>
            {
                if (!ReferenceEquals(_pingCts, cts)) return;
                _replies.Add(reply);
                if (_replies.Count > 3600) _replies.RemoveAt(0);
                PingReplies = _replies.ToArray();
                PingResult = LivePingService.Evaluate(host, PingResult?.ResolvedAddress, _replies);
                PingLog.Insert(0, reply.Success
                    ? $"#{reply.Sequence}  {reply.Time:HH:mm:ss}  Antwort in {reply.RoundtripMs} ms"
                    : $"#{reply.Sequence}  {reply.Time:HH:mm:ss}  {reply.Status}");
                while (PingLog.Count > 200) PingLog.RemoveAt(PingLog.Count - 1);
            }), ct: cts.Token);
            // Endauswertung erst übernehmen, wenn alle Antworten auf dem UI-Thread angekommen sind.
            await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            if (ReferenceEquals(_pingCts, cts))
                PingResult = LivePingService.Evaluate(host, result.ResolvedAddress, _replies);
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_pingCts, cts))
            {
                _pingCts = null;
                IsPinging = false;
            }
            cts.Dispose();
        }
    }

    [RelayCommand]
    private void ShowDetails(LanDeviceRow? row)
    {
        row ??= SelectedDevice;
        if (row is null) return;
        DetailDevice = row;
        var d = row.Device;
        var lines = new List<string>
        {
            $"IP-Adresse: {row.Ip}",
            $"MAC-Adresse: {row.Mac}",
            $"Hersteller: {row.Vendor}",
            $"Hostname: {row.Name}",
            $"Gerätetyp (geschätzt): {row.Type}",
            $"Ping beim Scan: {row.Ping}",
        };
        if (d.IsGateway) lines.Add("Rolle: Standardgateway (Router)");
        if (d.IsSelf) lines.Add("Rolle: dieser PC");
        if (d.IsNew) lines.Add("Status: NEU seit dem letzten Scan");
        if (!string.IsNullOrWhiteSpace(row.Label)) lines.Add($"Eigene Bezeichnung: {row.Label}");
        lines.Add($"Dienste: {row.Ports ?? "noch nicht geprüft („SERVICES“)"}");
        DetailText = string.Join("\n", lines);
    }

    [RelayCommand]
    private Task ServicesAsync(LanDeviceRow? row) => CheckPortsAsync(row);

    [RelayCommand]
    private void CopyIp(LanDeviceRow? row) => Copy(row?.Ip, "IP-Adresse");

    [RelayCommand]
    private void CopyMac(LanDeviceRow? row) => Copy(row?.Device.Mac, "MAC-Adresse");

    private void Copy(string? text, string what)
    {
        if (string.IsNullOrEmpty(text)) { StatusMessage = $"{what} nicht verfügbar."; return; }
        try
        {
            System.Windows.Clipboard.SetText(text);
            StatusMessage = $"{what} kopiert: {text}";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusMessage = "Die Zwischenablage ist gerade belegt – bitte erneut versuchen.";
        }
    }

    public override void OnNavigatedFrom() => _pingCts?.Cancel();

    [RelayCommand]
    private async Task CheckPortsAsync(LanDeviceRow? row)
    {
        row ??= SelectedDevice;
        if (row is null || SelectedSubnet is null || row.IsChecking) return;
        row.IsChecking = true;
        row.Ports = "Prüfe gängige Dienste …";
        try
        {
            var ports = await AppServices.Scanner.CheckPortsAsync(SelectedSubnet.Subnet, row.Device.Ip, CancellationToken.None);
            row.Ports = ports.Count == 0 ? "Keine der geprüften Dienste erreichbar"
                : string.Join(" · ", ports.Select(p => $"{p.Port} {p.Service}"));
            row.PortState = ports.Any(p => p.State == DataState.Problem) ? DataState.Problem
                : ports.Any(p => p.State == DataState.Warning) ? DataState.Warning : DataState.Ok;
            var guess = NetworkScannerService.GuessType(row.Device.Vendor, row.Device.HostName, ports.Select(p => p.Port).ToList());
            if (!row.IsSpecial && guess.Length > 0) row.Type = guess;
            var hints = ports.Where(p => p.Hint is not null && p.State != DataState.Ok).Select(p => $"• {p.Port}: {p.Hint}").ToList();
            if (hints.Count > 0) StatusMessage = $"{row.Ip}:\n" + string.Join("\n", hints);
        }
        catch (InvalidOperationException ex)
        {
            row.Ports = ex.Message;
        }
        finally
        {
            row.IsChecking = false;
        }
    }

    [RelayCommand]
    private void SaveLabel(LanDeviceRow? row)
    {
        if (row?.Device.Mac is null) return;
        AppServices.Scanner.SetLabel(row.Device.Mac, row.Label);
        StatusMessage = "Bezeichnung gespeichert.";
    }

    [RelayCommand]
    private async Task DownloadVendorsAsync()
    {
        if (!Dialogs.Confirm("Herstellerliste laden",
                $"Die offizielle MAC-Herstellerliste der IEEE (ca. 4 MB) wird über HTTPS geladen und lokal gespeichert:\n{NetworkScannerService.OuiUrl}\n\nFortfahren?"))
            return;
        IsBusy = true;
        try
        {
            ShowResult(await AppServices.Scanner.DownloadVendorListAsync(CancellationToken.None));
            HasVendorList = NetworkScannerService.HasVendorList;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ForgetDevices()
    {
        if (!Dialogs.Confirm("Bekannte Geräte vergessen", "Die Liste bekannter Geräte (für „neu im Netzwerk“) und alle Bezeichnungen löschen?")) return;
        AppServices.Scanner.ForgetKnownDevices();
        StatusMessage = "Liste bekannter Geräte gelöscht. Der nächste Scan gilt als Ausgangsbasis.";
    }

    [RelayCommand]
    private void OpenInBrowser(LanDeviceRow? row)
    {
        if (row is null || !IPAddress.TryParse(row.Ip, out var ip) || !NetworkScannerService.IsPrivate(ip)) return;
        // Nur private Adressen; Standardbrowser öffnet die Weboberfläche des Geräts (z. B. Router).
        using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo($"http://{ip}/") { UseShellExecute = true });
    }
}
