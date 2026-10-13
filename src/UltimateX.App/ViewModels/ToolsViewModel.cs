using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

/// <summary>CMD-/PowerShell-Tools und Windows Power Control (§36, §38).</summary>
public sealed partial class ToolsViewModel : PageViewModel
{
    public override string Title => "Tools & Power";
    public override string Glyph => "\uE756";
    public override string Keywords => "tools werkzeuge cmd powershell terminal taskmanager geräte-manager datenträgerverwaltung msinfo herunterfahren neustart shutdown restart sperren energiesparen abmelden";

    public IReadOnlyList<ToolDefinition> AllTools => ToolLauncher.All;

    [ObservableProperty] private bool _countdownEnabled = AppServices.Settings.ShutdownCountdownEnabled;
    [ObservableProperty] private bool _shutdownScheduled;

    protected override Task LoadAsync() => Task.CompletedTask;

    [RelayCommand]
    private async Task LaunchAsync(ToolDefinition? tool)
    {
        if (tool is null) return;
        if (tool.RequiresAdmin)
        {
            // Administrative Konsolen sind sensible Aktionen (§57): Authentifizierung + Bestätigung, danach UAC durch Windows.
            await AppServices.Guard.RunAsync(tool.Title,
                $"„{tool.Title}“ wird mit Administratorrechten geöffnet. Befehle darin können das System verändern.",
                () => Task.FromResult(Apply(AppServices.Tools.Launch(tool.Id))));
        }
        else
        {
            Apply(AppServices.Tools.Launch(tool.Id));
        }
    }

    private bool Apply(OperationResult r)
    {
        StatusMessage = r.Success ? null : r.Message;
        return r.Success;
    }

    partial void OnCountdownEnabledChanged(bool value)
    {
        AppServices.Settings.ShutdownCountdownEnabled = value;
        AppServices.SaveSettings();
    }

    [RelayCommand]
    private async Task ShutdownAsync()
    {
        await ConfirmPowerAsync(false, s => StatusMessage = s);
        ShutdownScheduled = AppServices.Settings.ShutdownCountdownEnabled;
    }

    [RelayCommand]
    private async Task RestartAsync()
    {
        await ConfirmPowerAsync(true, s => StatusMessage = s);
        ShutdownScheduled = AppServices.Settings.ShutdownCountdownEnabled;
    }

    [RelayCommand]
    private async Task AbortShutdownAsync()
    {
        var r = await AppServices.Power.AbortAsync();
        StatusMessage = r.Message;
        ShutdownScheduled = false;
    }

    [RelayCommand]
    private void Sleep()
    {
        if (Dialogs.Confirm("Energiesparmodus", "PC jetzt in den Energiesparmodus versetzen?")) Apply(AppServices.Power.Sleep());
    }

    [RelayCommand]
    private void LockPc() => Apply(AppServices.Power.Lock());

    [RelayCommand]
    private void SignOut()
    {
        if (Dialogs.Confirm("Abmelden", "Jetzt abmelden? Nicht gespeicherte Arbeit in anderen Programmen kann verloren gehen.", true))
            Apply(AppServices.Power.SignOut());
    }

    /// <summary>Gemeinsame Bestätigung für Neustart/Herunterfahren (Dashboard, Simple Mode, Tools).</summary>
    internal static async Task ConfirmPowerAsync(bool restart, Action<string> setStatus)
    {
        var s = AppServices.Settings;
        var seconds = s.ShutdownCountdownEnabled ? s.ShutdownCountdownSeconds : 0;
        var what = restart ? "Neustart" : "Herunterfahren";
        var when = seconds > 0 ? $"in {seconds} Sekunden (abbrechbar unter Tools & Power)" : "sofort";
        if (!Dialogs.Confirm(what, $"{what} {when}?\n\nBitte speichern Sie vorher alle offenen Arbeiten.", true)) return;
        var r = restart ? await AppServices.Power.RestartAsync(seconds) : await AppServices.Power.ShutdownAsync(seconds);
        setStatus(r.Message);
    }
}
