using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateX.App.Infrastructure;
using UltimateX.Core.Common;
using UltimateX.Core.Lifetime;
using UltimateX.Core.Localization;
using UltimateX.Services.Common;
using UltimateX.Services.Hardware;
using UltimateX.Services.Insights;

namespace UltimateX.App.ViewModels;

/// <summary>
/// PC LIFETIME (v1.3): Betriebsstunden/Einschaltvorgänge/Verschleiß je Laufwerk (SMART), Windows-Installationsdatum,
/// Laufzeit seit Start, Starts und Gesamtlaufzeit laut Ereignisprotokoll sowie die eigene Laufzeit-Mitschrift von ULTIMATE X.
/// Nicht messbare Werte (Stundenzähler von CPU/GPU/RAM, geschriebene Datenmenge) werden als N/A geführt.
/// </summary>
public sealed partial class LifetimeViewModel : PageViewModel
{
    public override string Title => "PC Lifetime";
    public override string Glyph => "\uE823";
    public override string Keywords => "lifetime lebensdauer betriebsstunden laufzeit uptime einschaltvorgänge power on hours verschleiß wear installationsdatum windows installiert starts gesamtlaufzeit alter";
    public override string? Subtitle => "Betriebsstunden, Starts und Laufzeit – nur gemessene Werte";

    public ObservableCollection<InfoSection> Drives { get; } = [];
    public ObservableCollection<InfoSection> Sections { get; } = [];

    [ObservableProperty] private string? _smartInfo;
    [ObservableProperty] private bool _hasSmart;

    protected override async Task LoadAsync()
    {
        var smart = SmartService.LoadCached();
        var (drives, system, uptime) = await Task.Run(() =>
            (AppServices.DriveHealth.GetDrives(smart), AppServices.Lifetime.Load(), AppServices.Uptime.Summarize()));

        HasSmart = smart is not null;
        SmartInfo = smart is null
            ? "SMART-Werte wurden noch nicht gelesen – „SMART lesen (UAC)“ benötigt einmalig Administratorrechte."
            : "SMART-Werte vom " + Format.DateTimeText(smart.Time) + " (Zwischenspeicher, wie auf der Seite Storage).";

        Drives.Clear();
        foreach (var d in drives) Drives.Add(ToSection(d));
        if (drives.Count == 0)
            Drives.Add(new InfoSection("Laufwerke", "\uEDA2", [InfoItem.Unavailable("Laufwerke", "N/A – keine Laufwerksdaten von Windows erhalten")]));

        Sections.Clear();
        Sections.Add(SystemSection(system));
        Sections.Add(OwnLogSection(uptime));
        Sections.Add(new InfoSection("CPU · GPU · RAM", "\uE950",
        [
            new InfoItem("Prozessor (CPU)", "N/A – kein Stundenzähler", DataState.NotAvailable),
            new InfoItem("Grafikkarte (GPU)", "N/A – kein Stundenzähler", DataState.NotAvailable),
            new InfoItem("Arbeitsspeicher (RAM)", "N/A – kein Stundenzähler", DataState.NotAvailable),
        ], "Diese Bauteile führen keinen auslesbaren Betriebsstundenzähler. Näherungsweise entspricht ihre Laufzeit der PC-Laufzeit oben – sofern sie seit der Windows-Installation verbaut sind."));
    }

    private static InfoSection ToSection(DriveHealthInfo d)
    {
        static DataState S(object? v) => v is null ? DataState.NotAvailable : DataState.Ok;
        var c = Loc.Culture;
        var items = new List<InfoItem>
        {
            new("Betriebsstunden", d.PowerOnHours is { } h ? string.Format(c, "{0:N0} h (≈ {1:N1} Jahre Dauerbetrieb)", h, h / 8766.0) : "N/A", S(d.PowerOnHours)),
            new("Einschaltvorgänge", d.PowerCycles is { } p ? p.ToString("N0", c) : "N/A", S(d.PowerCycles)),
            new("Geschrieben", "N/A – von Windows nicht herstellerübergreifend bereitgestellt", DataState.NotAvailable),
            new("Gelesen", "N/A – von Windows nicht herstellerübergreifend bereitgestellt", DataState.NotAvailable),
            new("Verschleiß", d.WearPercent is { } w ? $"{w} % (Restlebensdauer {Math.Clamp(100 - w, 0, 100)} %)" : "N/A",
                d.WearPercent is null ? DataState.NotAvailable : d.WearPercent >= 90 ? DataState.Problem : d.WearPercent >= 70 ? DataState.Warning : DataState.Ok),
            new("Zustand", d.Verdict.Label, d.Verdict.State),
            new("Typ", $"{d.MediaType} · {d.BusType} · {Format.Bytes(d.SizeBytes)}"),
        };
        var note = d.SmartTime is null
            ? "Ohne SMART-Daten sind Betriebsstunden, Einschaltvorgänge und Verschleiß N/A."
            : "Datenmengen (TBW) zeigt das Herstellerwerkzeug des Laufwerks.";
        return new InfoSection(d.Name, "\uEDA2", items, note);
    }

    private static InfoSection SystemSection(LifetimeSystemInfo s)
    {
        var c = Loc.Culture;
        var items = new List<InfoItem>
        {
            new("Windows installiert", s.WindowsInstallDate is { } i ? Format.DateTimeText(i) + $" (vor {(DateTime.Now - i).TotalDays.ToString("N0", c)} Tagen)" : "N/A",
                s.WindowsInstallDate is null ? DataState.NotAvailable : DataState.Ok),
            new("Laufzeit seit Start", RuntimeCalculator.HoursText(s.UptimeSinceBoot, c) + " · " + Format.Duration(s.UptimeSinceBoot)),
        };
        if (s.BootHistory is { } b)
        {
            var since = b.EarliestUtc is { } e ? Format.DateText(e.ToLocalTime()) : "N/A";
            items.Add(new("Starts (Protokoll)", $"mindestens {b.Starts.ToString("N0", c)}, seit {since}"));
            items.Add(new("Gesamtlaufzeit (Protokoll)", $"mindestens {RuntimeCalculator.HoursText(b.Runtime, c)}, seit {since}"));
            items.Add(new("Unerwartete Abschaltungen", b.UnexpectedShutdowns.ToString("N0", c), b.UnexpectedShutdowns > 0 ? DataState.Warning : DataState.Ok));
            items.Add(new("Quelle", s.BootSource + " · Kernel-Power 41/42/107 · EventLog 6008/6013"));
        }
        else
        {
            items.Add(InfoItem.Unavailable("Starts (Protokoll)", "N/A – System-Ereignisprotokoll nicht lesbar"));
            items.Add(InfoItem.Unavailable("Gesamtlaufzeit (Protokoll)", "N/A"));
        }
        return new InfoSection("PC / WINDOWS", "\uE7F8", items,
            "Das Installationsdatum setzt Windows bei Funktionsupdates (z. B. 23H2 → 24H2) neu. Das Ereignisprotokoll ist größenbegrenzt; " +
            "Starts aus dem Schnellstart/Ruhezustand zählen nicht als Start, Energiesparphasen nicht als Laufzeit. Abschnitte ohne sauberes Ende zählen nur bis zum letzten Lebenszeichen.");
    }

    private static InfoSection OwnLogSection(UptimeSummary u)
    {
        var c = Loc.Culture;
        var items = new List<InfoItem>
        {
            new("Erfasst seit", u.FirstStartUtc is { } f ? Format.DateTimeText(f.ToLocalTime()) : "N/A – Mitschrift beginnt mit diesem Start",
                u.FirstStartUtc is null ? DataState.NotAvailable : DataState.Ok),
            new("Laufzeit mit ULTIMATE X", RuntimeCalculator.HoursText(u.Total, c)),
            new("Abschnitte", u.Sessions.ToString("N0", c)),
            new("davon ohne sauberes Ende", u.SessionsWithoutStop.ToString("N0", c), u.SessionsWithoutStop > 0 ? DataState.Warning : DataState.Ok),
        };
        return new InfoSection("EIGENE LAUFZEIT-MITSCHRIFT", "\uE916", items,
            "ULTIMATE X protokolliert Start, Ende und alle 5 Minuten ein Lebenszeichen (lokal, uptime-log.json). Gezählt wird nur die Zeit, in der ULTIMATE X lief; " +
            "nach Absturz oder Stromausfall endet ein Abschnitt beim letzten Lebenszeichen.");
    }

    [RelayCommand]
    private async Task ReadSmartAsync()
    {
        string message;
        IsBusy = true;
        try
        {
            var (snap, error) = await AppServices.Smart.ReadAsync();
            message = error ?? $"SMART-Werte von {snap?.Disks.Count ?? 0} Laufwerk(en) gelesen.";
        }
        finally { IsBusy = false; }
        await RefreshAsync(); // setzt StatusMessage zurück – Meldung danach anzeigen
        StatusMessage ??= message;
    }
}
