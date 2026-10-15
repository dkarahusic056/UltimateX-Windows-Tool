using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;
using UltimateX.Core.NetSec;
using UltimateX.Services.NetSec;

namespace UltimateX.App.ViewModels;

/// <summary>
/// SICHERHEITS-CHECK (v1.3, IT-Audit): liest SMB1, Remotedesktop, Firewall, Administratoren, Konten, Gastkonto, UPnP,
/// Autorun, PowerShell 2.0 und den Windows-Support. Behebungen nur nach Bestätigung (UAC). Defender/UAC werden nie verändert.
/// </summary>
public sealed partial class SecurityAuditViewModel : PageViewModel
{
    public override string Title => "Sicherheits-Check";
    public override string Glyph => "";
    public override string Keywords => "sicherheit check audit smb1 remotedesktop rdp firewall administratoren gastkonto passwort upnp router autorun autoplay powershell windows version support";
    public override string? Subtitle => "Typische Schwachstellen prüfen – nur lesen, Behebung nur nach Bestätigung";

    public ObservableCollection<Finding> Findings { get; } = [];

    [ObservableProperty] private string _score = "–";
    [ObservableProperty] private DataState _scoreState = DataState.NotAvailable;
    [ObservableProperty] private string? _summary;
    [ObservableProperty] private bool _canFixSmb1;
    [ObservableProperty] private bool _canFixRdp;
    [ObservableProperty] private bool _canFixFirewall;
    [ObservableProperty] private string? _fixOutput;

    public bool HasFixes => CanFixSmb1 || CanFixRdp || CanFixFirewall;

    protected override async Task LoadAsync()
    {
        var facts = await AppServices.SecurityAudit.CollectAsync(CancellationToken.None);
        var result = SecurityAudit.Evaluate(facts);
        Findings.Clear();
        foreach (var f in result.Findings) Findings.Add(f);
        Score = result.Score is { } s ? $"{s} / 100" : "N/A";
        ScoreState = ScoreMath.StatusOf(result.Score, 80, 50).ToDataState();
        Summary = result.OpenIssues == 0 ? "Keine Auffälligkeiten gefunden." : $"{result.OpenIssues} Punkt(e) mit Handlungsbedarf.";
        CanFixSmb1 = result.Offers(AuditFix.Smb1Disable);
        CanFixRdp = result.Offers(AuditFix.RdpDisable);
        CanFixFirewall = result.Offers(AuditFix.FirewallEnableAll);
        OnPropertyChanged(nameof(HasFixes));
    }

    [RelayCommand]
    private Task FixSmb1Async() => FixAsync(AuditFix.Smb1Disable, "SMB1 deaktivieren",
        "Das veraltete SMB1-Protokoll wird über DISM deaktiviert (Adminrechte). Sehr alte NAS-Geräte oder Drucker, die nur SMB1 können, sind danach evtl. nicht mehr erreichbar. Ein Neustart ist nötig.\n\nFortfahren?");

    [RelayCommand]
    private Task FixRdpAsync() => FixAsync(AuditFix.RdpDisable, "Remotedesktop ausschalten",
        "Remotedesktop-Verbindungen zu diesem PC werden abgeschaltet (Adminrechte). Wenn Sie gerade selbst per Remotedesktop verbunden sind, wird die Verbindung danach nicht mehr möglich sein.\n\nFortfahren?");

    [RelayCommand]
    private Task FixFirewallAsync() => FixAsync(AuditFix.FirewallEnableAll, "Firewall einschalten",
        "Die Windows-Firewall wird für die Profile Domäne, Privat und Öffentlich eingeschaltet (Adminrechte). Ausnahmeregeln bleiben erhalten.\n\nFortfahren?");

    private async Task FixAsync(AuditFix fix, string title, string message)
    {
        if (IsBusy) return;
        if (!Dialogs.Confirm(title, message, warning: true)) return;
        IsBusy = true;
        OperationResult r;
        try
        {
            r = await SecurityAuditService.FixAsync(fix, null, CancellationToken.None);
        }
        finally
        {
            IsBusy = false;
        }
        FixOutput = r.TechnicalDetails;
        await RefreshAsync();
        StatusMessage = r.Message;
    }

    [RelayCommand]
    private void OpenWindowsSecurity() => ShowResult(UriLauncher.OpenSettings("windowsdefender"));

    [RelayCommand]
    private void OpenAutoplay() => ShowResult(UriLauncher.OpenSettings("autoplay"));

    [RelayCommand]
    private void OpenWindowsUpdate() => ShowResult(UriLauncher.OpenSettings("windowsupdate"));
}
