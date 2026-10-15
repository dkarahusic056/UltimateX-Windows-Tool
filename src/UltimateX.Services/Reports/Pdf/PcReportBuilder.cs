using System.Globalization;
using UltimateX.Core.Common;
using UltimateX.Core.Localization;
using UltimateX.Core.Security;
using UltimateX.Services.Common;
using UltimateX.Services.Diagnostics;
using UltimateX.Services.Sensors;

namespace UltimateX.Services.Reports.Pdf;

/// <summary>
/// Layout des PC-Berichts (v1.1 Phase 5): ULTIMATE-X-Branding mit schwarz/rotem Kopf, Übersicht mit Health Score,
/// Hardware-Karten, Tabellen, Statusanzeigen, Warnungen und Diagrammen. Erzeugt eine geräteunabhängige
/// Zeichenliste, die als PDF geschrieben (PdfWriter) oder in WPF angezeigt/gedruckt wird – identisches Ergebnis.
/// </summary>
public sealed class PcReportBuilder
{
    // Druckfreundliche Palette: Weißes Papier, schwarzer Kopf, Rot als Akzent.
    private static readonly RColor Black = RColor.Hex("0B0B0B");
    private static readonly RColor Graphite = RColor.Hex("1D1D1D");
    private static readonly RColor Red = RColor.Hex("D40000");
    private static readonly RColor RedHot = RColor.Hex("FF2525");
    private static readonly RColor RedDeep = RColor.Hex("8B0000");
    private static readonly RColor White = RColor.Hex("FFFFFF");
    private static readonly RColor Text = RColor.Hex("1A1A1A");
    private static readonly RColor Muted = RColor.Hex("6B6B6B");
    private static readonly RColor Light = RColor.Hex("F4F4F4");
    private static readonly RColor Line = RColor.Hex("DADADA");
    private static readonly RColor Ok = RColor.Hex("1E9E5A");
    private static readonly RColor Warn = RColor.Hex("D98A00");
    private static readonly RColor Na = RColor.Hex("9A9A9A");

    private const double Margin = 40;
    private const double ContentW = ReportPage.Width - 2 * Margin;
    private const double Bottom = ReportPage.Height - 56;

    private readonly PcReportData _d;
    private readonly PcReportOptions _o;
    private readonly ReportDocument _doc;
    private ReportPage _page = null!;
    private double _y;
    private string _section = "";

    private PcReportBuilder(PcReportData data, PcReportOptions options)
    {
        _d = data;
        _o = options;
        _doc = new ReportDocument { Title = T("ULTIMATE X PC-Bericht") + " " + data.ReportId, Created = data.Created };
    }

    public static ReportDocument Build(PcReportData data, PcReportOptions options)
    {
        var b = new PcReportBuilder(data, options);
        b.Cover();
        if (options.Hardware) b.HardwareSection();
        if (options.Sensors) b.SensorSection();
        if (options.Network) b.NetworkSection();
        if (options.Security) b.SecuritySection();
        if (options.Drivers) b.DriverSection();
        if (options.WindowsHealth) b.WindowsHealthSection();
        if (options.ComponentTests) b.ComponentTestSection();
        if (options.Benchmarks) b.BenchmarkSection();
        if (options.CrashAnalysis) b.CrashSection();
        if (options.Analysis) b.AnalysisSection();
        b.Footers();
        return b._doc;
    }

    private static string T(string s) => Loc.T(s);

    private static IReadOnlyList<string> Row(params string[] cells) => cells;

    private static RColor StateColor(DataState s) => s switch
    {
        DataState.Ok => Ok,
        DataState.Warning => Warn,
        DataState.Problem => Red,
        _ => Na,
    };

    private string Mask(string? value, Func<string, string> mask) =>
        value is null ? "–" : _o.PrivacyMode ? mask(value) : value;

    private string MaskIps(IEnumerable<string> ips) =>
        string.Join(", ", ips.Select(ip => _o.PrivacyMode ? SensitiveMasker.MaskIp(ip) : ip).DefaultIfEmpty("–"));

    // ================================================================== Seitensteuerung

    private void NewPage()
    {
        _page = new ReportPage();
        _doc.Pages.Add(_page);
        // Kompakter Kopf für Folgeseiten
        Add(new RectOp(0, 0, ReportPage.Width, 38, Black, null));
        Add(new PathOp([(0, 38), (ReportPage.Width - 120, 38), (ReportPage.Width - 112, 42), (0, 42)], true, Red, null));
        Logo(Margin, 25, 13);
        var title = _section.ToUpper(Loc.Culture);
        Add(new TextOp(ReportPage.Width - Margin - FontMetrics.Width(title, 9, true), 24, title, 9, true, RColor.Hex("BBBBBB")));
        _y = 62;
    }

    private void Add(DrawOp op) => _page.Ops.Add(op);

    private void Ensure(double height)
    {
        if (_y + height > Bottom) NewPage();
    }

    private void Logo(double x, double baseline, double size)
    {
        Add(new TextOp(x, baseline, "ULTIMATE", size, true, White));
        var w = FontMetrics.Width("ULTIMATE ", size, true);
        Add(new TextOp(x + w, baseline, "X", size * 1.15, true, RedHot));
    }

    private void Section(string title)
    {
        // Jeder Hauptabschnitt beginnt auf einer neuen Seite (übersichtlicher Druck).
        _section = T(title);
        NewPage();
        _y += 6;
        Add(new PathOp([(Margin, _y), (Margin + 6, _y), (Margin + 6, _y + 16), (Margin, _y + 22)], true, Red, null));
        Add(new TextOp(Margin + 14, _y + 15, _section.ToUpper(Loc.Culture), 14, true, Text));
        _y += 26;
        Add(new LineOp(Margin, _y, Margin + ContentW, _y, Line, 0.6));
        _y += 12;
    }

    private void SubTitle(string title)
    {
        Ensure(40);
        Add(new TextOp(Margin, _y + 10, T(title), 10.5, true, RedDeep));
        _y += 18;
    }

    private void Paragraph(string text, double size = 9, RColor? color = null)
    {
        foreach (var line in FontMetrics.Wrap(T(text), size, false, ContentW))
        {
            Ensure(size + 4);
            Add(new TextOp(Margin, _y + size, line, size, false, color ?? Text));
            _y += size + 3.5;
        }
        _y += 4;
    }

    private void Dot(double x, double cy, DataState s, double r = 3.2)
    {
        var pts = Enumerable.Range(0, 16).Select(i => (x + r + r * Math.Cos(i * Math.PI / 8), cy + r * Math.Sin(i * Math.PI / 8))).ToList();
        Add(new PathOp(pts, true, StateColor(s), null));
    }

    /// <summary>Abgeschrägte Karte (oben rechts/unten links) wie in der App.</summary>
    private void Chamfer(double x, double y, double w, double h, RColor fill, RColor? stroke, double c = 8, RColor? accent = null)
    {
        Add(new PathOp([(x, y), (x + w - c, y), (x + w, y + c), (x + w, y + h), (x + c, y + h), (x, y + h - c)], true, fill, stroke, 0.6));
        if (accent is { } a) Add(new RectOp(x, y, Math.Min(36, w / 3), 2, a, null));
    }

    // ================================================================== Bausteine

    private void KeyValues(IEnumerable<(string Label, string Value, DataState? State)> items, int columns = 2)
    {
        var list = items.ToList();
        var colW = ContentW / columns;
        for (var i = 0; i < list.Count; i += columns)
        {
            var rowH = 0.0;
            var cells = list.Skip(i).Take(columns).ToList();
            foreach (var c in cells)
                rowH = Math.Max(rowH, 12 + 11 * Math.Min(3, FontMetrics.Wrap(T(c.Value), 9, false, colW - 14).Count));
            Ensure(rowH + 4);
            for (var j = 0; j < cells.Count; j++)
            {
                var (label, value, state) = cells[j];
                var x = Margin + j * colW;
                Add(new TextOp(x, _y + 8, T(label).ToUpper(Loc.Culture), 6.8, true, Muted));
                var vx = x;
                if (state is { } s) { Dot(x, _y + 16.5, s); vx += 10; }
                var lines = FontMetrics.Wrap(T(value), 9, false, colW - 14 - (vx - x)).Take(3).ToList();
                for (var k = 0; k < lines.Count; k++) Add(new TextOp(vx, _y + 19 + k * 11, lines[k], 9, false, Text));
            }
            _y += rowH + 4;
        }
        _y += 4;
    }

    private sealed record Col(string Header, double Weight, bool Wrap = false);

    /// <summary>Tabelle mit dunkler Kopfzeile, Zebrastreifen, optionalem Statuspunkt je Zeile und automatischem Seitenumbruch.</summary>
    private void Table(IReadOnlyList<Col> cols, IEnumerable<(IReadOnlyList<string> Cells, DataState? State)> rows, string? empty = null)
    {
        var data = rows.ToList();
        if (data.Count == 0)
        {
            if (empty is not null) Paragraph(empty, 9, Muted);
            return;
        }
        var hasDot = data.Any(r => r.State is not null);
        var dotW = hasDot ? 14 : 0;
        var total = cols.Sum(c => c.Weight);
        var widths = cols.Select(c => (ContentW - dotW) * c.Weight / total).ToArray();

        void Header()
        {
            Ensure(20);
            Chamfer(Margin, _y, ContentW, 17, Graphite, null, 5);
            var x = Margin + 6 + dotW;
            for (var i = 0; i < cols.Count; i++)
            {
                Add(new TextOp(x, _y + 11.5, FontMetrics.Ellipsis(T(cols[i].Header).ToUpper(Loc.Culture), 7, true, widths[i] - 8), 7, true, White));
                x += widths[i];
            }
            _y += 19;
        }

        Header();
        var zebra = false;
        foreach (var (cells, state) in data)
        {
            var lines = new List<IReadOnlyList<string>>();
            for (var i = 0; i < cols.Count; i++)
            {
                var text = T(i < cells.Count ? cells[i] : "");
                lines.Add(cols[i].Wrap ? FontMetrics.Wrap(text, 8.5, false, widths[i] - 8).Take(4).ToList()
                                       : [FontMetrics.Ellipsis(text, 8.5, i == 0, widths[i] - 8)]);
            }
            var h = 6 + 10.5 * lines.Max(l => l.Count);
            if (_y + h > Bottom) { NewPage(); Header(); }
            if (zebra) Add(new RectOp(Margin, _y - 1, ContentW, h, Light, null));
            zebra = !zebra;
            if (state is { } s) Dot(Margin + 4, _y + 6.5, s);
            var x = Margin + 6 + dotW;
            for (var i = 0; i < cols.Count; i++)
            {
                for (var k = 0; k < lines[i].Count; k++)
                    Add(new TextOp(x, _y + 9 + k * 10.5, lines[i][k], 8.5, i == 0, i == 0 ? Text : RColor.Hex("2A2A2A")));
                x += widths[i];
            }
            _y += h;
        }
        Add(new LineOp(Margin, _y, Margin + ContentW, _y, Line, 0.5));
        _y += 12;
    }

    private void LineChart(string title, double height, params (string Name, IReadOnlyList<double> Values, RColor Color)[] series)
    {
        var usable = series.Where(s => s.Values.Count >= 2).ToList();
        if (usable.Count == 0) return;
        Ensure(height + 34);
        Add(new TextOp(Margin, _y + 9, T(title), 9, true, Text));
        var lx = Margin + ContentW;
        foreach (var s in usable.AsEnumerable().Reverse())
        {
            var name = T(s.Name);
            lx -= FontMetrics.Width(name, 8, false) + 26;
            Add(new RectOp(lx, _y + 5, 12, 2.5, s.Color, null));
            Add(new TextOp(lx + 16, _y + 9, name, 8, false, Muted));
        }
        _y += 16;
        var x0 = Margin + 26;
        var w = ContentW - 26;
        foreach (var level in new[] { 0, 25, 50, 75, 100 })
        {
            var y = _y + height - level / 100.0 * height;
            Add(new LineOp(x0, y, x0 + w, y, Line, 0.4));
            Add(new TextOp(Margin, y + 3, level.ToString(CultureInfo.InvariantCulture) + "%", 6.5, false, Muted));
        }
        foreach (var s in usable)
        {
            var step = w / (s.Values.Count - 1);
            var pts = s.Values.Select((v, i) => (x0 + i * step, _y + height - Math.Clamp(v, 0, 100) / 100.0 * height)).ToList();
            Add(new PathOp(pts, false, null, s.Color, 1.3));
        }
        _y += height + 14;
    }

    private void Ring(double cx, double cy, double r, double? percent, RColor color, double thickness)
    {
        List<(double, double)> Arc(double from, double to)
        {
            var n = Math.Max(2, (int)((to - from) / 4));
            return Enumerable.Range(0, n + 1).Select(i =>
            {
                var a = (from + (to - from) * i / n - 90) * Math.PI / 180;
                return (cx + r * Math.Cos(a), cy + r * Math.Sin(a));
            }).ToList();
        }
        Add(new PathOp(Arc(0, 359.9), false, null, RColor.Hex("3A3A3A"), thickness));
        if (percent is { } p && p > 0) Add(new PathOp(Arc(0, 360 * Math.Clamp(p, 0, 100) / 100), false, null, color, thickness));
        for (var i = 0; i < 60; i++)
        {
            var a = (i * 6 - 90) * Math.PI / 180;
            var r1 = r + thickness / 2 + 3;
            var r2 = r1 + (i % 5 == 0 ? 5 : 3);
            Add(new LineOp(cx + r1 * Math.Cos(a), cy + r1 * Math.Sin(a), cx + r2 * Math.Cos(a), cy + r2 * Math.Sin(a),
                percent is { } pp && i < pp / 100 * 60 ? color : RColor.Hex("4A4A4A"), 0.7));
        }
    }

    // ================================================================== Deckblatt / Übersicht

    private void Cover()
    {
        _section = T("Übersicht");
        _page = new ReportPage();
        _doc.Pages.Add(_page);
        const double headH = 150;
        Add(new RectOp(0, 0, ReportPage.Width, headH, Black, null));
        Add(new PathOp([(0, headH), (ReportPage.Width - 170, headH), (ReportPage.Width - 158, headH + 8), (0, headH + 8)], true, Red, null));
        Add(new PathOp([(ReportPage.Width - 220, 0), (ReportPage.Width, 0), (ReportPage.Width, 60)], true, RedDeep, null));
        Logo(Margin, 62, 30);
        Add(new TextOp(Margin, 84, "WINDOWS CONTROL", 10, true, RColor.Hex("AFAFAF")));
        Add(new TextOp(Margin, 120, T("PC-BERICHT"), 22, true, White));

        var meta = new[]
        {
            (T("Berichtsdatum"), _d.Created.ToString(Loc.IsEnglish ? "MM/dd/yyyy HH:mm" : "dd.MM.yyyy HH:mm", Loc.Culture)),
            (T("Bericht-ID"), _d.ReportId),
            (T("App-Version"), "v" + _d.AppVersion),
            (T("Computer"), _o.PrivacyMode ? T("(ausgeblendet)") : _d.Hardware?.Os.ComputerName ?? "–"),
        };
        var my = 76.0;
        foreach (var (label, value) in meta)
        {
            Add(new TextOp(ReportPage.Width - Margin - 170, my, label.ToUpper(Loc.Culture), 6.8, true, RColor.Hex("8A8A8A")));
            Add(new TextOp(ReportPage.Width - Margin - 90, my, value, 8.5, false, White));
            my += 14;
        }
        _y = headH + 26;

        // Health Score + Status
        var score = _d.Score;
        var state = score?.State ?? DataState.NotAvailable;
        Chamfer(Margin, _y, ContentW, 130, Light, Line, 12, Red);
        Ring(Margin + 72, _y + 65, 40, score?.Score, StateColor(state), 7);
        var scoreText = score?.Score.ToString(CultureInfo.InvariantCulture) ?? "–";
        Add(new TextOp(Margin + 72 - FontMetrics.Width(scoreText, 24, true) / 2, _y + 72, scoreText, 24, true, StateColor(state)));
        Add(new TextOp(Margin + 72 - FontMetrics.Width("HEALTH SCORE", 6, true) / 2, _y + 84, "HEALTH SCORE", 6, true, Muted));
        var tx = Margin + 150;
        Add(new TextOp(tx, _y + 28, "SYSTEM HEALTH", 8, true, Red));
        var status = state switch { DataState.Ok => "GOOD", DataState.Warning => T("HINWEISE"), DataState.Problem => T("HANDLUNGSBEDARF"), _ => T("NICHT BEWERTET") };
        Add(new TextOp(tx, _y + 50, "PC STATUS: " + status, 18, true, StateColor(state)));
        var rating = score is null ? T("Kein Health Score berechnet.") : $"{T(score.Rating)} · {score.Factors.Count} {T("Abzug/Abzüge")}";
        Add(new TextOp(tx, _y + 66, rating, 9.5, false, Text));
        var os = _d.Hardware?.Os;
        if (os is not null)
        {
            Add(new TextOp(tx, _y + 86, $"{os.Caption.Replace("Microsoft ", "", StringComparison.Ordinal)} {os.DisplayVersion} · Build {os.Build} · {os.Architecture}", 8.5, false, Muted));
            Add(new TextOp(tx, _y + 100, $"{T("Betriebszeit")}: {Format.Duration(os.Uptime)}", 8.5, false, Muted));
        }
        if (_d.ComponentTests is { } run)
            Add(new TextOp(tx, _y + 114, $"{T("Komponententest")}: {run.Overall.Label()} · PASS {run.Count(TestStatus.Pass)} · WARNING {run.Count(TestStatus.Warning)} · FAIL {run.Count(TestStatus.Fail)}", 8.5, false, Muted));
        _y += 146;

        // Hardware-Karten 3 × 2
        var hw = _d.Hardware;
        var gpu = hw?.Gpus.FirstOrDefault(g => !g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase)) ?? hw?.Gpus.FirstOrDefault();
        var sys = hw?.Volumes.FirstOrDefault(v => v.Letter.StartsWith((Path.GetPathRoot(Environment.SystemDirectory) ?? "C")[..1], StringComparison.OrdinalIgnoreCase));
        var secProblems = _d.Security.Count(s => s.State == DataState.Problem && s.Area != "Application Integrity");
        var cards = new (string Title, string Value, string Sub, DataState State)[]
        {
            ("CPU", hw?.Cpu?.Name ?? "–", hw?.Cpu is { } c ? $"{c.Cores} {T("Kerne")} / {c.Threads} Threads" : "", hw?.Cpu is null ? DataState.NotAvailable : DataState.Ok),
            ("GPU", gpu?.Name ?? "–", gpu is null ? "" : $"VRAM {Format.Bytes(gpu.VramBytes)} · {T("Treiber")} {gpu.DriverVersion ?? "–"}", gpu is null ? DataState.NotAvailable : DataState.Ok),
            ("RAM", Format.Bytes(hw?.Memory.TotalBytes), hw is null ? "" : $"{hw.Memory.Modules.Count} {T("Modul(e)")}" + (hw.Memory.Modules.FirstOrDefault()?.SpeedMTs is { } mts ? $" · {mts} MT/s" : ""), hw is null ? DataState.NotAvailable : DataState.Ok),
            (T("SPEICHER"), sys is null ? "–" : $"{sys.Letter} {Format.Bytes(sys.FreeBytes)} {T("frei")}", sys is null ? "" : $"{Format.Bytes(sys.SizeBytes)} · {sys.UsedPercent:0} % {T("belegt")}",
                sys?.UsedPercent is >= 95 ? DataState.Problem : sys?.UsedPercent is >= 85 ? DataState.Warning : sys is null ? DataState.NotAvailable : DataState.Ok),
            ("MAINBOARD", hw is null ? "–" : $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim(), hw is null ? "" : $"{hw.Board.Firmware} · BIOS {hw.Board.BiosVersion ?? "–"}", hw is null ? DataState.NotAvailable : DataState.Ok),
            ("SECURITY", _d.Security.Count == 0 ? "–" : secProblems > 0 ? T("HANDLUNGSBEDARF") : "PROTECTED", $"{_d.Security.Count} {T("Prüfpunkte")}",
                _d.Security.Count == 0 ? DataState.NotAvailable : secProblems > 0 ? DataState.Problem : _d.Security.Any(s => s.State == DataState.Warning) ? DataState.Warning : DataState.Ok),
        };
        var cw = (ContentW - 16) / 3;
        for (var i = 0; i < cards.Length; i++)
        {
            var cx = Margin + i % 3 * (cw + 8);
            var cy = _y + i / 3 * 66;
            var card = cards[i];
            Chamfer(cx, cy, cw, 58, White, Line, 8, StateColor(card.State));
            Add(new TextOp(cx + 10, cy + 16, card.Title, 7.5, true, Red));
            Dot(cx + cw - 16, cy + 13, card.State);
            Add(new TextOp(cx + 10, cy + 32, FontMetrics.Ellipsis(T(card.Value), 9.5, true, cw - 20), 9.5, true, Text));
            Add(new TextOp(cx + 10, cy + 46, FontMetrics.Ellipsis(card.Sub, 7.5, false, cw - 20), 7.5, false, Muted));
        }
        _y += 140;

        // Warnungen
        var warnings = new List<(string Text, DataState State)>();
        if (score is not null) warnings.AddRange(score.Factors.Select(f => ($"{f.Name}: {f.Reason} (−{f.Deduction})", DataState.Warning)));
        if (_d.ComponentTests is { } ct)
            warnings.AddRange(ct.Results.Where(r => r.Status is TestStatus.Fail or TestStatus.Warning)
                .Select(r => ($"{T("Komponententest")} {r.Name}: {r.Summary}", r.Status == TestStatus.Fail ? DataState.Problem : DataState.Warning)));
        warnings.AddRange(_d.Security.Where(s => s.State == DataState.Problem && s.Area != "Application Integrity").Select(s => ($"{s.Area}: {s.Status}", DataState.Problem)));
        SubTitle("Warnungen und Hinweise");
        if (warnings.Count == 0) Paragraph("Keine Warnungen – alle erfassten Prüfpunkte sind in Ordnung.", 9, Ok);
        foreach (var (text, st) in warnings.DistinctBy(w => w.Text).OrderBy(w => w.State == DataState.Problem ? 0 : 1).Take(14))
        {
            var lines = FontMetrics.Wrap(T(text), 8.8, false, ContentW - 14);
            Ensure(lines.Count * 11 + 4);
            Dot(Margin, _y + 6, st);
            for (var k = 0; k < lines.Count; k++) Add(new TextOp(Margin + 12, _y + 9 + k * 11, lines[k], 8.8, false, Text));
            _y += lines.Count * 11 + 3;
        }
        _y += 8;

        LineChart("Auslastung der letzten Minuten", 90,
            ("CPU", _d.CpuHistory, Red), ("GPU", _d.GpuHistory, Graphite), ("RAM", _d.RamHistory, RColor.Hex("9A9A9A")));

        SubTitle("Inhalt dieses Berichts");
        var included = new List<string>();
        if (_o.Hardware) included.Add(T("Hardware"));
        if (_o.Sensors) included.Add(T("Sensoren"));
        if (_o.Network) included.Add(T("Netzwerk"));
        if (_o.Security) included.Add(T("Sicherheit"));
        if (_o.Drivers) included.Add(T("Treiber"));
        if (_o.WindowsHealth) included.Add("Windows Health");
        if (_o.ComponentTests) included.Add(T("Komponententests"));
        if (_o.Benchmarks) included.Add("Benchmarks");
        if (_o.CrashAnalysis) included.Add(T("Absturzanalyse"));
        Paragraph(string.Join(" · ", included), 9);
        Paragraph(_o.PrivacyMode
            ? "Datenschutzmodus AN: IP- und MAC-Adressen, Benutzer- und Computername, Seriennummern sowie Gateway/DNS sind maskiert oder ausgeblendet."
            : "Datenschutzmodus AUS: Der Bericht enthält vollständige Netzwerk- und Gerätedaten. Nur an vertrauenswürdige Personen weitergeben.",
            8.5, _o.PrivacyMode ? Muted : Red);
    }

    // ================================================================== Abschnitte

    private void HardwareSection()
    {
        Section("Hardware");
        var hw = _d.Hardware;
        if (hw is null) { Paragraph("Hardwaredaten nicht verfügbar.", 9, Muted); return; }
        SubTitle("System");
        KeyValues(
        [
            ("Windows", $"{hw.Os.Caption} {hw.Os.DisplayVersion}", null),
            ("Build / Architektur", $"{hw.Os.Build} · {hw.Os.Architecture}", null),
            ("Installiert am", Format.DateText(hw.Os.InstallDate), null),
            ("Betriebszeit", Format.Duration(hw.Os.Uptime), null),
            ("Benutzer", _o.PrivacyMode ? T("(ausgeblendet)") : _d.UserName ?? "–", null),
            ("Computername", _o.PrivacyMode ? T("(ausgeblendet)") : hw.Os.ComputerName, null),
        ]);
        SubTitle("Prozessor und Grafik");
        var items = new List<(string, string, DataState?)>();
        if (hw.Cpu is { } c)
        {
            items.Add(("CPU", c.Name, null));
            items.Add(("Kerne / Threads / Takt", $"{c.Cores} / {c.Threads} / {c.MaxClockMHz} MHz", null));
            items.Add(("Cache L2 / L3", $"{(c.L2CacheKB is { } l2 ? l2 / 1024.0 + " MB" : "–")} / {(c.L3CacheKB is { } l3 ? l3 / 1024.0 + " MB" : "–")}", null));
            items.Add(("Sockel", c.Socket ?? "–", null));
        }
        foreach (var g in hw.Gpus)
        {
            items.Add(("GPU", g.Name, null));
            items.Add(("VRAM / Treiber", $"{Format.Bytes(g.VramBytes)} · {g.DriverVersion ?? "–"} ({Format.DateText(g.DriverDate)})", null));
        }
        KeyValues(items);

        SubTitle("Arbeitsspeicher");
        KeyValues([("Installiert", Format.Bytes(hw.Memory.TotalBytes), null), ("Steckplätze belegt", $"{hw.Memory.Modules.Count}" + (hw.Memory.SlotCount is { } s ? $" / {s}" : ""), null)]);
        Table([new("Steckplatz", 1.2), new("Größe", 1), new("Takt", 1), new("Hersteller", 1.3), new("Teilenummer", 1.5)],
            hw.Memory.Modules.Select(m => (Row(m.Slot, Format.Bytes(m.CapacityBytes), m.SpeedMTs is { } sp ? $"{sp} MT/s" : "–", m.Manufacturer ?? "–", m.PartNumber ?? "–"), (DataState?)null)));

        SubTitle("Mainboard und Firmware");
        KeyValues(
        [
            ("Hersteller / Modell", $"{hw.Board.Manufacturer} {hw.Board.Product}".Trim(), null),
            ("Seriennummer", _o.PrivacyMode ? T("(ausgeblendet)") : hw.Board.SerialNumber ?? "–", null),
            ("Firmware", hw.Board.Firmware, null),
            ("BIOS", $"{hw.Board.BiosVendor} {hw.Board.BiosVersion} ({Format.DateText(hw.Board.BiosDate)})", null),
            ("Secure Boot", hw.Board.SecureBoot, hw.Board.SecureBootState),
            ("TPM", hw.Board.Tpm, hw.Board.TpmState),
        ]);

        SubTitle("Datenträger");
        Table([new("Laufwerk", 2.4), new("Typ", 0.8), new("Bus", 0.7), new("Größe", 0.9), new("Zustand", 1), new("SMART", 1.8, true)],
            hw.Disks.Select(d =>
            {
                var sm = _d.Smart.FirstOrDefault(x => x.Name.Equals(d.Name, StringComparison.OrdinalIgnoreCase));
                var smart = sm is null ? "–" : $"{(sm.TemperatureC is { } t ? t + " °C" : "")} {(sm.RemainingLifePercent is { } rl ? rl + " % " + T("Rest") : "")} {(sm.PowerOnHours is { } h ? h + " h" : "")}".Trim();
                return (Row(d.Name, d.MediaType, d.BusType, Format.Bytes(d.SizeBytes), d.Health, smart.Length == 0 ? "–" : smart), (DataState?)d.HealthState);
            }), T("Keine Datenträger gefunden."));
        Table([new("Volume", 0.8), new("Bezeichnung", 1.6), new("Dateisystem", 1), new("Größe", 1), new("Frei", 1), new("Belegt", 0.8)],
            hw.Volumes.Select(v => (Row(v.Letter, v.Label ?? "–", v.FileSystem ?? "–", Format.Bytes(v.SizeBytes), Format.Bytes(v.FreeBytes), v.UsedPercent is { } p ? $"{p:0} %" : "–"),
                (DataState?)(v.UsedPercent is >= 95 ? DataState.Problem : v.UsedPercent is >= 85 ? DataState.Warning : DataState.Ok))));
    }

    private void SensorSection()
    {
        Section("Sensoren");
        Table([new("Quelle", 1.6), new("Status", 1.1), new("Werte", 0.7), new("Verwendet", 0.8), new("Hinweis", 3, true)],
            _d.SensorSources.Select(s => (Row(s.Name, s.StateText, s.SensorCount.ToString(CultureInfo.InvariantCulture), s.UsedSensors.ToString(CultureInfo.InvariantCulture), s.Detail),
                (DataState?)(s.State switch { SensorSourceState.Connected => DataState.Ok, SensorSourceState.Available => DataState.Ok, SensorSourceState.Inactive => DataState.Warning, _ => DataState.NotAvailable }))));
        Paragraph("Priorität: HWiNFO → LibreHardwareMonitor → Windows nativ → ACPI. Es werden nur gemessene Werte angezeigt.", 8, Muted);
        Table([new("Sensor", 1.5), new("Bezeichnung", 1.9), new("Aktuell", 0.8), new("Min", 0.7), new("Ø", 0.7), new("Max", 0.7), new("Quelle", 1.3)],
            _d.Sensors.Select(s =>
            {
                string F(double v) => $"{v.ToString(s.Unit is "MHz" or "RPM" ? "0" : "0.#", Loc.Culture)} {s.Unit}".Trim();
                var st = s.Unit == "°C" ? (s.Current >= 90 ? DataState.Problem : s.Current >= 80 ? DataState.Warning : DataState.Ok) : (DataState?)null;
                return (Row(s.Code, s.Label, F(s.Current), F(s.Min), F(s.Average), F(s.Max), s.SourceName), st);
            }), T("Keine Sensorwerte verfügbar."));
        LineChart("CPU / GPU / RAM – Auslastung (%)", 90, ("CPU", _d.CpuHistory, Red), ("GPU", _d.GpuHistory, Graphite), ("RAM", _d.RamHistory, RColor.Hex("9A9A9A")));
    }

    private void NetworkSection()
    {
        Section("Netzwerk");
        Table([new("Adapter", 1.8), new("Typ", 0.55), new("Status", 0.85), new("Link", 0.9), new("IPv4", 1.25), new("MAC", 1.75), new("DHCP", 0.5)],
            _d.Adapters.Select(a => (Row(a.Description, a.Kind, a.IsUp ? T("verbunden") : T("getrennt"),
                a.LinkSpeedBps is > 0 ? $"{a.LinkSpeedBps.Value / 1_000_000} Mbit/s" : "–", MaskIps(a.IPv4), Mask(a.MacAddress, SensitiveMasker.MaskMac), a.DhcpEnabled ? T("ja") : T("nein")),
                (DataState?)(a.IsUp ? DataState.Ok : DataState.NotAvailable))), T("Keine Netzwerkadapter gefunden."));
        var up = _d.Adapters.Where(a => a.IsUp && a.Gateways.Count > 0).ToList();
        if (up.Count > 0)
            KeyValues(
            [
                ("Standardgateway", MaskIps(up.SelectMany(a => a.Gateways).Distinct()), null),
                ("DNS-Server", MaskIps(up.SelectMany(a => a.DnsServers).Distinct().Take(4)), null),
            ]);
        SubTitle("Speedtests");
        Table([new("Datum", 1.3), new("Download", 1), new("Upload", 1), new("Ping", 0.8), new("Jitter", 0.8), new("Server", 1.6)],
            _d.Speedtests.OrderByDescending(s => s.Time).Take(8).Select(s => (Row(Format.DateTimeText(s.Time),
                Format.BitsPerSecond(s.DownloadBps), Format.BitsPerSecond(s.UploadBps), $"{s.PingMs:0} ms", $"{s.JitterMs:0.0} ms", s.Server), (DataState?)null)),
            T("Noch kein Speedtest durchgeführt."));
        if (_o.PrivacyMode) Paragraph("Öffentliche IP-Adresse: nicht enthalten (Datenschutzmodus).", 8, Muted);
    }

    private void SecuritySection()
    {
        Section("Sicherheit");
        Table([new("Bereich", 1.6), new("Status", 2), new("Details", 2.4, true)],
            _d.Security.Select(s => (Row(s.Area, s.Status, s.Detail ?? ""), (DataState?)s.State)),
            T("Sicherheitsstatus nicht verfügbar."));
        Paragraph("Alle Prüfungen sind nur lesend. ULTIMATE X deaktiviert niemals Defender, Firewall oder UAC und legt keine Ausnahmen an.", 8, Muted);
    }

    private void DriverSection()
    {
        Section("Treiber");
        if (_d.Hardware is { } hw)
            Table([new("Grafikkarte", 2.2), new("Treiberversion", 1.3), new("Datum", 1), new("Bewertung", 1.6)],
                hw.Gpus.Select(g =>
                {
                    var basic = g.Name.Contains("Basic Display", StringComparison.OrdinalIgnoreCase);
                    var old = g.DriverDate is { } dt && dt < DateTime.Now.AddYears(-2);
                    return (Row(g.Name, g.DriverVersion ?? "–", Format.DateText(g.DriverDate),
                        basic ? T("Kein Herstellertreiber") : old ? T("älter als 2 Jahre") : "OK"),
                        (DataState?)(basic ? DataState.Problem : old ? DataState.Warning : DataState.Ok));
                }));
        SubTitle("Geräte mit Problemen (Geräte-Manager)");
        Table([new("Gerät", 2.4), new("Klasse", 1), new("Fehler", 2, true)],
            _d.ProblemDevices.Take(40).Select(p => (Row(p.Name, p.Class, p.ErrorText), (DataState?)DataState.Problem)),
            T("Keine Geräte mit Fehlercode – alle Treiber geladen."));
    }

    private void WindowsHealthSection()
    {
        Section("Windows Health");
        KeyValues(
        [
            ("Neustart ausstehend", _d.Reboot is null ? "–" : _d.Reboot.RebootRequired ? string.Join("; ", _d.Reboot.Reasons) : T("nein"),
                _d.Reboot is null ? DataState.NotAvailable : _d.Reboot.RebootRequired ? DataState.Warning : DataState.Ok),
            ("Bluescreens (30 Tage)", _d.Crashes?.BugChecks.ToString(CultureInfo.InvariantCulture) ?? "–",
                _d.Crashes is null ? DataState.NotAvailable : _d.Crashes.BugChecks == 0 ? DataState.Ok : DataState.Problem),
            ("Unerwartete Neustarts (30 Tage)", _d.Crashes?.UnexpectedShutdowns.ToString(CultureInfo.InvariantCulture) ?? "–",
                _d.Crashes is null ? DataState.NotAvailable : _d.Crashes.UnexpectedShutdowns > 2 ? DataState.Warning : DataState.Ok),
            ("Programmabstürze (30 Tage)", _d.Crashes?.AppCrashes.ToString(CultureInfo.InvariantCulture) ?? "–", null),
        ]);
        SubTitle("Wichtige Dienste");
        Table([new("Dienst", 2.2), new("Name", 1.1), new("Status", 1), new("Starttyp", 1)],
            _d.Services.Select(s => (Row(s.DisplayName, s.Name, s.State, s.StartMode), (DataState?)s.Health)));
        if (_d.Score is { } score)
        {
            SubTitle("Health Score – Abzüge");
            Table([new("Faktor", 1.4), new("Abzug", 0.6), new("Begründung", 3.4, true)],
                score.Factors.Select(f => (Row(f.Name, $"−{f.Deduction}", f.Reason), (DataState?)DataState.Warning)),
                T("Keine Abzüge – Score 100."));
        }
    }

    private void ComponentTestSection()
    {
        Section("Komponententests");
        var run = _d.ComponentTests;
        if (run is null) { Paragraph("Noch kein Komponententest durchgeführt (Seite „Komponententest“ → TEST ALL SYSTEM COMPONENTS).", 9, Muted); return; }
        KeyValues(
        [
            ("Lauf", $"{run.Started.ToString(Loc.IsEnglish ? "MM/dd/yyyy HH:mm" : "dd.MM.yyyy HH:mm", Loc.Culture)} · {run.Results.Count} Tests", null),
            ("Gesamtergebnis", run.Overall.Label(), run.Overall.ToDataState()),
            ("PASS / WARNING / FAIL", $"{run.Count(TestStatus.Pass)} / {run.Count(TestStatus.Warning)} / {run.Count(TestStatus.Fail)}", null),
            ("NOT AVAILABLE / USER ACTION", $"{run.Count(TestStatus.NotAvailable)} / {run.Count(TestStatus.UserActionRequired)}", null),
        ]);
        foreach (var group in run.Results.GroupBy(r => r.Category))
        {
            SubTitle(ComponentTestReport.CategoryName(group.Key));
            Table([new("Komponente", 1.3), new("Status", 1.2), new("Ergebnis", 4, true)],
                group.Select(r => (Row(r.Name, r.Status.Label(), r.Summary), (DataState?)r.Status.ToDataState())));
        }
    }

    private void BenchmarkSection()
    {
        Section("Benchmarks");
        if (_d.Benchmarks.Count == 0) { Paragraph("Noch keine Benchmarks ausgeführt.", 9, Muted); return; }
        var latest = _d.Benchmarks.GroupBy(b => b.Kind).Select(g => g.OrderByDescending(b => b.Time).First()).ToList();
        // Balkendiagramm: letzte Ergebnisse je Art relativ zum besten eigenen Ergebnis dieser Art.
        Ensure(30 + latest.Count * 22);
        foreach (var b in latest)
        {
            var best = _d.Benchmarks.Where(x => x.Kind == b.Kind).Max(x => x.Score);
            var frac = best > 0 ? b.Score / best : 0;
            Add(new TextOp(Margin, _y + 11, T(b.Kind), 9, true, Text));
            Chamfer(Margin + 90, _y + 2, ContentW - 190, 12, Light, null, 4);
            Chamfer(Margin + 90, _y + 2, Math.Max(4, (ContentW - 190) * frac), 12, Red, null, 4);
            var label = $"{b.Score.ToString("0", Loc.Culture)} {T("Punkte")}";
            Add(new TextOp(Margin + ContentW - 90, _y + 11, label, 9, false, Text));
            _y += 20;
        }
        Paragraph("Balken: letztes Ergebnis im Verhältnis zum besten eigenen Ergebnis derselben Testart.", 8, Muted);
        Table([new("Datum", 1.2), new("Test", 0.9), new("Punkte", 0.8), new("Messwerte", 3.6, true)],
            _d.Benchmarks.OrderByDescending(b => b.Time).Take(15).Select(b => (Row(Format.DateTimeText(b.Time), b.Kind, b.Score.ToString("0", Loc.Culture),
                string.Join(" · ", b.Metrics.Take(5).Select(m => $"{m.Name} {m.Value.ToString("0.#", Loc.Culture)} {m.Unit}"))), (DataState?)null)));
    }

    private void AnalysisSection()
    {
        Section("Bewertung und Empfehlungen");
        var a = _d.Analysis;
        if (a is null) { Paragraph("Keine Bewertungsdaten vorhanden.", 9, Muted); return; }
        static string Score(int? v) => v is { } x ? $"{x}/100" : "N/A";
        static DataState? State(int? v) => v is null ? DataState.NotAvailable : v >= 60 ? DataState.Ok : v >= 35 ? DataState.Warning : DataState.Problem;
        var s = a.Performance.Scores;
        KeyValues(
        [
            ("ULTIMATE X Performance Score", a.Performance.Total is { } t ? $"{t}/100 · {a.Performance.TierText}" : "N/A", State(a.Performance.Total)),
            ("Gaming Score", Score(a.GamingScore), State(a.GamingScore)),
            ("CPU", Score(s.Cpu), State(s.Cpu)),
            ("GPU", Score(s.Gpu), State(s.Gpu)),
            ("RAM", Score(s.Ram), State(s.Ram)),
            ("SSD", Score(s.Storage), State(s.Storage)),
            ("Netzwerk (Network Health Score)", Score(a.NetworkScore), State(a.NetworkScore)),
            ("Kühlung", Score(s.Cooling), State(s.Cooling)),
            ("RAM-Test", a.MemoryTest ?? "Noch nicht ausgeführt", a.MemoryTestState),
        ]);
        Paragraph("Punkteskala geschätzt: 100 ≈ aktuelle Spitzenklasse für Endanwender. Schätzwerte sind als ESTIMATED gekennzeichnet; nicht messbare Werte als N/A.", 8, Muted);
        if (a.Drives.Count > 0)
        {
            SubTitle("Laufwerke");
            Table([new("Laufwerk", 3), new("Zustand", 1)], a.Drives.Select(d => (Row(d.Drive, d.Verdict), (DataState?)d.State)));
        }
        SubTitle("Gefundene Probleme");
        Table([new("Bereich", 1.4), new("Befund", 4, true)], a.Problems.Select(p => (Row(p.Area, p.Value + (p.Advice is null ? "" : " – " + p.Advice)), (DataState?)p.State)),
            "Keine Probleme gefunden.");
        SubTitle("Empfehlungen");
        if (a.Recommendations.Count == 0) Paragraph("Keine Maßnahmen erforderlich.", 9, Muted);
        foreach (var r in a.Recommendations) Paragraph("• " + r, 9);
    }

    private void CrashSection()
    {
        Section("Absturzanalyse");
        var c = _d.CrashReport;
        if (c is null) { Paragraph("Absturzdaten nicht verfügbar.", 9, Muted); return; }
        KeyValues(
        [
            ("Zeitraum", $"{c.Days} {T("Tage")}", null),
            ("Bluescreens", c.BlueScreens.Count.ToString(CultureInfo.InvariantCulture), c.BlueScreens.Count == 0 ? DataState.Ok : DataState.Problem),
            ("Stromausfall/Reset ohne Herunterfahren", c.PowerLossResets.ToString(CultureInfo.InvariantCulture), c.PowerLossResets > 2 ? DataState.Warning : DataState.Ok),
            ("Grafiktreiber-Neustarts", c.DisplayDriverResets.ToString(CultureInfo.InvariantCulture), c.DisplayDriverResets > 0 ? DataState.Warning : DataState.Ok),
        ]);
        if (c.Hints.Count > 0)
        {
            SubTitle("Diagnose");
            Table([new("Hinweis", 1.6), new("Details", 4, true)], c.Hints.Select(h => (Row(h.Title, h.Detail), (DataState?)h.Severity)));
        }
        SubTitle("Bluescreens");
        Table([new("Datum", 1.1), new("Code", 1), new("Bezeichnung", 1.8), new("Bedeutung", 3, true)],
            c.BlueScreens.Take(20).Select(b => (Row(Format.DateTimeText(b.Time), $"0x{b.Code:X8}", b.Info.Name, b.Info.Meaning), (DataState?)DataState.Problem)),
            T("Keine Bluescreens im Zeitraum."));
        SubTitle("Hardwarefehler (WHEA)");
        Table([new("Art", 1.4), new("Ereignis", 0.7), new("Anzahl", 0.6), new("Zuletzt", 1), new("Erklärung", 3, true)],
            c.HardwareErrors.Select(h => (Row(h.Kind, h.EventId.ToString(CultureInfo.InvariantCulture), h.Count.ToString(CultureInfo.InvariantCulture), Format.DateTimeText(h.Last), h.Explanation), (DataState?)DataState.Warning)),
            T("Keine Hardwarefehler protokolliert."));
        SubTitle("Programmabstürze");
        Table([new("Programm", 1.6), new("Modul", 1.4), new("Code", 1), new("Anzahl", 0.6), new("Zuletzt", 1.1)],
            c.AppCrashes.Take(12).Select(a => (Row(a.Application, a.Module, a.IsHang ? T("hängt") : a.ExceptionCode, a.Count.ToString(CultureInfo.InvariantCulture), Format.DateTimeText(a.Last)), (DataState?)null)),
            T("Keine Programmabstürze protokolliert."));
    }

    // ================================================================== Fußzeilen

    private void Footers()
    {
        var n = _doc.Pages.Count;
        for (var i = 0; i < n; i++)
        {
            var p = _doc.Pages[i];
            var y = ReportPage.Height - 30;
            p.Ops.Add(new LineOp(Margin, y - 10, ReportPage.Width - Margin, y - 10, Line, 0.5));
            p.Ops.Add(new RectOp(Margin, y - 10.5, 40, 1.5, Red, null));
            var left = $"ULTIMATE X Windows Control v{_d.AppVersion} · {T("Bericht")} {_d.ReportId} · {_d.Created.ToString(Loc.IsEnglish ? "MM/dd/yyyy HH:mm" : "dd.MM.yyyy HH:mm", Loc.Culture)}" +
                       (_o.PrivacyMode ? " · " + T("Datenschutzmodus AN") : "");
            p.Ops.Add(new TextOp(Margin, y + 2, left, 7, false, Muted));
            var right = $"{T("Seite")} {i + 1} / {n}";
            p.Ops.Add(new TextOp(ReportPage.Width - Margin - FontMetrics.Width(right, 7, true), y + 2, right, 7, true, Text));
        }
    }
}
