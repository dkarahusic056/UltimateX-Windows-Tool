using UltimateX.Core.Security;

namespace UltimateX.Core.Tests;

internal static partial class CommandSamples
{
    private static readonly object BackupGate = new();

    /// <summary>Gültiges DriverExport-Token, dessen Ordner im Test-Stammordner existiert.</summary>
    internal const string DriverSampleToken = "Z-20260101-120000";
    internal const string DriverSampleStamp = "20260101-120000";

    private static bool Backup(AllowedCommand cmd, ref string? parameter)
    {
        switch (cmd)
        {
            case AllowedCommand.BackupSystemImage:
            case AllowedCommand.BackupImageVersions:
                parameter = NonSystemDrive();
                return true;
            case AllowedCommand.DriverExport:
                EnsureDriverExportTestRoot();
                parameter = DriverSampleToken;
                return true;
            case AllowedCommand.RecoveryDriveCreate:
                parameter = null;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Ein Laufwerksbuchstabe, der sicher nicht das Systemlaufwerk ist.</summary>
    internal static string NonSystemDrive() => CommandCatalog.SystemDriveLetter == 'Z' ? "Y" : "Z";

    /// <summary>
    /// DriverExport prüft im Build, dass der feste Zielordner existiert. Für Tests wird die Laufwerkswurzel auf einen festen
    /// Temp-Ordner umgelenkt (immer derselbe Wert → unschädlich bei parallel laufenden Tests). Der kleine, leere Ordnerbaum
    /// unter %TEMP%\UltimateX-Tests-DriverRoot bleibt bewusst liegen, weil andere Tests ihn gleichzeitig nutzen können.
    /// </summary>
    internal static string EnsureDriverExportTestRoot()
    {
        lock (BackupGate)
        {
            var root = Path.Combine(Path.GetTempPath(), "UltimateX-Tests-DriverRoot");
            Directory.CreateDirectory(Path.Combine(root, CommandCatalog.BackupRootFolderName, CommandCatalog.DriverBackupFolderName, DriverSampleStamp));
            CommandCatalog.DriverExportRootOverrideForTests = root;
            return root;
        }
    }
}
