using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;

namespace UltimateX.App.ViewModels;

/// <summary>Basis aller Seiten: Laden im Hintergrund, Busy-Status, Fehler werden angezeigt statt die App zu beenden (§90).</summary>
public abstract partial class PageViewModel : ObservableObject
{
    private bool _loaded;

    public abstract string Title { get; }

    /// <summary>Segoe-Fluent-Icons-Glyph.</summary>
    public abstract string Glyph { get; }

    /// <summary>Suchbegriffe für die globale Suche (§102).</summary>
    public virtual string Keywords => string.Empty;

    public virtual string? Subtitle => null;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusMessage;

    public async Task EnsureLoadedAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            AppLog.Error(Title, "Laden fehlgeschlagen", ex);
            StatusMessage = "Daten konnten nicht vollständig geladen werden: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected abstract Task LoadAsync();

    public virtual void OnNavigatedTo() { }

    public virtual void OnNavigatedFrom() { }

    protected void ShowResult(OperationResult r) => StatusMessage = r.Message;
}

/// <summary>Titelzeile + Liste von Werten – generisch gerendert über Themes/Templates.xaml.</summary>
public sealed record InfoSection(string Title, string Glyph, IReadOnlyList<InfoItem> Items, string? Note = null);
