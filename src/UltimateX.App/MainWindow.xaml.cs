using System.Windows;
using System.Windows.Input;
using UltimateX.App.Infrastructure;
using UltimateX.App.ViewModels;

namespace UltimateX.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // Tastenkürzel: Strg+K = Suche, F5 = Aktualisieren
        InputBindings.Add(new KeyBinding(new RelayKeyCommand(() => { SearchBox.Focus(); SearchBox.SelectAll(); }), Key.K, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayKeyCommand(() => viewModel.CurrentPage?.RefreshCommand.Execute(null)), Key.F5, ModifierKeys.None));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ThemeManager.ApplyTitleBar(this);
    }

    /// <summary>Minimaler ICommand für Tastenkürzel (nur UI-Fokus/Navigation, keine Systemaktionen).</summary>
    private sealed class RelayKeyCommand(Action action) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => action();
    }
}
