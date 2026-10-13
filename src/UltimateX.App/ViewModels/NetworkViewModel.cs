using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Network;

namespace UltimateX.App.ViewModels;

public sealed record AdapterView(string Title, string Subtitle, bool IsUp, IReadOnlyList<InfoItem> Items, string? Mac);

/// <summary>Network Center (§24–§29).</summary>
public sealed partial class NetworkViewModel : PageViewModel
{
    public override string Title => "Netzwerk";
    public override string Glyph => "\uE968";
    public override string Keywords => "netzwerk network internet ip ipv4 ipv6 mac adresse wlan wifi lan ethernet dns gateway dhcp ping diagnose";

    public ObservableCollection<AdapterView> Adapters { get; } = [];
    public ObservableCollection<DiagnosisStep> Diagnosis { get; } = [];
    public ObservableCollection<DnsTestResult> DnsResults { get; } = [];
    public ObservableCollection<InfoSection> WlanSections { get; } = [];
    private IReadOnlyList<WlanInfo> _wlan = [];
    private string? _wlanMessage;

    [ObservableProperty] private bool _sensitiveRevealed;
    [ObservableProperty] private string? _publicIpV4;
    [ObservableProperty] private string? _publicIpV6;
    [ObservableProperty] private bool _publicIpLoaded;
    [ObservableProperty] private string _pingTarget = "1.1.1.1";
    [ObservableProperty] private PingStatistics? _pingResult;
    [ObservableProperty] private string? _pingError;
    [ObservableProperty] private bool _isToolRunning;

    private IReadOnlyList<AdapterInfo> _adapters = [];
    private string? _rawV4, _rawV6;

    protected override async Task LoadAsync()
    {
        var wlanTask = Task.Run(() => AppServices.Wlan.GetInfo());
        _adapters = await Task.Run(() => AppServices.Network.GetAdapters());
        var wlan = await wlanTask;
        _wlan = wlan.Interfaces;
        _wlanMessage = wlan.Message;
        Build();
    }

    public override void OnNavigatedTo()
    {
        if (SensitiveRevealed && !AppServices.Auth.HasActiveSession)
        {
            SensitiveRevealed = false;
            Build();
        }
    }

    private bool Masked => AppServices.Settings.SensitiveMode && !SensitiveRevealed;

    private string Ip(string ip) => Masked ? SensitiveMasker.MaskIp(ip) : ip;

    private void Build()
    {
        Adapters.Clear();
        foreach (var a in _adapters)
        {
            var items = new List<InfoItem>
            {
                new("Status", a.IsUp ? "Verbunden" : "Getrennt", a.IsUp ? DataState.Ok : DataState.NotAvailable),
                new("Typ", a.Kind),
                new("Geschwindigkeit", a.LinkSpeedBps is { } s ? Format.BitsPerSecond(s) : "nicht verfügbar"),
                new("MAC-Adresse", a.MacAddress is null ? "nicht verfügbar" : Masked ? SensitiveMasker.MaskMac(a.MacAddress) : a.MacAddress, DataState.Ok, true),
            };
            if (a.IPv4.Count > 0) items.Add(new("IPv4", string.Join(", ", a.IPv4.Select(Ip)), DataState.Ok, true));
            if (a.SubnetMasks.Count > 0) items.Add(new("Subnetz", string.Join(", ", a.SubnetMasks)));
            if (a.IPv6.Count > 0) items.Add(new("IPv6", string.Join(", ", a.IPv6.Select(Ip)), DataState.Ok, true));
            if (a.Gateways.Count > 0) items.Add(new("Gateway", string.Join(", ", a.Gateways)));
            if (a.DnsServers.Count > 0) items.Add(new("DNS", string.Join(", ", a.DnsServers)));
            items.Add(new("DHCP", a.DhcpEnabled ? "Aktiv" + (a.DhcpServers.Count > 0 ? $" (Server {string.Join(", ", a.DhcpServers)})" : "") : "Statische Konfiguration"));
            Adapters.Add(new AdapterView(a.Name, a.Description, a.IsUp, items, a.MacAddress));
        }
        // WLAN (§26)
        WlanSections.Clear();
        foreach (var w in _wlan)
        {
            var items = new List<InfoItem> { new("Status", w.Connected ? "Verbunden" : "Nicht verbunden", w.Connected ? DataState.Ok : DataState.NotAvailable) };
            if (w.Note is not null) items.Add(new InfoItem("Hinweis", w.Note, DataState.Warning));
            if (w.Ssid is not null) items.Add(new("SSID", w.Ssid));
            if (w.Bssid is not null) items.Add(new("BSSID", Masked ? SensitiveMasker.MaskMac(w.Bssid.Replace(':', '-')) : w.Bssid, DataState.Ok, true));
            if (w.SignalQuality is { } q) items.Add(new("Signalstärke", $"{q} %" + (w.RssiDbm is { } r ? $" ({r} dBm)" : ""), q >= 60 ? DataState.Ok : q >= 35 ? DataState.Warning : DataState.Problem));
            if (w.Band is not null) items.Add(new("Frequenzband", w.Band));
            if (w.Channel is { } ch) items.Add(new("Kanal", ch.ToString(System.Globalization.CultureInfo.CurrentCulture)));
            if (w.Standard is not null) items.Add(new("Wi-Fi-Standard", w.Standard));
            if (w.TxMbps is { } tx) items.Add(new("Übertragungsrate", $"{tx:0} Mbit/s"));
            if (w.RxMbps is { } rx) items.Add(new("Empfangsrate", $"{rx:0} Mbit/s"));
            if (w.Security is not null) items.Add(new("Sicherheit", w.Security,
                w.Security.StartsWith("WPA2", StringComparison.Ordinal) || w.Security.StartsWith("WPA3", StringComparison.Ordinal) ? DataState.Ok : DataState.Warning));
            WlanSections.Add(new InfoSection(w.InterfaceName, "\uE701", items));
        }
        if (_wlan.Count == 0 && _wlanMessage is not null)
            WlanSections.Add(new InfoSection("WLAN", "\uE701", [InfoItem.Unavailable("Status", _wlanMessage)]));

        PublicIpV4 = _rawV4 is null ? null : Ip(_rawV4);
        PublicIpV6 = _rawV6 is null ? null : Ip(_rawV6);
    }

    [RelayCommand]
    private async Task RevealSensitiveAsync()
    {
        if (SensitiveRevealed) { SensitiveRevealed = false; Build(); return; }
        if (!await AppServices.Guard.AuthenticateOnlyAsync("Netzwerkadressen vollständig anzeigen")) return;
        SensitiveRevealed = true;
        Build();
    }

    [RelayCommand]
    private async Task CopyMacAsync(AdapterView? adapter)
    {
        if (adapter?.Mac is null) return;
        if (Masked && !await AppServices.Guard.AuthenticateOnlyAsync("MAC-Adresse kopieren")) return;
        try
        {
            Clipboard.SetText(adapter.Mac);
            StatusMessage = $"MAC-Adresse von „{adapter.Title}“ kopiert.";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            StatusMessage = "Zwischenablage ist gerade nicht verfügbar.";
        }
    }

    [RelayCommand]
    private async Task LoadPublicIpAsync()
    {
        if (!Dialogs.Confirm("Öffentliche IP abfragen",
                "Zur Ermittlung der öffentlichen IP wird eine HTTPS-Anfrage an den Dienst ipify.org gesendet. " +
                "Dabei sieht der Dienst Ihre öffentliche IP-Adresse. Es werden keine weiteren Daten übertragen.\n\nFortfahren?"))
            return;
        IsToolRunning = true;
        try
        {
            var r = await AppServices.Network.GetPublicIpAsync();
            (_rawV4, _rawV6) = (r.IPv4, r.IPv6);
            PublicIpLoaded = true;
            Build();
            if (r.IPv4 is null && r.IPv6 is null) StatusMessage = "Öffentliche IP konnte nicht ermittelt werden (keine Internetverbindung?).";
        }
        finally { IsToolRunning = false; }
    }

    [RelayCommand]
    private async Task DiagnoseAsync()
    {
        IsToolRunning = true;
        Diagnosis.Clear();
        try
        {
            foreach (var s in await AppServices.Network.DiagnoseAsync()) Diagnosis.Add(s);
        }
        finally { IsToolRunning = false; }
    }

    [RelayCommand]
    private void SetPingTarget(string? target)
    {
        if (target == "gateway")
        {
            var gw = _adapters.Where(a => a.IsUp).SelectMany(a => a.Gateways).FirstOrDefault(g => g.Contains('.'));
            if (gw is null) { PingError = "Kein Gateway gefunden."; return; }
            PingTarget = gw;
        }
        else if (target is not null)
        {
            PingTarget = target;
        }
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        PingError = null;
        PingResult = null;
        if (!InputValidator.TryNormalizeHost(PingTarget, out var host))
        {
            PingError = "Bitte eine gültige IP-Adresse oder einen gültigen Hostnamen eingeben.";
            return;
        }
        IsToolRunning = true;
        try { PingResult = await AppServices.Network.PingAsync(host); }
        finally { IsToolRunning = false; }
    }

    [RelayCommand]
    private async Task TestDnsAsync()
    {
        IsToolRunning = true;
        DnsResults.Clear();
        try
        {
            var results = await AppServices.Network.TestDnsServersAsync();
            foreach (var r in results) DnsResults.Add(r);
            if (results.Count == 0) StatusMessage = "Keine DNS-Server auf aktiven Adaptern gefunden.";
        }
        finally { IsToolRunning = false; }
    }
}
