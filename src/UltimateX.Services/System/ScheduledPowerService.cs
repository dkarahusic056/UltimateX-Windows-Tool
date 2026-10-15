using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.Power;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.SystemTools;

/// <summary>
/// Zeitgesteuerte Energieaktionen (v1.3).
///  - Herunterfahren/Neustart: Windows plant selbst (shutdown.exe /s bzw. /r /t &lt;Sekunden&gt;, Allowlist ShutdownAt/RestartAt) –
///    läuft auch weiter, wenn ULTIMATE X beendet wird. Abbrechen über AbortShutdown (shutdown /a).
///  - Sperren/Abmelden/Energiesparen: App-interner Timer (ViewModel) ruft beim Fälligwerden <see cref="ExecuteInApp"/> auf.
/// Die Planung wird in power-schedule.json gespeichert (nur Anzeige nach App-Neustart, keine Nachholung verpasster Aktionen).
/// </summary>
public sealed class ScheduledPowerService(PowerService power, PowerScheduleStore? store = null)
{
    private readonly PowerScheduleStore _store = store ?? new PowerScheduleStore();

    /// <summary>Aktive Planung laden. Abgelaufene Einträge werden entfernt; <paramref name="missed"/> = verpasste App-interne Aktion.</summary>
    public PowerScheduleEntry? LoadActive(out PowerScheduleEntry? missed)
    {
        missed = null;
        var e = _store.Load();
        if (e is null) return null;
        if (PowerScheduleMath.IsStillActive(e, DateTime.UtcNow)) return e;
        if (!e.SystemScheduled) missed = e;
        _store.Clear();
        return null;
    }

    /// <summary>Plant eine Aktion. Eine vorhandene Planung von ULTIMATE X wird vorher abgebrochen (Windows erlaubt nur eine).</summary>
    public async Task<(OperationResult Result, PowerScheduleEntry? Entry)> ScheduleAsync(ScheduledPowerAction action, DateTime dueUtc,
        PowerScheduleEntry? existing, CancellationToken ct = default)
    {
        if (!PowerScheduleMath.TrySecondsUntil(dueUtc, DateTime.UtcNow, out var seconds) || seconds < 1)
            return (OperationResult.Fail("Der Zeitpunkt muss in der Zukunft und höchstens 7 Tage entfernt liegen."), null);

        if (existing is { SystemScheduled: true }) await AbortSystemAsync(ct).ConfigureAwait(false);

        var system = PowerScheduleEntry.UsesShutdownExe(action);
        if (system)
        {
            var id = action == ScheduledPowerAction.Restart ? AllowedCommand.RestartAt : AllowedCommand.ShutdownAt;
            var spec = CommandCatalog.Build(id, seconds.ToString(CultureInfo.InvariantCulture));
            var r = await ProcessRunner.RunAsync(spec, null, ct).ConfigureAwait(false);
            AppLog.Info("Power", spec.DisplayName, $"in {seconds}s, ExitCode={r.ExitCode}");
            if (r.ExitCode != 0)
            {
                if (existing is { SystemScheduled: true }) _store.Clear(); // alte Windows-Planung wurde bereits abgebrochen
                // 1190 = ERROR_SHUTDOWN_IS_SCHEDULED: bereits von einem anderen Programm geplant.
                return (OperationResult.Fail(r.ExitCode == 1190
                    ? "Windows hat bereits ein Herunterfahren/Neustart geplant (evtl. durch ein anderes Programm). Zuerst „Abbrechen“ wählen."
                    : $"{spec.DisplayName} konnte nicht geplant werden.", r.Output), null);
            }
        }

        var entry = new PowerScheduleEntry(Guid.NewGuid(), action, dueUtc, DateTime.UtcNow, system);
        _store.Save(entry);
        AppLog.Info("Power", "Energieaktion geplant", $"{action} um {dueUtc:O} ({(system ? "Windows" : "App-Timer")})");
        return (OperationResult.Ok($"{PowerScheduleMath.ActionLabel(action)} geplant."), entry);
    }

    /// <summary>Bricht die Planung ab (bei Herunterfahren/Neustart zusätzlich shutdown /a).</summary>
    public async Task<OperationResult> CancelAsync(PowerScheduleEntry? entry, CancellationToken ct = default)
    {
        _store.Clear();
        if (entry is null) return OperationResult.Ok("Keine Planung aktiv.");
        if (entry.SystemScheduled)
        {
            var r = await AbortSystemAsync(ct).ConfigureAwait(false);
            AppLog.Info("Power", "Geplante Energieaktion abgebrochen", entry.Action.ToString());
            return r.Success ? OperationResult.Ok("Geplante Aktion abgebrochen.")
                : OperationResult.Ok("Planung entfernt. Windows meldet kein geplantes Herunterfahren mehr (evtl. bereits anderweitig abgebrochen).");
        }
        AppLog.Info("Power", "Geplante Energieaktion abgebrochen", entry.Action.ToString());
        return OperationResult.Ok("Geplante Aktion abgebrochen.");
    }

    /// <summary>Verschiebt die Planung (neue Planung mit gleicher Aktion; bei Windows-Planung: /a und neu /t).</summary>
    public Task<(OperationResult Result, PowerScheduleEntry? Entry)> PostponeAsync(PowerScheduleEntry entry, TimeSpan delta, CancellationToken ct = default)
    {
        if (!PowerScheduleMath.TryPostpone(entry.DueUtc, delta, DateTime.UtcNow, out var due))
            return Task.FromResult<(OperationResult, PowerScheduleEntry?)>((OperationResult.Fail("Höchstens 7 Tage im Voraus."), entry));
        return ScheduleAsync(entry.Action, due, entry, ct);
    }

    /// <summary>Führt eine fällige App-interne Aktion aus (Sperren/Abmelden/Energiesparen) und entfernt die Planung.</summary>
    public OperationResult ExecuteInApp(PowerScheduleEntry entry)
    {
        _store.Clear();
        AppLog.Info("Power", "Geplante Energieaktion ausgeführt", entry.Action.ToString());
        return entry.Action switch
        {
            ScheduledPowerAction.Lock => power.Lock(),
            ScheduledPowerAction.SignOut => power.SignOut(),
            ScheduledPowerAction.Sleep => power.Sleep(),
            _ => OperationResult.Ok("Wird von Windows ausgeführt."),
        };
    }

    /// <summary>Planung ist erledigt (z. B. Windows führt Herunterfahren aus) – nur den Eintrag entfernen.</summary>
    public void Forget() => _store.Clear();

    private async Task<OperationResult> AbortSystemAsync(CancellationToken ct)
    {
        var r = await ProcessRunner.RunAsync(CommandCatalog.Build(AllowedCommand.AbortShutdown), null, ct).ConfigureAwait(false);
        return r.ExitCode == 0 ? OperationResult.Ok("Abgebrochen.") : OperationResult.Fail("Kein geplantes Herunterfahren aktiv.");
    }
}
