using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using UltimateX.Core.NetSec;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.Services.NetSec;

public sealed record ScheduledTaskInfo(
    string Path,
    string Name,
    string Folder,
    bool Enabled,
    string State,
    string Triggers,
    DateTime? LastRun,
    DateTime? NextRun,
    int? LastResult,
    string? Program,
    string? Arguments,
    string? Author,
    SignatureInfo? Signature,
    TaskMarker Marker,
    DataState MarkerState)
{
    public bool IsMicrosoftWindows => ScheduledTaskLogic.IsMicrosoftWindowsTask(Path);

    public string MarkerText => ScheduledTaskLogic.Label(Marker);

    public string Publisher => Signature?.Publisher ?? (Signature is null ? "–" : Signature.StatusText);

    public string LastResultText => LastResult is { } r ? (r == 0 ? "OK (0)" : $"0x{r:X8}") : "–";
}

public sealed record ScheduledTaskScan(IReadOnlyList<ScheduledTaskInfo> Tasks, int SkippedFolders, string? Error);

/// <summary>
/// v1.3 Geplante Aufgaben: Auflistung über die COM-Schnittstelle „Schedule.Service“ (Late Binding, keine Interop-Assembly).
/// Ändern (Aktivieren/Deaktivieren) nur über die Allowlist (schtasks /Change, erhöht). Es wird nichts gelöscht.
/// </summary>
public sealed class ScheduledTaskService
{
    private const int TaskEnumHidden = 1;
    private const int TaskActionExec = 0;

    public ScheduledTaskScan GetTasks(IProgress<int>? progress, CancellationToken ct)
    {
        var list = new List<ScheduledTaskInfo>();
        var skipped = 0;
        var signatureCache = new Dictionary<string, SignatureInfo>(StringComparer.OrdinalIgnoreCase);
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
        IReadOnlyList<string> writable =
        [
            System.IO.Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ];

        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null) return new ScheduledTaskScan(list, 0, "Die Aufgabenplanung ist nicht verfügbar.");
        object? serviceObj = null;
        try
        {
            serviceObj = Activator.CreateInstance(type);
            if (serviceObj is null) return new ScheduledTaskScan(list, 0, "Die Aufgabenplanung ist nicht verfügbar.");
            dynamic service = serviceObj;
            service.Connect();
            var folders = new Stack<dynamic>();
            folders.Push(service.GetFolder("\\"));
            while (folders.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var folder = folders.Pop();
                try
                {
                    dynamic tasks = folder.GetTasks(TaskEnumHidden);
                    int count = tasks.Count;
                    for (int i = 1; i <= count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        ScheduledTaskInfo? info = ReadTask((object)tasks[i], signatureCache, writable, windowsDir, system32);
                        if (info is not null)
                        {
                            list.Add(info);
                            progress?.Report(list.Count);
                        }
                    }
                    dynamic subs = folder.GetFolders(0);
                    int subCount = subs.Count;
                    for (int i = 1; i <= subCount; i++) folders.Push(subs[i]);
                }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException or InvalidCastException)
                {
                    skipped++; // Ordner ohne Leserecht (z. B. andere Benutzer) – überspringen
                }
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException or InvalidCastException)
        {
            AppLog.Warn("ScheduledTasks", "Aufgabenplanung nicht lesbar", ex.Message);
            return new ScheduledTaskScan(list, skipped, "Aufgabenplanung konnte nicht gelesen werden: " + ex.Message);
        }
        finally
        {
            if (serviceObj is not null && Marshal.IsComObject(serviceObj)) Marshal.FinalReleaseComObject(serviceObj);
        }
        return new ScheduledTaskScan(list.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList(), skipped, null);
    }

    private static ScheduledTaskInfo? ReadTask(object taskObject, Dictionary<string, SignatureInfo> cache, IReadOnlyList<string> writable, string windowsDir, string system32)
    {
        dynamic task = taskObject;
        try
        {
            string path = task.Path;
            string name = task.Name;
            bool enabled = task.Enabled;
            int state = task.State;
            DateTime? last = ToDate((object?)task.LastRunTime);
            DateTime? next = ToDate((object?)task.NextRunTime);
            int? lastResult = null;
            try { lastResult = (int)task.LastTaskResult; }
            catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException) { }

            string? program = null, arguments = null, author = null, rawProgram = null;
            var triggers = new List<string>();
            try
            {
                dynamic def = task.Definition;
                try { author = (string?)def.RegistrationInfo.Author; }
                catch (Exception ex) when (ex is COMException or RuntimeBinderException or InvalidCastException) { }

                dynamic actions = def.Actions;
                int actionCount = actions.Count;
                for (int i = 1; i <= actionCount; i++)
                {
                    dynamic action = actions[i];
                    int actionType = action.Type;
                    if (actionType != TaskActionExec) continue;
                    rawProgram = (string?)action.Path;
                    arguments = (string?)action.Arguments;
                    break; // erste Programm-Aktion wird angezeigt/geprüft
                }

                dynamic trig = def.Triggers;
                int triggerCount = trig.Count;
                for (int i = 1; i <= triggerCount; i++)
                {
                    dynamic t = trig[i];
                    int tt = t.Type;
                    var text = ScheduledTaskLogic.TriggerText(tt);
                    if (!triggers.Contains(text)) triggers.Add(text);
                }
            }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException or InvalidCastException)
            {
                // Definition nicht lesbar (fehlende Rechte) – Grunddaten trotzdem anzeigen.
            }

            program = ScheduledTaskLogic.NormalizeProgramPath(rawProgram, Environment.ExpandEnvironmentVariables, system32, File.Exists);
            var exists = program is not null && System.IO.Path.IsPathFullyQualified(program) && File.Exists(program);
            SignatureInfo? sig = null;
            if (exists && program is not null)
            {
                if (!cache.TryGetValue(program, out sig))
                {
                    sig = SignatureVerifier.Verify(program);
                    cache[program] = sig;
                }
            }
            var marker = ScheduledTaskLogic.Classify(new TaskProgramFacts(program, exists, sig?.Status, sig?.Publisher), writable, windowsDir);
            var folderPath = path.Length > name.Length ? path[..^name.Length].TrimEnd('\\') : "";
            return new ScheduledTaskInfo(path, name, folderPath.Length == 0 ? "\\" : folderPath, enabled, ScheduledTaskLogic.StateText(state),
                triggers.Count == 0 ? "–" : string.Join(", ", triggers), last, next, lastResult, program ?? rawProgram, arguments, author, sig,
                marker, ScheduledTaskLogic.StateOf(marker, sig?.Status));
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or RuntimeBinderException or InvalidCastException)
        {
            return null;
        }
    }

    private static DateTime? ToDate(object? value) => value is DateTime dt ? ScheduledTaskLogic.ValidRunTime(dt) : null;

    /// <summary>Aktiviert/deaktiviert eine Aufgabe über schtasks (UAC). Der Pfad wird als geprüftes Base64url-Token übergeben.</summary>
    public static async Task<OperationResult> SetEnabledAsync(string taskPath, bool enable, CancellationToken ct)
    {
        string token;
        try { token = ScheduledTaskLogic.EncodeToken(taskPath); }
        catch (ArgumentException ex) { return OperationResult.Fail("Diese Aufgabe kann nicht sicher geändert werden: " + ex.Message); }

        var r = await ElevatedCommandRunner.RunAsync(enable ? AllowedCommand.ScheduledTaskEnable : AllowedCommand.ScheduledTaskDisable, token, null, ct).ConfigureAwait(false);
        if (r.Cancelled) return OperationResult.Fail("Die Administratorfreigabe wurde abgelehnt.");
        AppLog.Info("ScheduledTasks", enable ? "Aufgabe aktiviert" : "Aufgabe deaktiviert", $"{taskPath} ExitCode={r.ExitCode}");
        return r.ExitCode == 0
            ? OperationResult.Ok(enable ? "Aufgabe aktiviert." : "Aufgabe deaktiviert.", r.Output)
            : OperationResult.Fail($"Änderung fehlgeschlagen (Exit {r.ExitCode}).", r.Output);
    }
}
