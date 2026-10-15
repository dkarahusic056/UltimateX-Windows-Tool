using System.Globalization;
using UltimateX.Core.Analysis;

namespace UltimateX.Core.NetSec;

// =====================================================================================================
// v1.3 Sicherheits-Check (IT-Audit): Bewertung der gesammelten Fakten. Nur Lesen – die Erhebung liegt in
// UltimateX.Services.NetSec.SecurityAuditService. Behebungen werden nur angeboten, nie automatisch ausgeführt.
// =====================================================================================================

public sealed record FirewallProfileState(string Profile, bool? Enabled);

public sealed record LocalAccountInfo(string Name, bool Disabled, bool PasswordRequired, string? Sid)
{
    public bool IsGuest => Sid?.EndsWith("-501", StringComparison.Ordinal) == true;

    public bool IsBuiltInAdministrator => Sid?.EndsWith("-500", StringComparison.Ordinal) == true;
}

/// <summary>Gesammelte Fakten. null = nicht ermittelbar (→ N/A, nie geraten).</summary>
public sealed record AuditFacts
{
    /// <summary>Windows-Feature „SMB1Protocol“ aktiviert?</summary>
    public bool? Smb1FeatureEnabled { get; init; }

    /// <summary>Feature-Status war nur mit Adminrechten lesbar.</summary>
    public bool Smb1NeedsAdmin { get; init; }

    /// <summary>Registry LanmanServer\Parameters\SMB1 (null = nicht gesetzt).</summary>
    public int? Smb1ServerRegistry { get; init; }

    public bool? RdpEnabled { get; init; }

    /// <summary>Netzwerkebenen-Authentifizierung (NLA) für RDP.</summary>
    public bool? RdpNla { get; init; }

    public IReadOnlyList<FirewallProfileState> Firewall { get; init; } = [];

    /// <summary>Mitglieder der lokalen Administratorengruppe (null = nicht ermittelbar).</summary>
    public IReadOnlyList<string>? Administrators { get; init; }

    public IReadOnlyList<LocalAccountInfo>? LocalAccounts { get; init; }

    /// <summary>true = ein Router (InternetGatewayDevice) hat auf SSDP geantwortet; null = nicht geprüft.</summary>
    public bool? UpnpGatewayFound { get; init; }

    public string? UpnpDevice { get; init; }

    public int? NoDriveTypeAutoRun { get; init; }

    public bool? AutoplayDisabled { get; init; }

    public bool? PowerShell2Enabled { get; init; }

    public bool PowerShell2NeedsAdmin { get; init; }

    public int? WindowsBuild { get; init; }

    public string? WindowsEdition { get; init; }

    public string? WindowsDisplayVersion { get; init; }

    public DateOnly Today { get; init; } = DateOnly.FromDateTime(DateTime.Today);
}

/// <summary>Behebungen, die die App (nach Bestätigung, mit UAC) anbieten darf. Defender/UAC/Windows Update werden nie verändert.</summary>
public enum AuditFix
{
    None,
    Smb1Disable,
    RdpDisable,
    FirewallEnableAll,
}

public sealed record AuditItem(string Id, Finding Finding, double Weight, AuditFix Fix = AuditFix.None);

public sealed record AuditResult(IReadOnlyList<AuditItem> Items, int? Score)
{
    public IEnumerable<Finding> Findings => Items.Select(i => i.Finding);

    public bool Offers(AuditFix fix) => Items.Any(i => i.Fix == fix);

    public int OpenIssues => Items.Count(i => i.Finding.Status is CheckStatus.Warning or CheckStatus.Fail);
}

public static class SecurityAudit
{
    public static AuditResult Evaluate(AuditFacts f)
    {
        var items = new List<AuditItem>
        {
            Smb1(f),
            Rdp(f),
            Firewall(f),
            Administrators(f),
            NoPassword(f),
            Guest(f),
            Upnp(f),
            Autorun(f),
            PowerShell2(f),
            WindowsSupport.Evaluate(f.WindowsBuild, f.WindowsEdition, f.WindowsDisplayVersion, f.Today),
        };
        return new AuditResult(items, Score(items));
    }

    /// <summary>PASS = 100, WARNING = 50, FAIL = 0, gewichtet; N/A zählt nicht. null, wenn alles N/A ist.</summary>
    public static int? Score(IEnumerable<AuditItem> items)
        => ScoreMath.WeightedAverage(items.Select(i => (i.Finding.Status switch
        {
            CheckStatus.Pass => (int?)100,
            CheckStatus.Warning => 50,
            CheckStatus.Fail => 0,
            _ => null,
        }, i.Weight)));

    private static AuditItem Smb1(AuditFacts f)
    {
        const string area = "SMB1-Protokoll";
        if (f.Smb1FeatureEnabled == true)
            return new("smb1", new Finding(area, CheckStatus.Fail, "SMB1 ist installiert",
                "SMB1 ist veraltet und unsicher (u. a. WannaCry). Deaktivieren, sofern kein sehr altes NAS/Gerät es benötigt."), 3, AuditFix.Smb1Disable);
        if (f.Smb1FeatureEnabled == false)
            return new("smb1", new Finding(area, CheckStatus.Pass, "SMB1 ist nicht installiert"), 3);
        if (f.Smb1ServerRegistry == 0)
            return new("smb1", new Finding(area, CheckStatus.Pass, "SMB1-Server per Registry deaktiviert",
                "Feature-Status nicht lesbar – der SMB1-Server ist aber abgeschaltet."), 3);
        return new("smb1", new Finding(area, CheckStatus.NotAvailable, f.Smb1NeedsAdmin ? "N/A ohne Adminrechte" : "N/A",
            f.Smb1NeedsAdmin ? "Der Feature-Status ist nur mit Adminrechten lesbar." : null), 3);
    }

    private static AuditItem Rdp(AuditFacts f)
    {
        const string area = "Remotedesktop";
        return f.RdpEnabled switch
        {
            true when f.RdpNla == false => new("rdp", new Finding(area, CheckStatus.Fail, "Eingeschaltet, ohne Netzwerkebenen-Authentifizierung (NLA)",
                "Remotedesktop ausschalten, wenn er nicht benötigt wird – sonst unbedingt NLA aktivieren."), 2, AuditFix.RdpDisable),
            true => new("rdp", new Finding(area, CheckStatus.Warning, "Eingeschaltet",
                "Nur aktiv lassen, wenn Sie den PC wirklich aus der Ferne bedienen. Nie direkt ins Internet freigeben."), 2, AuditFix.RdpDisable),
            false => new("rdp", new Finding(area, CheckStatus.Pass, "Ausgeschaltet"), 2),
            _ => new("rdp", new Finding(area, CheckStatus.NotAvailable, "N/A"), 2),
        };
    }

    private static AuditItem Firewall(AuditFacts f)
    {
        const string area = "Firewall-Profile";
        if (f.Firewall.Count == 0 || f.Firewall.All(p => p.Enabled is null))
            return new("firewall", new Finding(area, CheckStatus.NotAvailable, "N/A"), 3);
        var text = string.Join(" · ", f.Firewall.Select(p => $"{p.Profile}: {(p.Enabled switch { true => "an", false => "AUS", _ => "N/A" })}"));
        if (f.Firewall.Any(p => p.Enabled == false))
            return new("firewall", new Finding(area, CheckStatus.Fail, text, "Die Windows-Firewall sollte in allen Profilen eingeschaltet sein."), 3, AuditFix.FirewallEnableAll);
        if (f.Firewall.Any(p => p.Enabled is null))
            return new("firewall", new Finding(area, CheckStatus.Warning, text, "Nicht alle Profile konnten gelesen werden."), 3);
        return new("firewall", new Finding(area, CheckStatus.Pass, text), 3);
    }

    private static AuditItem Administrators(AuditFacts f)
    {
        const string area = "Lokale Administratoren";
        if (f.Administrators is null) return new("admins", new Finding(area, CheckStatus.NotAvailable, "N/A"), 1);
        var list = f.Administrators.Count == 0 ? "keine Mitglieder gelesen" : string.Join(", ", f.Administrators);
        var builtIn = f.LocalAccounts?.FirstOrDefault(a => a.IsBuiltInAdministrator);
        if (builtIn is { Disabled: false })
            return new("admins", new Finding(area, CheckStatus.Warning, $"{f.Administrators.Count}: {list}",
                $"Das integrierte Konto „{builtIn.Name}“ ist aktiv. Es ist ein bevorzugtes Angriffsziel – für den Alltag ein Standardkonto verwenden."), 1);
        if (f.Administrators.Count > 2)
            return new("admins", new Finding(area, CheckStatus.Warning, $"{f.Administrators.Count}: {list}",
                "Mehr als zwei Administratoren. Nur Konten, die es wirklich brauchen, sollten Adminrechte haben."), 1);
        return new("admins", new Finding(area, CheckStatus.Pass, $"{f.Administrators.Count}: {list}"), 1);
    }

    private static AuditItem NoPassword(AuditFacts f)
    {
        const string area = "Konten ohne Passwortpflicht";
        if (f.LocalAccounts is null) return new("nopassword", new Finding(area, CheckStatus.NotAvailable, "N/A"), 2);
        var open = f.LocalAccounts.Where(a => !a.Disabled && !a.PasswordRequired).Select(a => a.Name).ToList();
        return open.Count == 0
            ? new("nopassword", new Finding(area, CheckStatus.Pass, "Alle aktiven lokalen Konten erfordern ein Passwort"), 2)
            : new("nopassword", new Finding(area, CheckStatus.Warning, string.Join(", ", open),
                "Diese aktiven Konten dürfen ohne Passwort sein. Prüfen, ob ein Passwort gesetzt ist (Einstellungen → Konten → Anmeldeoptionen)."), 2);
    }

    private static AuditItem Guest(AuditFacts f)
    {
        const string area = "Gastkonto";
        var guest = f.LocalAccounts?.FirstOrDefault(a => a.IsGuest);
        if (guest is null) return new("guest", new Finding(area, CheckStatus.NotAvailable, "N/A"), 2);
        return guest.Disabled
            ? new("guest", new Finding(area, CheckStatus.Pass, "Deaktiviert"), 2)
            : new("guest", new Finding(area, CheckStatus.Fail, $"„{guest.Name}“ ist AKTIV",
                "Das Gastkonto erlaubt Anmeldungen ohne eigenes Konto. In der Computerverwaltung deaktivieren."), 2);
    }

    private static AuditItem Upnp(AuditFacts f)
    {
        const string area = "UPnP am Router";
        return f.UpnpGatewayFound switch
        {
            true => new("upnp", new Finding(area, CheckStatus.Warning, "Router meldet UPnP aktiv" + (f.UpnpDevice is { } d ? $" ({d})" : ""),
                "Über UPnP können Programme im Heimnetz selbst Ports im Router öffnen. Im Router abschalten, wenn nicht benötigt (z. B. für Konsolen)."), 1),
            false => new("upnp", new Finding(area, CheckStatus.Pass, "Kein Router hat auf die UPnP-Suche geantwortet",
                "Hinweis: Das ist kein Beweis, dass UPnP aus ist (Antworten können gefiltert sein)."), 1),
            _ => new("upnp", new Finding(area, CheckStatus.NotAvailable, "N/A"), 1),
        };
    }

    private static AuditItem Autorun(AuditFacts f)
    {
        const string area = "Autorun / AutoPlay";
        if (f.AutoplayDisabled == true || f.NoDriveTypeAutoRun == 0xFF)
            return new("autorun", new Finding(area, CheckStatus.Pass, "Für alle Laufwerkstypen deaktiviert"), 1);
        var detail = f.NoDriveTypeAutoRun is { } v ? $"NoDriveTypeAutoRun = 0x{v.ToString("X2", CultureInfo.InvariantCulture)}" : "Windows-Standard";
        return new("autorun", new Finding(area, CheckStatus.Warning, $"AutoPlay aktiv ({detail})",
            "Windows führt autorun.inf von USB-Sticks nicht mehr aus, fragt aber beim Einstecken nach. Abschalten: Einstellungen → Bluetooth & Geräte → AutoPlay."), 1);
    }

    private static AuditItem PowerShell2(AuditFacts f)
    {
        const string area = "PowerShell 2.0";
        return f.PowerShell2Enabled switch
        {
            true => new("ps2", new Finding(area, CheckStatus.Warning, "Installiert",
                "PowerShell 2.0 umgeht moderne Schutz- und Protokollfunktionen. Unter „Windows-Features aktivieren oder deaktivieren“ entfernen."), 1),
            false => new("ps2", new Finding(area, CheckStatus.Pass, "Nicht installiert"), 1),
            _ => new("ps2", new Finding(area, CheckStatus.NotAvailable, f.PowerShell2NeedsAdmin ? "N/A ohne Adminrechte" : "N/A"), 1),
        };
    }
}

/// <summary>
/// Supportzeitraum der Windows-Versionen (Stand der Tabelle: Oktober 2026, Quelle: Microsoft Lifecycle).
/// Neuere, unbekannte Builds werden als N/A gemeldet – nicht geraten.
/// </summary>
public static class WindowsSupport
{
    public sealed record Release(int Build, string Name, DateOnly HomePro, DateOnly EnterpriseEducation, DateOnly? Ltsc = null);

    public const string TableAsOf = "Oktober 2026";

    public static IReadOnlyList<Release> Releases { get; } =
    [
        new(19041, "Windows 10 2004", new(2021, 12, 14), new(2021, 12, 14)),
        new(19042, "Windows 10 20H2", new(2022, 5, 10), new(2023, 5, 9)),
        new(19043, "Windows 10 21H1", new(2022, 12, 13), new(2022, 12, 13)),
        new(19044, "Windows 10 21H2", new(2023, 6, 13), new(2024, 6, 11), new(2027, 1, 12)),
        new(19045, "Windows 10 22H2", new(2025, 10, 14), new(2025, 10, 14)),
        new(22000, "Windows 11 21H2", new(2023, 10, 10), new(2024, 10, 8)),
        new(22621, "Windows 11 22H2", new(2024, 10, 8), new(2025, 10, 14)),
        new(22631, "Windows 11 23H2", new(2025, 11, 11), new(2026, 11, 10)),
        new(26100, "Windows 11 24H2", new(2026, 10, 13), new(2027, 10, 12), new(2029, 10, 9)),
        new(26200, "Windows 11 25H2", new(2027, 10, 12), new(2028, 10, 10)),
    ];

    /// <summary>Warnung, wenn das Supportende in weniger als so vielen Tagen erreicht ist.</summary>
    public const int WarnDays = 90;

    public static bool IsLtsc(string? edition) => edition is not null && edition.Contains("EnterpriseS", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnterpriseOrEducation(string? edition)
        => edition is not null && (edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase) || edition.Contains("Education", StringComparison.OrdinalIgnoreCase));

    /// <summary>Supportende für Build und Edition; null = nicht in der Tabelle.</summary>
    public static (Release Release, DateOnly End)? Lookup(int build, string? edition)
    {
        var r = Releases.FirstOrDefault(x => x.Build == build);
        if (r is null) return null;
        var end = IsLtsc(edition) && r.Ltsc is { } l ? l : IsEnterpriseOrEducation(edition) ? r.EnterpriseEducation : r.HomePro;
        return (r, end);
    }

    public static AuditItem Evaluate(int? build, string? edition, string? displayVersion, DateOnly today)
    {
        const string area = "Windows-Version";
        if (build is null) return new("winsupport", new Finding(area, CheckStatus.NotAvailable, "N/A"), 3);
        var b = build.Value;
        var label = $"Build {b.ToString(CultureInfo.InvariantCulture)}" + (string.IsNullOrWhiteSpace(displayVersion) ? "" : $" ({displayVersion})");
        if (edition?.StartsWith("Server", StringComparison.OrdinalIgnoreCase) == true)
            return new("winsupport", new Finding(area, CheckStatus.NotAvailable, $"Windows Server, {label}", "Supportzeiträume von Windows Server sind nicht in der Tabelle."), 3);
        if (b < 19041)
            return new("winsupport", new Finding(area, CheckStatus.Fail, $"{label} – nicht mehr unterstützt",
                "Keine Sicherheitsupdates mehr. Auf eine aktuelle Windows-Version aktualisieren."), 3);
        var hit = Lookup(b, edition);
        if (hit is null)
        {
            return b > Releases[^1].Build
                ? new("winsupport", new Finding(area, CheckStatus.NotAvailable, $"{label} – neuer als die Support-Tabelle (Stand {TableAsOf})"), 3)
                : new("winsupport", new Finding(area, CheckStatus.NotAvailable, $"{label} – unbekannter Build (Insider/Vorabversion?)"), 3);
        }
        var (release, end) = hit.Value;
        var endText = end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        if (today > end)
        {
            var advice = release.Build == 19045
                ? "Windows 10 erhält ohne kostenpflichtige erweiterte Sicherheitsupdates (ESU) keine Updates mehr. Auf Windows 11 wechseln."
                : "Über Windows Update auf die aktuelle Version aktualisieren.";
            return new("winsupport", new Finding(area, CheckStatus.Fail, $"{release.Name} – Supportende am {endText}", advice), 3);
        }
        var days = end.DayNumber - today.DayNumber;
        if (days <= WarnDays)
            return new("winsupport", new Finding(area, CheckStatus.Warning, $"{release.Name} – Supportende am {endText} (in {days} Tagen)",
                "Bald auf die aktuelle Version aktualisieren (Windows Update → Funktionsupdate)."), 3);
        return new("winsupport", new Finding(area, CheckStatus.Pass, $"{release.Name} – unterstützt bis {endText}"), 3);
    }
}
