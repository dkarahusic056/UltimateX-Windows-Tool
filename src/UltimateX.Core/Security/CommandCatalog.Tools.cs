using UltimateX.Core.Common;

namespace UltimateX.Core.Security;

/// <summary>
/// v1.3 Werkzeuge (Geekbench). ULTIMATE X liefert Geekbench NICHT mit, lädt nichts herunter und lädt nichts hoch –
/// es startet nur ein bereits installiertes Geekbench aus einem festen, nur von Administratoren beschreibbaren Ort.
/// </summary>
public static partial class CommandCatalog
{
    /// <summary>
    /// Feste Liste der möglichen Geekbench-Programme (höchste Version zuerst). Ausschließlich unter %ProgramFiles% –
    /// keine Suche in Benutzerordnern, keine Pfade aus Benutzereingaben.
    /// </summary>
    public static IReadOnlyList<string> GeekbenchCandidates
    {
        get
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return
            [
                Path.Combine(pf, "Geekbench 6", "Geekbench 6.exe"),
                Path.Combine(pf, "Geekbench 5", "Geekbench 5.exe"),
            ];
        }
    }

    /// <summary>Installiertes Geekbench (höchste Version) oder null, wenn keines installiert ist.</summary>
    public static string? FindInstalledGeekbench() => GeekbenchCandidates.FirstOrDefault(File.Exists);

    /// <summary>Hauptversion (6 oder 5) eines Pfads aus <see cref="GeekbenchCandidates"/> – sonst null.</summary>
    public static int? GeekbenchVersionOf(string? path)
    {
        if (path is null) return null;
        var list = GeekbenchCandidates;
        if (string.Equals(path, list[0], StringComparison.OrdinalIgnoreCase)) return 6;
        if (string.Equals(path, list[1], StringComparison.OrdinalIgnoreCase)) return 5;
        return null;
    }

    /// <summary>Feste Ablage des NVIDIA-Treiberpakets (v1.3). Gestartet wird es nur von der App nach Signaturprüfung – nie vom Helper.</summary>
    public static string NvidiaDriverDirectory => Path.Combine(AppPaths.UserDataRoot, "setup", "nvidia");
    public static string NvidiaDriverPath => Path.Combine(NvidiaDriverDirectory, "nvidia-driver.exe");

    private static CommandSpec? BuildToolCommand(AllowedCommand id, string? parameter) => id switch
    {
        // Ohne Parameter: NVIDIA-Installer mit eigener Oberfläche (Express/Benutzerdefiniert, Lizenz) – nichts wird still installiert.
        AllowedCommand.NvidiaDriverInstall => parameter is null
            ? new CommandSpec(id, NvidiaDriverPath, [], true, true, "NVIDIA-Grafiktreiber installieren")
            : throw new ArgumentException("Der NVIDIA-Treiber wird ohne Parameter gestartet.", nameof(parameter)),
        // Ohne Parameter, ohne Erhöhung, interaktiv (eigenes Fenster). Ist kein Geekbench installiert, liefert Build den festen
        // erwarteten Pfad (Geekbench 6) – der Aufrufer prüft vor dem Start File.Exists (ProcessRunner.StartDetached ebenfalls).
        AllowedCommand.GeekbenchLaunch => parameter is null
            ? new CommandSpec(id, FindInstalledGeekbench() ?? GeekbenchCandidates[0], [], false, true, "Geekbench starten (Primate Labs)")
            : throw new ArgumentException("Geekbench wird ohne Parameter gestartet.", nameof(parameter)),
        _ => null,
    };
}
