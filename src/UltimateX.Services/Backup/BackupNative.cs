using System.Runtime.InteropServices;

namespace UltimateX.Services.Backup;

/// <summary>Win32-Aufrufe des Backup-Centers (dokumentierte, öffentliche API).</summary>
internal static class BackupNative
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    internal const int ERROR_FILE_NOT_FOUND = 2;
    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_NOT_SAME_DEVICE = 17;
    internal const int ERROR_INVALID_FUNCTION = 1;
    internal const int ERROR_ALREADY_EXISTS = 183;
    internal const int ERROR_TOO_MANY_LINKS = 1142;

    /// <summary>
    /// Legt <paramref name="newPath"/> als Hardlink auf <paramref name="existingPath"/> an. Funktioniert nur auf demselben NTFS-Volume
    /// und bis 1023 Links je Datei – sonst false (der Aufrufer kopiert dann). <paramref name="error"/> = Win32-Fehlercode.
    /// </summary>
    internal static bool TryCreateHardLink(string newPath, string existingPath, out int error)
    {
        error = 0;
        if (!OperatingSystem.IsWindows()) { error = ERROR_INVALID_FUNCTION; return false; }
        if (CreateHardLinkW(Long(newPath), Long(existingPath), IntPtr.Zero)) return true;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    /// <summary>Lange Pfade (≥ 260 Zeichen) für die Win32-API mit \\?\ kennzeichnen.</summary>
    private static string Long(string path)
        => path.Length >= 248 && !path.StartsWith(@"\\?\", StringComparison.Ordinal) && !path.StartsWith(@"\\", StringComparison.Ordinal)
            ? @"\\?\" + Path.GetFullPath(path)
            : path;
}
