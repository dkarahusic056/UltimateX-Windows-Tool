using System.Management;
using System.Text.Json;
using Microsoft.Win32;
using UltimateX.Core.Security;

namespace UltimateX.AdminHelper;

/// <summary>
/// Admin-Aktionen, die per Windows-API statt über eine EXE ausgeführt werden.
/// Jede Aktion validiert ihre Eingaben erneut und schreibt eine lesbare Zusammenfassung in die Ausgabe.
/// Maschinenlesbare Ergebnisse stehen in einer Zeile mit Präfix "##RESULT##".
/// </summary>
internal static class InternalActions
{
    public const string ResultPrefix = "##RESULT##";

    public static int Run(CommandSpec spec, IProgress<string> output)
    {
        try
        {
            return spec.Id switch
            {
                AllowedCommand.ReadSmart => ReadSmart(output),
                AllowedCommand.CreateRestorePoint => CreateRestorePoint(output),
                AllowedCommand.SetMachineStartup => SetMachineStartup(spec.Parameter!, output),
                AllowedCommand.InstallWindowsUpdate => InstallWindowsUpdate(spec.Parameter!, output),
                AllowedCommand.ReadMinidumps => CrashDumps.ReadHeaders(output),
                AllowedCommand.AnalyzeMinidump => CrashDumps.Analyze(spec.Parameter!, output),
                _ => 9001,
            };
        }
        catch (Exception ex)
        {
            // Bewusst breit: Der Helper darf nie mit unbehandelter Ausnahme enden; der Fehler wird gemeldet.
            output.Report($"Fehler: {ex.GetType().Name}: {ex.Message}\n");
            return 1;
        }
    }

    // ---------------- SMART (§44) ----------------

    private static int ReadSmart(IProgress<string> output)
    {
        const string ns = @"root\Microsoft\Windows\Storage";
        var disks = Query(ns, "SELECT DeviceId, FriendlyName, MediaType, BusType, HealthStatus FROM MSFT_PhysicalDisk");
        var counters = Query(ns, "SELECT DeviceId, Temperature, TemperatureMax, PowerOnHours, Wear, ReadErrorsTotal, ReadErrorsUncorrected, WriteErrorsTotal, WriteErrorsUncorrected, StartStopCycleCount FROM MSFT_StorageReliabilityCounter");

        var result = new List<Dictionary<string, object?>>();
        foreach (var d in disks)
        {
            var id = d["DeviceId"]?.ToString();
            var c = counters.FirstOrDefault(x => x["DeviceId"]?.ToString() == id);
            var entry = new Dictionary<string, object?>(d);
            if (c is not null)
                foreach (var (k, v) in c) if (k != "DeviceId") entry[k] = v;
            result.Add(entry);
            output.Report($"{d["FriendlyName"]}: {(c is null ? "keine Zuverlässigkeitsdaten" : "Daten gelesen")}\n");
        }
        output.Report(ResultPrefix + JsonSerializer.Serialize(result) + "\n");
        return 0;
    }

    private static List<Dictionary<string, object?>> Query(string scope, string wql)
    {
        var list = new List<Dictionary<string, object?>>();
        using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(wql),
            new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(30), ReturnImmediately = true });
        using var results = searcher.Get();
        foreach (var o in results)
        {
            using (o)
            {
                var d = new Dictionary<string, object?>();
                foreach (var p in o.Properties) d[p.Name] = p.Value is Array ? null : p.Value;
                list.Add(d);
            }
        }
        return list;
    }

    // ---------------- Wiederherstellungspunkt (§86) ----------------

    private static int CreateRestorePoint(IProgress<string> output)
    {
        using var cls = new ManagementClass(new ManagementScope(@"root\default"), new ManagementPath("SystemRestore"), null);
        using var inParams = cls.GetMethodParameters("CreateRestorePoint");
        inParams["Description"] = "ULTIMATE X – vor Systemänderung";
        inParams["RestorePointType"] = 12u;   // MODIFY_SETTINGS
        inParams["EventType"] = 100u;         // BEGIN_SYSTEM_CHANGE
        using var outParams = cls.InvokeMethod("CreateRestorePoint", inParams, null);
        var rc = Convert.ToInt32(outParams?["ReturnValue"] ?? -1, System.Globalization.CultureInfo.InvariantCulture);
        output.Report(rc == 0
            ? "Wiederherstellungspunkt angefordert. Hinweis: Windows legt höchstens einen Punkt pro 24 Stunden an; ein bereits vorhandener aktueller Punkt bleibt gültig.\n"
            : $"Wiederherstellungspunkt konnte nicht erstellt werden (Code {rc}). Ist der Computerschutz für das Systemlaufwerk aktiviert?\n");
        return rc == 0 ? 0 : 2;
    }

    // ---------------- Autostart für alle Benutzer (§35) ----------------

    private static int SetMachineStartup(string token, IProgress<string> output)
    {
        if (!CommandCatalog.TryDecodeStartupToken(token, out var enable, out var source, out var name)) return 9001;

        // Der Eintrag muss tatsächlich existieren – es werden nur bestehende Einträge umgeschaltet, nie neue angelegt.
        bool exists = source switch
        {
            1 => ValueExists(RegistryView.Registry64, name),
            2 => ValueExists(RegistryView.Registry32, name),
            _ => File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Path.GetFileName(name)))
                 && Path.GetFileName(name) == name,
        };
        if (!exists)
        {
            output.Report("Autostart-Eintrag nicht gefunden.\n");
            return 3;
        }

        var sub = source switch { 1 => "Run", 2 => "Run32", _ => "StartupFolder" };
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = root.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{sub}", writable: true);
        var data = new byte[12];
        if (enable) data[0] = 0x02;
        else
        {
            data[0] = 0x03;
            BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        }
        key.SetValue(name, data, RegistryValueKind.Binary);
        output.Report($"Autostart „{name}“ {(enable ? "aktiviert" : "deaktiviert")}.\n");
        return 0;
    }

    private static bool ValueExists(RegistryView view, string name)
    {
        using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using var run = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
        return run?.GetValueNames().Contains(name, StringComparer.Ordinal) == true;
    }

    // ---------------- Windows Update (Treiber/Updates, §31, §32) ----------------

    /// <summary>
    /// Installiert GENAU EIN Update, das Windows Update für diesen PC anbietet (Suche per UpdateID).
    /// Quelle ist ausschließlich Microsoft Windows Update; Pakete sind von Microsoft signiert und
    /// für die Hardware-ID freigegeben. Der Benutzer hat der Installation (inkl. Lizenzbedingungen) in der App zugestimmt.
    /// </summary>
    private static int InstallWindowsUpdate(string updateId, IProgress<string> output)
    {
        if (!Guid.TryParseExact(updateId, "D", out _)) return 9001;

        var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session") ?? throw new InvalidOperationException("Windows Update Agent nicht verfügbar.");
        var collType = Type.GetTypeFromProgID("Microsoft.Update.UpdateColl") ?? throw new InvalidOperationException("Windows Update Agent nicht verfügbar.");

        dynamic session = Activator.CreateInstance(sessionType)!;
        session.ClientApplicationID = "ULTIMATE X Windows Control";
        dynamic searcher = session.CreateUpdateSearcher();
        output.Report("Suche Update bei Windows Update …\n");
        // UpdateID ist als GUID validiert → kein Injection-Risiko im Suchkriterium.
        dynamic search = searcher.Search($"IsInstalled=0 and UpdateID='{updateId}'");
        if ((int)search.Updates.Count == 0)
        {
            output.Report("Update wird von Windows Update nicht (mehr) angeboten.\n");
            return 4;
        }

        dynamic update = search.Updates.Item(0);
        output.Report($"Update: {update.Title}\n");
        if (!(bool)update.EulaAccepted) update.AcceptEula();

        dynamic coll = Activator.CreateInstance(collType)!;
        coll.Add(update);

        output.Report("Download …\n");
        dynamic downloader = session.CreateUpdateDownloader();
        downloader.Updates = coll;
        dynamic dl = downloader.Download();
        if ((int)dl.ResultCode is not (2 or 3))
        {
            output.Report($"Download fehlgeschlagen (ResultCode {(int)dl.ResultCode}).\n");
            return 5;
        }

        output.Report("Installation …\n");
        dynamic installer = session.CreateUpdateInstaller();
        installer.Updates = coll;
        dynamic ir = installer.Install();
        int code = ir.ResultCode;
        bool reboot = ir.RebootRequired;
        output.Report(code switch
        {
            2 => "Erfolgreich installiert.",
            3 => "Installiert, mit Fehlern.",
            4 => "Installation fehlgeschlagen.",
            5 => "Installation abgebrochen.",
            _ => $"Ergebnis unklar (ResultCode {code}).",
        } + (reboot ? " Ein Neustart ist erforderlich.\n" : "\n"));
        output.Report($"{ResultPrefix}{{\"resultCode\":{code},\"rebootRequired\":{(reboot ? "true" : "false")}}}\n");
        return code is 2 or 3 ? 0 : 6;
    }
}
