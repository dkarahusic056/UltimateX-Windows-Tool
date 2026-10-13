using System.Collections.ObjectModel;
using System.Windows;
using UltimateX.Core.Processes;
using UltimateX.Core.Security;

namespace UltimateX.App.Infrastructure;

public sealed record CommandHistoryEntry(DateTime Time, AllowedCommand Command, int ExitCode, string Output)
{
    public string Title => $"{Time:HH:mm:ss} · {Command} · Exit Code {ExitCode} (0x{ExitCode:X8})";
}

/// <summary>Letzte Admin-Befehle mit Exit Code und Originalausgabe (Technician Mode, §7, §87).</summary>
public static class CommandHistory
{
    public static ObservableCollection<CommandHistoryEntry> Entries { get; } = [];

    public static void Start()
    {
        ElevatedCommandRunner.CommandCompleted += (cmd, code, output) =>
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                Entries.Insert(0, new CommandHistoryEntry(DateTime.Now, cmd, code, output.Length > 100_000 ? output[^100_000..] : output));
                while (Entries.Count > 20) Entries.RemoveAt(Entries.Count - 1);
            });
    }
}
