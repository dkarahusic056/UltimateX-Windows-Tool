namespace UltimateX.Core.Common;

/// <summary>
/// Alle Speicherorte der Anwendung an einer Stelle.
/// Benutzerdaten liegen unter %LOCALAPPDATA%\UltimateX (kein Admin nötig),
/// Ausgaben des Admin-Helpers unter %ProgramData%\UltimateX\HelperRuns.
/// </summary>
public static class AppPaths
{
    public static string UserDataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UltimateX");

    public static string LogDirectory => Ensure(Path.Combine(UserDataRoot, "logs"));
    public static string BackupDirectory => Ensure(Path.Combine(UserDataRoot, "backups"));
    public static string SettingsFile => Path.Combine(Ensure(UserDataRoot), "settings.json");

    /// <summary>
    /// Wird ausschließlich vom erhöhten Admin-Helper (bzw. dem Installer) angelegt, nie von der normalen App.
    /// So kann ein Prozess im Benutzerkontext keinen Junction/Symlink an diese Stelle legen (siehe THREAT_MODEL T-07).
    /// </summary>
    public static string HelperRunParent { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "UltimateX");

    public static string HelperRunDirectory { get; } = Path.Combine(HelperRunParent, "HelperRuns");

    public static string InstallDirectory => AppContext.BaseDirectory;

    public static string System32 => Environment.GetFolderPath(Environment.SpecialFolder.System);

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
