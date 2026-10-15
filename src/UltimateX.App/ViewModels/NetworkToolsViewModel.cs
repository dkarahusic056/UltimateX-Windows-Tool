using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.NetSec;
using UltimateX.Services.NetSec;

namespace UltimateX.App.ViewModels;

public sealed record DnsChoice(string? ProviderId, string Label)
{
    public override string ToString() => Label;
}

public sealed record WakeChoice(string Mac, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// NETZWERK-WERKZEUGE (v1.3): Traceroute, Port-Prüfung, DNS-Cache, IP erneuern, DNS-Server, Netzwerk-Reset, Wake-on-LAN.
/// Internetzugriffe nur zu Zielen, die der Benutzer selbst eingibt.
/// </summary>
public sealed partial class NetworkToolsViewModel : PageViewModel
{
    public override string Title => "Netzwerk-Werkzeuge";
    public override string Glyph => "";
    public override string Keywords => "netzwerk werkzeuge traceroute tracert route port prüfen dns cache flushdns ip erneuern renew dns server cloudflare quad9 google winsock reset wake on lan wol";
    public override string? Subtitle => "Route verfolgen, Ports prüfen, DNS und Netzwerk reparieren, Geräte aufwecken";

    private CancellationTokenSource? _cts;

    // ---- Traceroute
    [ObservableProperty] private string _traceTarget = "";
    [ObservableProperty] private bool _resolveNames = true;
    public ObservableCollection<TraceHop> TraceHops { get; } = [];

    // ---- Ports
    [ObservableProperty] private string _portHost = "";
    [ObservableProperty] private string _portList = "80, 443";
    public ObservableCollection<PortResult> PortResults { get; } = [];

    // ---- DNS
    public ObservableCollection<DnsAdapter> Adapters { get; } = [];
    [ObservableProperty] private DnsAdapter? _selectedAdapter;
    public IReadOnlyList<DnsChoice> DnsChoices { get; } =
        DnsPresets.Providers.Select(p => new DnsChoice(p.Id, p.Name)).Append(new DnsChoice(null, "Automatisch (DHCP)")).ToList();
    [ObservableProperty] private DnsChoice? _selectedDns;
    [ObservableProperty] private bool _includeIpv6;

    // ---- Wake-on-LAN
    [ObservableProperty] private string _wakeMac = "";
    public ObservableCollection<WakeChoice> WakeTargets { get; } = [];
    [ObservableProperty] private WakeChoice? _selectedWakeTarget;

    // ---- Ergebnisbereich
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isRunning;

    public string PortHint => $"Nur ein Ziel, höchstens {PortCheckLogic.MaxPorts} einzelne Ports pro Prüfung (kein Portscanner).";

    protected override async Task LoadAsync()
    {
        var adapters = await Task.Run(NetworkToolsService.GetAdapters);
        var known = await Task.Run(() => AppServices.History.GetKnownDevices());
        var keep = SelectedAdapter?.Index;
        Adapters.Clear();
        foreach (var a in adapters) Adapters.Add(a);
        SelectedAdapter = Adapters.FirstOrDefault(a => a.Index == keep) ?? Adapters.FirstOrDefault();
        SelectedDns ??= DnsChoices[0];

        WakeTargets.Clear();
        foreach (var d in known.Where(d => WakeOnLan.TryParseMac(d.Mac, out _)))
            WakeTargets.Add(new WakeChoice(d.Mac, $"{d.Label ?? d.Name ?? d.Vendor ?? "Gerät"} · {d.Ip} · {d.Mac}"));
    }

    partial void OnSelectedWakeTargetChanged(WakeChoice? value)
    {
        if (value is not null) WakeMac = value.Mac;
    }

    private CancellationToken Begin(string title)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        IsRunning = true;
        StatusMessage = null;
        Output = $"== {title} ({DateTime.Now:HH:mm:ss}) =={Environment.NewLine}";
        return _cts.Token;
    }

    private void End() => IsRunning = false;

    private void Append(string text) => Output += text.EndsWith('\n') ? text : text + Environment.NewLine;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ------------------------------------------------------------ Traceroute

    [RelayCommand]
    private async Task TraceAsync()
    {
        if (IsRunning) return;
        var ct = Begin("Traceroute");
        TraceHops.Clear();
        try
        {
            var (ip, error) = await NetworkToolsService.ResolveAsync(TraceTarget, ct);
            if (ip is null) { Append(error ?? "Ziel ungültig."); StatusMessage = error; return; }
            Append($"Route zu {TraceTarget.Trim()} [{ip}], höchstens {TracerouteLogic.MaxHops} Hops:");
            var progress = new Progress<TraceHop>(h =>
            {
                TraceHops.Add(h);
                Append($"{h.Ttl,2}  {h.TimesText,-28} {h.AddressText}{(h.HostName is null ? "" : "  (" + h.HostName + ")")}");
            });
            var hops = await AppServices.NetTools.TraceAsync(ip, ResolveNames, progress, ct);
            Append(TracerouteLogic.Summary(hops, false));
        }
        catch (OperationCanceledException)
        {
            Append(TracerouteLogic.Summary(TraceHops.ToList(), true));
        }
        finally
        {
            End();
        }
    }

    // ------------------------------------------------------------ Ports

    [RelayCommand]
    private void UseCommonPorts() => PortList = string.Join(", ", PortCheckLogic.CommonServices.Select(s => s.Port));

    [RelayCommand]
    private async Task CheckPortsAsync()
    {
        if (IsRunning) return;
        if (!PortCheckLogic.TryParsePorts(PortList, out var ports, out var portError)) { StatusMessage = portError; return; }
        var ct = Begin("Port-Prüfung");
        PortResults.Clear();
        try
        {
            var (ip, error) = await NetworkToolsService.ResolveAsync(PortHost, ct);
            if (ip is null) { Append(error ?? "Ziel ungültig."); StatusMessage = error; return; }
            Append($"Ziel {PortHost.Trim()} [{ip}], {ports.Count} Port(s), Zeitlimit {PortCheckLogic.TimeoutMs} ms:");
            var results = await AppServices.NetTools.CheckPortsAsync(ip, ports, null, ct);
            foreach (var r in results)
            {
                PortResults.Add(r);
                Append($"{r.Port,5}  {r.Service,-22} {r.StateText}{(r.Ms is { } ms ? $" ({ms} ms)" : "")}");
            }
            var open = results.Count(r => r.State == PortState.Open);
            Append($"{open} von {results.Count} Port(s) offen.");
        }
        catch (OperationCanceledException)
        {
            Append("Abgebrochen.");
        }
        finally
        {
            End();
        }
    }

    // ------------------------------------------------------------ DNS-Cache / IP

    [RelayCommand]
    private async Task FlushDnsAsync()
    {
        if (IsRunning) return;
        var ct = Begin("DNS-Cache leeren");
        try
        {
            var r = await NetworkToolsService.FlushDnsAsync(null, ct);
            Append(r.Output.Trim());
            StatusMessage = r.ExitCode == 0 ? "DNS-Cache geleert." : $"Fehlgeschlagen (Exit {r.ExitCode}).";
        }
        finally
        {
            End();
        }
    }

    [RelayCommand]
    private async Task RenewIpAsync()
    {
        if (IsRunning) return;
        if (!Dialogs.Confirm("IP-Adresse neu anfordern", "Alle Netzwerkverbindungen werden kurz unterbrochen, während Windows neue Adressen vom Router anfordert. Fortfahren?"))
            return;
        var ct = Begin("IP-Adresse neu anfordern");
        try
        {
            var progress = new Progress<string>(Append);
            var r = await NetworkToolsService.RenewIpAsync(progress, ct);
            StatusMessage = r.Cancelled ? "Abgebrochen." : r.ExitCode == 0 ? "IP-Adressen neu angefordert." : $"Teilweise fehlgeschlagen (Exit {r.ExitCode}) – Details im Ergebnisbereich.";
        }
        finally
        {
            End();
        }
        await RefreshAsync();
    }

    // ------------------------------------------------------------ DNS-Server

    [RelayCommand]
    private async Task ApplyDnsAsync()
    {
        if (IsRunning) return;
        if (SelectedAdapter is not { } adapter || SelectedDns is not { } choice) { StatusMessage = "Bitte Adapter und DNS-Server wählen."; return; }
        var steps = choice.ProviderId is null ? (IncludeIpv6 ? 2 : 1) : (IncludeIpv6 ? 4 : 2);
        if (!Dialogs.Confirm("DNS-Server umstellen",
                $"DNS-Server für „{adapter.Name}“ auf „{choice.Label}“ umstellen?\n\nDafür sind Adminrechte nötig; Windows fragt {steps}× nach (ein Schritt je Adresse)."))
            return;
        var ct = Begin("DNS-Server umstellen");
        try
        {
            var r = await NetworkToolsService.SetDnsAsync(adapter, choice.ProviderId, IncludeIpv6, new Progress<string>(Append), ct);
            if (r.TechnicalDetails is { } d) Append(d);
            StatusMessage = r.Message;
        }
        catch (OperationCanceledException)
        {
            Append("Abgebrochen.");
        }
        finally
        {
            End();
        }
        await RefreshAsync();
    }

    // ------------------------------------------------------------ Netzwerk zurücksetzen

    [RelayCommand]
    private async Task ResetNetworkAsync()
    {
        if (IsRunning) return;
        if (!Dialogs.Confirm("Netzwerk zurücksetzen",
                "Winsock-Katalog und TCP/IP-Einstellungen werden auf Windows-Standard zurückgesetzt. Manuell eingetragene IP-Adressen gehen dabei verloren; VPN-/Proxy-Software muss evtl. neu eingerichtet werden.\n\nDanach ist ein NEUSTART nötig. Fortfahren?",
                warning: true))
            return;
        var ct = Begin("Netzwerk zurücksetzen");
        try
        {
            var r = await NetworkToolsService.ResetNetworkAsync(new Progress<string>(Append), ct);
            if (r.TechnicalDetails is { } d) Append(d);
            StatusMessage = r.Message;
            if (r.Success) Append("Hinweis: Neustart nötig, damit die Änderungen wirksam werden.");
        }
        catch (OperationCanceledException)
        {
            Append("Abgebrochen.");
        }
        finally
        {
            End();
        }
    }

    // ------------------------------------------------------------ Wake-on-LAN

    [RelayCommand]
    private async Task WakeAsync()
    {
        if (IsRunning) return;
        if (!WakeOnLan.TryParseMac(WakeMac, out _)) { StatusMessage = "Ungültige MAC-Adresse (z. B. AA:BB:CC:DD:EE:FF)."; return; }
        var ct = Begin("Wake-on-LAN");
        try
        {
            var r = await NetworkToolsService.WakeAsync(WakeMac, ct);
            Append(r.Message);
            if (r.TechnicalDetails is { } d) Append(d);
            Append("Das Gerät muss Wake-on-LAN im BIOS/Netzwerktreiber aktiviert haben und per Kabel verbunden sein.");
            StatusMessage = r.Message;
        }
        catch (OperationCanceledException)
        {
            Append("Abgebrochen.");
        }
        finally
        {
            End();
        }
    }

    [RelayCommand]
    private void CopyOutput()
    {
        if (string.IsNullOrEmpty(Output)) return;
        try
        {
            System.Windows.Clipboard.SetText(Output);
            StatusMessage = "Ergebnis kopiert.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            StatusMessage = "Die Zwischenablage ist gerade belegt – bitte erneut versuchen.";
        }
    }

    public override void OnNavigatedFrom() => _cts?.Cancel();
}
