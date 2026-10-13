using System.Windows;
using UltimateX.Core.Localization;

namespace UltimateX.App.Infrastructure;

/// <summary>Einheitliche Rückfragen/Meldungen. Kritische Aktionen werden IMMER bestätigt (§58).</summary>
public static class Dialogs
{
    private static Window? Owner => Application.Current?.MainWindow;

    public static bool Confirm(string title, string message, bool warning = false)
    {
        title = Loc.T(title);
        message = Loc.T(message);
        var owner = Owner;
        var result = owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, warning ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No)
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, warning ? MessageBoxImage.Warning : MessageBoxImage.Question, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }

    public static void Info(string title, string message)
    {
        title = Loc.T(title);
        message = Loc.T(message);
        var owner = Owner;
        if (owner is null) MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public static void Error(string title, string message)
    {
        title = Loc.T(title);
        message = Loc.T(message);
        var owner = Owner;
        if (owner is null) MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        else MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>Speichern-Dialog; liefert null bei Abbruch.</summary>
    public static string? SaveFile(string defaultName, string filter)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = defaultName, Filter = Loc.T(filter), AddExtension = true, OverwritePrompt = true };
        var owner = Owner;
        return (owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner)) == true ? dlg.FileName : null;
    }

    public static string? OpenFile(string filter)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T(filter), CheckFileExists = true, Multiselect = false };
        var owner = Owner;
        return (owner is null ? dlg.ShowDialog() : dlg.ShowDialog(owner)) == true ? dlg.FileName : null;
    }
}
