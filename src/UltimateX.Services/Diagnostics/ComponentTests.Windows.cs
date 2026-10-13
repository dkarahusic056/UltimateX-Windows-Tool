using System.Diagnostics;
using System.Xml;
using UltimateX.Core.Common;
using UltimateX.Services.Common;

namespace UltimateX.Services.Diagnostics;

public sealed partial class ComponentTestService
{
    private sealed record SvcInfo(string Name, string DisplayName, string State, string StartMode)
    {
        public bool Running => State.Equals("Running", StringComparison.OrdinalIgnoreCase);
    }

    private Dictionary<string, SvcInfo>? _services;

    private SvcInfo? Svc(string name)
    {
        _services ??= Wmi.Query("SELECT Name, DisplayName, State, StartMode FROM Win32_Service").Rows
            .Where(r => r.Str("Name") is not null)
            .GroupBy(r => r.Str("Name")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => new SvcInfo(g.Key, g.First().Str("DisplayName") ?? g.Key, g.First().Str("State") ?? "?", g.First().Str("StartMode") ?? "?"),
                StringComparer.OrdinalIgnoreCase);
        return _services.TryGetValue(name, out var s) ? s : null;
    }

    // ------------------------------------------------------------------ Sicherheit (nur lesend)

    private ComponentTestResult FromSecurity(ComponentTestDefinition d, Func<string, bool> areaFilter, string okText, string emptyText)
    {
        var items = Context.Security.Value.Where(i => areaFilter(i.Area)).ToList();
        if (items.Count == 0) return R(d, TestStatus.NotAvailable, emptyText);
        var details = items.Select(i => $"{i.Area}: {i.Status}" + (i.Detail is null ? "" : $" – {i.Detail}")).ToList();
        details.Add("Nur lesende Prüfung – ULTIMATE X ändert keine Sicherheitseinstellungen.");
        var status = Worst(items.Select(i => i.State switch
        {
            DataState.Ok => TestStatus.Pass,
            DataState.Warning => TestStatus.Warning,
            DataState.Problem => TestStatus.Fail,
            _ => TestStatus.NotAvailable,
        }).Where(s => s != TestStatus.NotAvailable).DefaultIfEmpty(TestStatus.NotAvailable));
        var worst = items.Where(i => i.State is DataState.Problem or DataState.Warning).OrderBy(i => i.State == DataState.Problem ? 0 : 1).FirstOrDefault();
        return R(d, status, worst is null ? okText : $"{worst.Area}: {worst.Status}", details);
    }

    private ComponentTestResult TestDefender(ComponentTestDefinition d) =>
        FromSecurity(d, a => a is "Microsoft Defender" or "Virendefinitionen" or "Letzter Scan" or "Manipulationsschutz" or "Virenschutz",
            "Virenschutz aktiv und aktuell.", "Virenschutzstatus nicht ermittelbar.");

    private ComponentTestResult TestFirewall(ComponentTestDefinition d) =>
        FromSecurity(d, a => a.StartsWith("Firewall", StringComparison.Ordinal), "Firewall in allen Profilen aktiv.", "Firewallstatus nicht ermittelbar.");

    private ComponentTestResult TestSecurityCenter(ComponentTestDefinition d)
    {
        var r = FromSecurity(d, a => a is "Benutzerkontensteuerung (UAC)" or "BitLocker", "UAC aktiv, BitLocker geprüft.", "Sicherheitsfunktionen nicht ermittelbar.");
        var wsc = Svc("wscsvc");
        var details = r.Details.ToList();
        details.Insert(0, $"Windows-Sicherheitscenter (wscsvc): {(wsc is null ? "nicht vorhanden" : wsc.Running ? "läuft" : "läuft nicht")}");
        var status = r.Status;
        var summary = r.Summary;
        if (wsc is { Running: false })
        {
            status = Worst([status, TestStatus.Warning]);
            if (status == TestStatus.Warning && r.Status != TestStatus.Warning) summary = "Windows-Sicherheitscenter läuft nicht.";
        }
        return r with { Status = status, Summary = summary, Details = details };
    }

    // ------------------------------------------------------------------ Windows

    private static readonly (string Name, string Label, bool Critical)[] TestedServices =
    [
        ("wuauserv", "Windows Update", true),
        ("BITS", "Intelligenter Hintergrundübertragungsdienst", true),
        ("Audiosrv", "Windows-Audio", true),
        ("Dhcp", "DHCP-Client", true),
        ("Dnscache", "DNS-Client", true),
        ("EventLog", "Windows-Ereignisprotokoll", true),
        ("wscsvc", "Sicherheitscenter", true),
        ("WinDefend", "Microsoft Defender Antivirus", false),
        ("CryptSvc", "Kryptografiedienste", true),
        ("Schedule", "Aufgabenplanung", true),
        ("Winmgmt", "Windows-Verwaltungsinstrumentation", true),
        ("Spooler", "Druckwarteschlange", false),
    ];

    private ComponentTestResult TestServices(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        foreach (var (name, label, critical) in TestedServices)
        {
            var s = Svc(name);
            if (s is null)
            {
                details.Add($"{label} ({name}): nicht vorhanden");
                if (critical && name != "WinDefend") statuses.Add(TestStatus.Warning);
                continue;
            }
            var mode = s.StartMode switch { "Auto" => "Automatisch", "Manual" => "Manuell", "Disabled" => "Deaktiviert", _ => s.StartMode };
            details.Add($"{label} ({name}): {(s.Running ? "läuft" : "beendet")}, Start {mode}");
            if (s.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) statuses.Add(critical ? TestStatus.Fail : TestStatus.Warning);
            else if (!s.Running && s.StartMode.Equals("Auto", StringComparison.OrdinalIgnoreCase) && name is not ("BITS" or "wuauserv")) statuses.Add(TestStatus.Warning);
            else statuses.Add(TestStatus.Pass);
        }
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Mindestens ein wichtiger Windows-Dienst ist deaktiviert.",
            TestStatus.Warning => "Einige Dienste laufen nicht wie erwartet.",
            _ => "Alle wichtigen Windows-Dienste in Ordnung.",
        }, details);
    }

    private ComponentTestResult TestWindowsUpdate(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        var wu = Svc("wuauserv");
        details.Add($"Dienst wuauserv: {(wu is null ? "nicht vorhanden" : $"{(wu.Running ? "läuft" : "beendet")}, Start {wu.StartMode}")}");
        if (wu is null || wu.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) statuses.Add(TestStatus.Fail);

        var installed = EventProbe.Query("System", "Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and EventID=19", 60, 50);
        var failed = EventProbe.Query("System", "Provider[@Name='Microsoft-Windows-WindowsUpdateClient'] and EventID=20", 30, 50);
        if (installed is null) details.Add("Update-Verlauf: Protokoll nicht lesbar");
        else if (installed.Count == 0)
        {
            details.Add("Keine erfolgreiche Update-Installation in den letzten 60 Tagen protokolliert.");
            statuses.Add(TestStatus.Warning);
        }
        else details.Add($"Letzte erfolgreiche Installation: {installed[0].Time:dd.MM.yyyy HH:mm} ({installed.Count} in 60 Tagen)");
        if (failed is { Count: > 0 })
        {
            details.Add($"Fehlgeschlagene Installationen (30 Tage): {failed.Count}");
            statuses.Add(TestStatus.Warning);
        }
        var reboot = health.GetRebootStatus();
        if (reboot.RebootRequired)
        {
            details.Add("Neustart ausstehend: " + string.Join("; ", reboot.Reasons));
            statuses.Add(TestStatus.Warning);
        }
        if (statuses.Count == 0) statuses.Add(TestStatus.Pass);
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "Windows Update ist deaktiviert.",
            TestStatus.Warning => "Windows Update funktioniert, Hinweise beachten.",
            _ => "Windows Update arbeitet normal.",
        }, details);
    }

    private static ComponentTestResult TestEventLog(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var unreadable = 0;
        var critical = 0;
        foreach (var log in new[] { "System", "Application", "Setup" })
        {
            var crit = EventProbe.Query(log, "Level=1", 7, 500);
            var err = EventProbe.Query(log, "Level=2", 7, 2000);
            if (crit is null || err is null) { unreadable++; details.Add($"{log}: nicht lesbar"); continue; }
            critical += crit.Count;
            details.Add($"{log}: {crit.Count} kritisch, {err.Count}{(err.Count >= 2000 ? "+" : "")} Fehler (7 Tage)");
        }
        if (unreadable == 3) return R(d, TestStatus.Fail, "Ereignisprotokolle sind nicht lesbar.", details);
        if (unreadable > 0) return R(d, TestStatus.Warning, "Nicht alle Ereignisprotokolle sind lesbar.", details);
        return critical > 0
            ? R(d, TestStatus.Warning, $"{critical} kritische Ereignisse in 7 Tagen – Details unter „Absturzanalyse“.", details)
            : R(d, TestStatus.Pass, "Ereignisprotokolle lesbar, keine kritischen Ereignisse.", details);
    }

    private static ComponentTestResult TestWmi(ComponentTestDefinition d)
    {
        var details = new List<string>();
        var statuses = new List<TestStatus>();
        foreach (var (wql, scope, label) in new[]
                 {
                     ("SELECT Caption FROM Win32_OperatingSystem", Wmi.Cimv2, "root\\cimv2"),
                     ("SELECT FriendlyName FROM MSFT_PhysicalDisk", Wmi.Storage, "Storage"),
                 })
        {
            var sw = Stopwatch.StartNew();
            var r = Wmi.Query(wql, scope, 15);
            var ms = sw.Elapsed.TotalMilliseconds;
            details.Add($"{label}: {(r.Ok ? $"OK, {r.Rows.Count} Objekt(e) in {ms:0} ms" : $"Fehler – {r.Error}")}");
            statuses.Add(!r.Ok ? (label == "Storage" ? TestStatus.Warning : TestStatus.Fail) : ms > 5000 ? TestStatus.Warning : TestStatus.Pass);
        }
        var status = Worst(statuses);
        return R(d, status, status switch
        {
            TestStatus.Fail => "WMI antwortet nicht – das WMI-Repository ist möglicherweise beschädigt.",
            TestStatus.Warning => "WMI antwortet langsam oder teilweise nicht.",
            _ => "WMI/CIM antwortet normal.",
        }, details);
    }

    private ComponentTestResult TestSearch(ComponentTestDefinition d)
    {
        var s = Svc("WSearch");
        if (s is null) return R(d, TestStatus.NotAvailable, "Windows Search ist nicht installiert.");
        var details = new List<string> { $"Dienst WSearch: {(s.Running ? "läuft" : "beendet")}, Start {s.StartMode}" };
        if (s.StartMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase)) return R(d, TestStatus.Warning, "Windows Search ist deaktiviert (Suche im Startmenü/Explorer langsamer).", details);
        if (!s.Running) return R(d, TestStatus.Warning, "Windows Search läuft nicht.", details);
        return R(d, TestStatus.Pass, "Windows Search läuft.", details);
    }

    private async Task<ComponentTestResult> TestSystemTimeAsync(ComponentTestDefinition d, CancellationToken ct)
    {
        var w32 = Svc("W32Time");
        var details = new List<string>
        {
            $"Lokale Zeit: {DateTime.Now:dd.MM.yyyy HH:mm:ss} ({TimeZoneInfo.Local.DisplayName})",
            $"Zeitdienst W32Time: {(w32 is null ? "nicht vorhanden" : $"{(w32.Running ? "läuft" : "beendet")}, Start {w32.StartMode}")}",
        };
        var offset = await SntpClient.QueryOffsetAsync(ct).ConfigureAwait(false);
        if (offset is null) return R(d, TestStatus.NotAvailable, "Kein Zeitserver erreichbar (offline oder UDP 123 blockiert).", details);
        var abs = Math.Abs(offset.Value.Offset.TotalSeconds);
        details.Add($"Abweichung zu {offset.Value.Server}: {offset.Value.Offset.TotalSeconds:+0.00;-0.00} s");
        return abs <= 2 ? R(d, TestStatus.Pass, $"Systemzeit korrekt (Abweichung {abs:0.00} s).", details)
            : abs <= 60 ? R(d, TestStatus.Warning, $"Systemzeit weicht um {abs:0} s ab.", details)
            : R(d, TestStatus.Fail, $"Systemzeit weicht um {abs:0} s ab – Anmeldungen und Zertifikate können fehlschlagen.", details);
    }

    /// <summary>WinRE-Status aus ReAgent.xml (nur lesend). Ohne Leserecht: NOT AVAILABLE statt einer Schätzung.</summary>
    private static ComponentTestResult TestWinRe(ComponentTestDefinition d)
    {
        var path = Path.Combine(Environment.SystemDirectory, "Recovery", "ReAgent.xml");
        try
        {
            if (!File.Exists(path)) return R(d, TestStatus.Warning, "Keine WinRE-Konfiguration gefunden (ReAgent.xml fehlt).");
            var info = new FileInfo(path);
            if (info.Length > 1024 * 1024) return R(d, TestStatus.NotAvailable, "WinRE-Konfiguration hat eine unerwartete Größe.");
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);
            var bcd = doc.SelectSingleNode("//WinreBCD")?.Attributes?["id"]?.Value;
            var location = doc.SelectSingleNode("//WinreLocation")?.Attributes?["path"]?.Value;
            var details = new List<string> { $"WinRE-BCD-Eintrag: {bcd ?? "–"}", $"Speicherort: {(string.IsNullOrEmpty(location) ? "–" : location)}" };
            var enabled = bcd is not null && !bcd.Trim('{', '}').All(c => c is '0' or '-');
            return enabled
                ? R(d, TestStatus.Pass, "Windows-Wiederherstellungsumgebung ist eingerichtet.", details)
                : R(d, TestStatus.Warning, "WinRE ist deaktiviert – Reparaturoptionen beim Start nicht verfügbar.", details);
        }
        catch (UnauthorizedAccessException)
        {
            return R(d, TestStatus.NotAvailable, "WinRE-Status nur mit Adminrechten lesbar.");
        }
        catch (Exception ex) when (ex is IOException or XmlException)
        {
            return R(d, TestStatus.NotAvailable, "WinRE-Konfiguration nicht lesbar: " + ex.Message);
        }
    }
}
