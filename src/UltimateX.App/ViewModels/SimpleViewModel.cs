using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Settings;

namespace UltimateX.App.ViewModels;

/// <summary>Simple Mode (§9): große Schaltflächen, nur das Wichtigste.</summary>
public sealed partial class SimpleViewModel : PageViewModel
{
    private readonly MainViewModel _main;

    public SimpleViewModel(MainViewModel main) => _main = main;

    public override string Title => "Start";
    public override string Glyph => "\uE80F";
    public override string Keywords => "simple einfach";
    public override string? Subtitle => "Was möchten Sie tun?";

    protected override Task LoadAsync() => Task.CompletedTask;

    [RelayCommand]
    private void CheckPc()
    {
        var dash = _main.Page<DashboardViewModel>();
        _main.Navigate(dash);
        if (dash.CompleteCheckCommand.CanExecute(null)) dash.CompleteCheckCommand.Execute(null);
    }

    [RelayCommand] private void TestInternet() => _main.Navigate(_main.Page<SpeedtestViewModel>());

    [RelayCommand] private void SearchUpdates() => _main.Navigate(_main.Page<UpdatesViewModel>());

    [RelayCommand] private void CleanStorage() => _main.Navigate(_main.Page<CleanupViewModel>());

    [RelayCommand] private void OrganizeDesktop() => _main.Navigate(_main.Page<DesktopOrganizerViewModel>());

    [RelayCommand] private void ManageStartup() => _main.Navigate(_main.Page<StartupViewModel>());

    [RelayCommand] private void CheckWindows() => _main.Navigate(_main.Page<HealthViewModel>());

    [RelayCommand] private Task Restart() => ToolsViewModel.ConfirmPowerAsync(true, s => StatusMessage = s);

    [RelayCommand]
    private void Sleep()
    {
        if (Dialogs.Confirm("Energiesparmodus", "PC jetzt in den Energiesparmodus versetzen?")) ShowResult(AppServices.Power.Sleep());
    }

    [RelayCommand]
    private void AdvancedView()
    {
        AppServices.Settings.Layout = LayoutMode.ModernControlCenter;
        AppServices.SaveSettings();
        _main.ApplyLayout(LayoutMode.ModernControlCenter, _main.Page<DashboardViewModel>());
    }
}
