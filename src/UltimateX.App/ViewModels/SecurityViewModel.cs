using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Security;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

/// <summary>Security Center (§53, §61, §72–§80) inkl. Datei-Signaturprüfung und Hash-Tool.</summary>
public sealed partial class SecurityViewModel : PageViewModel
{
    public override string Title => "Security Center";
    public override string Glyph => "\uEA18";
    public override string Keywords => "security sicherheit defender virenschutz antivirus firewall secure boot tpm bitlocker uac signatur hash sha256 integrität lock";

    public ObservableCollection<SecurityItem> Items { get; } = [];

    [ObservableProperty] private string? _fileName;
    [ObservableProperty] private ObservableCollection<InfoItem> _fileResults = [];
    [ObservableProperty] private bool _includeLegacyMd5;

    protected override async Task LoadAsync()
    {
        var items = await Task.Run(() => AppServices.Security.GetStatus());
        Items.Clear();
        foreach (var i in items) Items.Add(i);
    }

    /// <summary>File Signature Checker (§78) + Hash Tool (§79). Die Datei wird NIE ausgeführt.</summary>
    [RelayCommand]
    private async Task CheckFileAsync()
    {
        var path = Dialogs.OpenFile("Programme und Bibliotheken (*.exe;*.dll;*.msi;*.sys)|*.exe;*.dll;*.msi;*.sys|Alle Dateien (*.*)|*.*");
        if (path is null) return;
        FileName = path;
        FileResults = [];
        IsBusy = true;
        try
        {
            var md5 = IncludeLegacyMd5;
            var results = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                var sig = SignatureVerifier.Verify(path);
                var list = new List<InfoItem>
                {
                    new("Größe", Format.Bytes(info.Length)),
                    new("Signatur", sig.StatusText, sig.Status switch
                    {
                        SignatureStatus.Valid => DataState.Ok,
                        SignatureStatus.Tampered => DataState.Problem,
                        SignatureStatus.Error => DataState.NotAvailable,
                        _ => DataState.Warning,
                    }),
                    new("Herausgeber", sig.Publisher ?? "–"),
                    new("Zertifikat (Thumbprint)", sig.Thumbprint ?? "–"),
                    new("SHA-256", SignatureVerifier.Sha256(path)),
                    new("SHA-512", SignatureVerifier.Sha512(path)),
                };
                if (md5) list.Add(new InfoItem("MD5 (Legacy, unsicher)", SignatureVerifier.Md5Legacy(path), DataState.Warning));
                return (list, sig);
            });
            FileResults = new ObservableCollection<InfoItem>(results.list);
            if (results.sig.Status == SignatureStatus.Tampered)
                AppLog.Security("SignatureFailure", "Ungültige Signatur bei geprüfter Datei", LogLevel.Warning, Path.GetFileName(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Datei konnte nicht gelesen werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenWindowsSecurity() => ShowResult(AppServices.Tools.Launch(SystemTool.WindowsSecurity));

    [RelayCommand]
    private void OpenFirewall() => ShowResult(AppServices.Tools.Launch(SystemTool.FirewallSettings));
}
