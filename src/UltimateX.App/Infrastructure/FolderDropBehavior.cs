using System.Windows;
using System.Windows.Input;

namespace UltimateX.App.Infrastructure;

/// <summary>
/// Angehängte Eigenschaft für Drag &amp; Drop von Ordnern/Dateien aus dem Explorer (v1.3 Backup-Center):
/// <c>infra:FolderDropBehavior.DropCommand="{Binding AddDroppedCommand}"</c> – das Kommando erhält ein string[] mit vollständigen Pfaden.
/// Es werden nur Dateisystempfade angenommen (DataFormats.FileDrop); Prüfung der Pfade erfolgt im ViewModel.
/// </summary>
public static class FolderDropBehavior
{
    public static readonly DependencyProperty DropCommandProperty = DependencyProperty.RegisterAttached(
        "DropCommand", typeof(ICommand), typeof(FolderDropBehavior), new PropertyMetadata(null, OnChanged));

    public static ICommand? GetDropCommand(DependencyObject d) => (ICommand?)d.GetValue(DropCommandProperty);

    public static void SetDropCommand(DependencyObject d, ICommand? value) => d.SetValue(DropCommandProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        el.DragOver -= OnDragOver;
        el.Drop -= OnDrop;
        if (e.NewValue is null) return;
        el.AllowDrop = true;
        el.DragOver += OnDragOver;
        el.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not DependencyObject d || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var cmd = GetDropCommand(d);
        if (cmd?.CanExecute(paths) == true) cmd.Execute(paths);
        e.Handled = true;
    }
}
