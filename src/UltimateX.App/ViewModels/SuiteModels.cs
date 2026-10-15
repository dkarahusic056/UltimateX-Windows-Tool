using CommunityToolkit.Mvvm.ComponentModel;
using UltimateX.Core.Analysis;
using UltimateX.Core.Common;

namespace UltimateX.App.ViewModels;

/// <summary>Eine Teilwertung (0–100) mit Balken; null = N/A.</summary>
public sealed record ScoreRow(string Name, int? Score, string? Detail = null)
{
    public string Text => Score is { } s ? $"{s}/100" : "N/A";
    public double Percent => Score ?? 0;
    public DataState State => Score is null ? DataState.NotAvailable : ScoreMath.StatusOf(Score).ToDataState();
}

/// <summary>Erwartete gegenüber gemessener Leistung (v1.2 §10).</summary>
public sealed record ExpectedRow(string Component, string Expected, string Measured, string Deviation, DataState State, string Causes);

/// <summary>Zeile „Schritt / Status / Detail“ (Complete Benchmark, One-Click-Setup).</summary>
public sealed partial class StepRow(string name) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private CheckStatus _status = CheckStatus.NotAvailable;
    [ObservableProperty] private string _detail = "Ausstehend";
    [ObservableProperty] private bool _isRunning;

    public DataState State => Status.ToDataState();
    public string StatusText => IsRunning ? "LÄUFT" : Detail == "Ausstehend" ? "–" : Status.Label();

    partial void OnStatusChanged(CheckStatus value) { OnPropertyChanged(nameof(State)); OnPropertyChanged(nameof(StatusText)); }
    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(StatusText));
    partial void OnDetailChanged(string value) => OnPropertyChanged(nameof(StatusText));
}

/// <summary>Auswahl-Eintrag im NEW PC SETUP.</summary>
public sealed partial class SoftwareChoice(SoftwarePackage package) : ObservableObject
{
    public SoftwarePackage Package { get; } = package;
    public string Name => Package.Name;
    public string Category => Package.Category;
    public string Description => Package.Description;
    public string SourceText => Package.Source == PackageSource.MicrosoftStore ? "Microsoft Store" : $"winget · {Package.PackageId}";

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _installedVersion;
    [ObservableProperty] private bool _isRecommended;
    [ObservableProperty] private string _status = "–";
    [ObservableProperty] private DataState _state = DataState.NotAvailable;

    public bool IsInstalled => InstalledVersion is not null;
    public string InstalledText => InstalledVersion is null ? "nicht installiert" : InstalledVersion.Length == 0 ? "installiert" : $"installiert ({InstalledVersion})";

    partial void OnInstalledVersionChanged(string? value) { OnPropertyChanged(nameof(IsInstalled)); OnPropertyChanged(nameof(InstalledText)); }
}
