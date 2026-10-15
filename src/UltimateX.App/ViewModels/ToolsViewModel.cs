using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Power;
using UltimateX.Services.SystemTools;

namespace UltimateX.App.ViewModels;

/// <summary>Auswahl einer zeitgesteuerten Energieaktion.</summary>
public sealed record PowerActionOption(ScheduledPowerAction Action, string Label, string Note);

/// <summary>Einheit für „in X …“.</summary>
public sealed record DelayUnitOption(PowerDelayUnit Unit, string Label);

/// <summary>CMD-/PowerShell-Tools und Windows Power Control (§36, §38) inkl. zeitgesteuerter Energieaktionen (v1.3).</summary>
public sealed partial class ToolsViewModel : PageViewModel
{
    public override string Title => "Tools & Power";
    public override string Glyph => "\uE756";
    public override string Keywords => "tools werkzeuge cmd powershell terminal taskmanager geräte-manager datenträgerverwaltung msinfo herunterfahren neustart shutdown restart sperren energiesparen abmelden timer zeitgesteuert geplant uhrzeit countdown sleep timer";

    public IReadOnlyList<ToolDefinition> AllTools => ToolLauncher.All;

    [ObservableProperty] private bool _countdownEnabled = AppServices.Settings.ShutdownCountdownEnabled;
    [ObservableProperty] private bool _shutdownScheduled;

    // ---------------- v1.3 Zeitgesteuerte Energieaktionen ----------------

    private static readonly IReadOnlyList<PowerActionOption> ActionOptions =
    [
        new(ScheduledPowerAction.Shutdown, "Herunterfahren", "Windows plant selbst – läuft auch weiter, wenn ULTIMATE X beendet wird."),
        new(ScheduledPowerAction.Restart, "Neustart", "Windows plant selbst – läuft auch weiter, wenn ULTIMATE X beendet wird."),
        new(ScheduledPowerAction.Lock, "Sperren", "Nur solange ULTIMATE X läuft (auch minimiert)."),
        new(ScheduledPowerAction.SignOut, "Abmelden", "Nur solange ULTIMATE X läuft (auch minimiert)."),
        new(ScheduledPowerAction.Sleep, "Energiesparen", "Nur solange ULTIMATE X läuft (auch minimiert)."),
    ];

    private static readonly IReadOnlyList<DelayUnitOption> UnitOptions =
    [
        new(PowerDelayUnit.Minutes, "Minuten"),
        new(PowerDelayUnit.Hours, "Stunden"),
    ];

    public IReadOnlyList<PowerActionOption> PowerActions => ActionOptions;
    public IReadOnlyList<DelayUnitOption> DelayUnits => UnitOptions;

    [ObservableProperty] private PowerActionOption _selectedPowerAction = ActionOptions[0];
    [ObservableProperty] private bool _useDelay = true;
    [ObservableProperty] private bool _useClock;
    [ObservableProperty] private string _delayAmount = "30";
    [ObservableProperty] private DelayUnitOption _selectedDelayUnit = UnitOptions[0];
    [ObservableProperty] private string _clockText = "23:00";
    [ObservableProperty] private bool _clockToday = true;
    [ObservableProperty] private bool _clockTomorrow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSchedule))]
    private PowerScheduleEntry? _scheduled;

    [ObservableProperty] private string? _scheduleText;
    [ObservableProperty] private string? _remainingText;
    [ObservableProperty] private string? _scheduleNote;

    /// <summary>Hinweis auf eine verpasste App-interne Aktion (App war zum Zeitpunkt geschlossen) – wird nicht nachgeholt.</summary>
    [ObservableProperty] private string? _missedNote;

    public bool HasSchedule => Scheduled is not null;

    private readonly DispatcherTimer _timer;
    private bool _warnedFive;
    private bool _warnedOne;
    private bool _warningOpen;
    private bool _executing;

    public ToolsViewModel()
    {
        // Gespeicherte Planung wiederherstellen (Anzeige nach App-Neustart).
        var active = AppServices.ScheduledPower.LoadActive(out var missed);
        if (active is not null) SetScheduled(active);
        else if (missed is not null)
            MissedNote = $"Die geplante Aktion „{PowerScheduleMath.ActionLabel(missed.Action)}“ ({Local(missed.DueUtc)}) wurde nicht ausgeführt, weil ULTIMATE X zu diesem Zeitpunkt nicht lief.";

        // Der Timer läuft unabhängig von der geöffneten Seite (das ViewModel lebt so lange wie das Hauptfenster).
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    protected override Task LoadAsync() => Task.CompletedTask;

    partial void OnUseDelayChanged(bool value)
    {
        if (value) UseClock = false;
    }

    partial void OnUseClockChanged(bool value)
    {
        if (value) UseDelay = false;
    }

    partial void OnClockTodayChanged(bool value)
    {
        if (value) ClockTomorrow = false;
    }

    partial void OnClockTomorrowChanged(bool value)
    {
        if (value) ClockToday = false;
    }

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
        // shutdown /a bricht auch eine zeitgesteuerte Windows-Planung ab → Anzeige angleichen.
        if (Scheduled is { SystemScheduled: true })
        {
            AppServices.ScheduledPower.Forget();
            SetScheduled(null);
        }
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

    // ---------------- v1.3 Planen / Abbrechen / Verschieben ----------------

    [RelayCommand]
    private async Task SchedulePowerAsync()
    {
        var action = SelectedPowerAction.Action;
        DateTime dueUtc;
        var nowUtc = DateTime.UtcNow;
        if (UseClock)
        {
            if (!PowerScheduleMath.TryParseClock(ClockText?.Trim(), out var h, out var m))
            {
                StatusMessage = "Bitte eine Uhrzeit im Format HH:mm eingeben (z. B. 22:30).";
                return;
            }
            if (!PowerScheduleMath.TryResolveClock(nowUtc, TimeZoneInfo.Local, h, m, ClockTomorrow, out dueUtc, out var error))
            {
                StatusMessage = error;
                return;
            }
        }
        else
        {
            if (!int.TryParse(DelayAmount?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var amount)
                || !PowerScheduleMath.TryDelaySeconds(amount, SelectedDelayUnit.Unit, out var secs))
            {
                StatusMessage = "Bitte eine ganze Zahl eingeben: 1–10080 Minuten bzw. 1–168 Stunden (höchstens 7 Tage).";
                return;
            }
            dueUtc = nowUtc.AddSeconds(secs);
        }

        var label = PowerScheduleMath.ActionLabel(action);
        var remaining = PowerScheduleMath.FormatRemaining(dueUtc - nowUtc);
        var how = PowerScheduleEntry.UsesShutdownExe(action)
            ? "Windows plant die Aktion selbst – sie läuft auch dann, wenn ULTIMATE X beendet wird. Zum Zeitpunkt schließt Windows alle Programme " +
              "zwangsweise; ungespeicherte Arbeit geht dabei verloren."
            : "Die Aktion wird nur ausgeführt, solange ULTIMATE X läuft (auch minimiert).";
        var replace = Scheduled is not null ? $"\n\nDie bisherige Planung ({PowerScheduleMath.ActionLabel(Scheduled.Action)}) wird ersetzt." : "";
        if (!Dialogs.Confirm($"{label} planen",
                $"{label} um {Local(dueUtc)} (in {remaining})?\n\n{how}\n\nBitte speichern Sie vorher alle offenen Arbeiten. " +
                $"5 Minuten und 1 Minute vorher erscheint eine Warnung mit Abbrechen/Verschieben.{replace}", true))
            return;

        MissedNote = null;
        IsBusy = true;
        try
        {
            var (r, entry) = await AppServices.ScheduledPower.ScheduleAsync(action, dueUtc, Scheduled);
            StatusMessage = r.Message;
            if (entry is not null) SetScheduled(entry);
            else SetScheduled(AppServices.ScheduledPower.LoadActive(out _));
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task CancelScheduleAsync()
    {
        if (Scheduled is not { } entry) return;
        var r = await AppServices.ScheduledPower.CancelAsync(entry);
        StatusMessage = r.Message;
        SetScheduled(null);
    }

    [RelayCommand]
    private Task Postpone15Async() => PostponeAsync(TimeSpan.FromMinutes(15));

    [RelayCommand]
    private Task Postpone60Async() => PostponeAsync(TimeSpan.FromHours(1));

    private async Task PostponeAsync(TimeSpan delta)
    {
        if (Scheduled is not { } entry) return;
        var (r, updated) = await AppServices.ScheduledPower.PostponeAsync(entry, delta);
        if (updated is not null && r.Success)
        {
            SetScheduled(updated);
            StatusMessage = $"Verschoben auf {Local(updated.DueUtc)}.";
        }
        else
        {
            StatusMessage = r.Message;
            SetScheduled(AppServices.ScheduledPower.LoadActive(out _));
        }
    }

    private void SetScheduled(PowerScheduleEntry? entry)
    {
        Scheduled = entry;
        if (entry is null)
        {
            ScheduleText = null;
            RemainingText = null;
            ScheduleNote = null;
            return;
        }
        (_warnedFive, _warnedOne) = PowerScheduleMath.InitialWarnings(entry.DueUtc - DateTime.UtcNow);
        ScheduleText = $"{PowerScheduleMath.ActionLabel(entry.Action)} geplant um {Local(entry.DueUtc)}";
        ScheduleNote = entry.SystemScheduled
            ? "Von Windows geplant (shutdown.exe) – läuft auch ohne ULTIMATE X. Programme werden zum Zeitpunkt zwangsweise geschlossen: ungespeicherte Arbeit sichern!"
            : "App-interner Timer – wird nur ausgeführt, solange ULTIMATE X läuft. Ungespeicherte Arbeit sichern!";
        UpdateRemaining();
    }

    private void UpdateRemaining()
    {
        if (Scheduled is { } e) RemainingText = "noch " + PowerScheduleMath.FormatRemaining(e.DueUtc - DateTime.UtcNow);
    }

    private void OnTick()
    {
        if (Scheduled is not { } entry || _executing) return;
        var remaining = entry.DueUtc - DateTime.UtcNow;
        UpdateRemaining();

        if (remaining <= TimeSpan.Zero)
        {
            _executing = true;
            try
            {
                if (entry.SystemScheduled)
                {
                    AppServices.ScheduledPower.Forget(); // Windows führt die Aktion aus.
                    SetScheduled(null);
                }
                else
                {
                    SetScheduled(null);
                    ShowResult(AppServices.ScheduledPower.ExecuteInApp(entry));
                }
            }
            finally { _executing = false; }
            return;
        }

        var stage = PowerScheduleMath.WarningDue(remaining, _warnedFive, _warnedOne);
        if (stage == PowerWarningStage.None || _warningOpen) return;
        if (stage == PowerWarningStage.OneMinute) _warnedOne = _warnedFive = true;
        else _warnedFive = true;
        _ = WarnAsync(entry, stage);
    }

    /// <summary>Vorwarnung 5 bzw. 1 Minute vorher: Fenster nach vorn holen, Abbrechen oder Verschieben anbieten.</summary>
    private async Task WarnAsync(PowerScheduleEntry entry, PowerWarningStage stage)
    {
        _warningOpen = true;
        try
        {
            BringToFront();
            var label = PowerScheduleMath.ActionLabel(entry.Action);
            var when = stage == PowerWarningStage.OneMinute ? "in 1 Minute" : "in 5 Minuten";
            var title = $"{label} {when}";
            var text = $"{label} {when} (um {Local(entry.DueUtc)}).\n\nBitte jetzt ungespeicherte Arbeit sichern.\n\n";
            if (Dialogs.Confirm(title, text + "Geplante Aktion ABBRECHEN?", true))
            {
                if (Scheduled?.Id == entry.Id) await CancelScheduleAsync();
                return;
            }
            if (Scheduled?.Id != entry.Id) return; // inzwischen ausgeführt/geändert
            if (Dialogs.Confirm(title, "Um 15 Minuten verschieben?\n\n„Nein“ = wie geplant ausführen."))
            {
                if (Scheduled?.Id == entry.Id) await PostponeAsync(TimeSpan.FromMinutes(15));
            }
        }
        finally { _warningOpen = false; }
    }

    private static void BringToFront()
    {
        if (Application.Current?.MainWindow is not { } w) return;
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        if (!w.IsVisible) w.Show();
        w.Activate();
    }

    private static string Local(DateTime utc)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
        var today = DateTime.Now.Date;
        var day = local.Date == today ? "heute" : local.Date == today.AddDays(1) ? "morgen" : local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return $"{local:HH:mm} Uhr ({day})";
    }
}
