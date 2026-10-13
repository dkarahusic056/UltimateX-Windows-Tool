using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using UltimateX.Core.Common;
using UltimateX.Core.Logging;
using static UltimateX.Services.Desktop.ShellInterop;

namespace UltimateX.Services.Desktop;

public enum DesktopSide { Left, Right }

/// <summary>Regeln des Desktop Organizers (§14). Werden in den Einstellungen gespeichert.</summary>
public sealed class OrganizerRules
{
    public bool SortDescending { get; set; }
    public DesktopSide StartSide { get; set; } = DesktopSide.Left;
    /// <summary>Leere Spalten zwischen zwei Gruppen (0–3).</summary>
    public int GroupGap { get; set; } = 1;
    /// <summary>Zusätzlicher Abstand zum Bildschirmrand in Pixeln (0–200).</summary>
    public int EdgeMargin { get; set; } = 8;

    /// <summary>Kategorie → Dateiendungen. Reihenfolge der Kategorien = Reihenfolge auf dem Desktop.</summary>
    public List<OrganizerCategory> Categories { get; set; } = DefaultCategories();

    public static List<OrganizerCategory> DefaultCategories() =>
    [
        new("Programme", ".lnk .url .exe .appref-ms"),
        new("Ordner", ""),
        new("PDF", ".pdf"),
        new("Word", ".doc .docx .rtf .odt"),
        new("Excel", ".xls .xlsx .xlsm .csv .ods"),
        new("PowerPoint", ".ppt .pptx .odp"),
        new("Text", ".txt .md .log"),
        new("Bilder", ".jpg .jpeg .png .webp .gif .bmp .heic .svg"),
        new("Archive", ".zip .rar .7z .tar .gz"),
    ];

    internal void Sanitize()
    {
        GroupGap = Math.Clamp(GroupGap, 0, 3);
        EdgeMargin = Math.Clamp(EdgeMargin, 0, 200);
        if (!Enum.IsDefined(StartSide)) StartSide = DesktopSide.Left;
        Categories = Categories.Where(c => !string.IsNullOrWhiteSpace(c.Name)).Take(20).ToList();
        if (Categories.Count == 0) Categories = DefaultCategories();
    }
}

public sealed record OrganizerCategory(string Name, string Extensions)
{
    private static readonly Regex ExtRegex = new(@"^\.[a-z0-9\-]{1,15}$", RegexOptions.CultureInvariant);

    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlySet<string> ExtensionSet => Extensions
        .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
        .Select(e => (e.StartsWith('.') ? e : "." + e).ToLowerInvariant())
        .Where(e => ExtRegex.IsMatch(e))
        .ToHashSet();
}

public sealed record DesktopItem(string ParsingName, string DisplayName, string Category, int X, int Y);

public sealed record DesktopLayoutBackup(DateTime Created, List<DesktopItem> Items);

/// <summary>
/// Desktop Organizer (§14) über die offizielle Shell-Schnittstelle IFolderView der Desktop-Ansicht.
/// Garantien:
///  - Es werden ausschließlich SYMBOLPOSITIONEN verändert. Dateien werden nie verschoben, umbenannt oder gelöscht.
///  - Vor jeder Änderung wird das aktuelle Layout gesichert (backups\desktop-*.json); "Letztes Layout wiederherstellen" ist jederzeit möglich.
///  - Muss auf dem UI-Thread (STA) aufgerufen werden.
/// </summary>
public sealed class DesktopOrganizerService
{
    private const string SystemCategory = "System";
    private const string OtherCategory = "Andere";

    private static string RulesFile => Path.Combine(AppPaths.UserDataRoot, "desktop-rules.json");

    /// <summary>Gespeicherte Regeln laden (§14 "Regeln speichern"); beschädigte Datei → Standardregeln.</summary>
    public static OrganizerRules LoadRules()
    {
        try
        {
            if (File.Exists(RulesFile) && new FileInfo(RulesFile).Length < 64 * 1024)
            {
                var r = JsonSerializer.Deserialize<OrganizerRules>(File.ReadAllText(RulesFile));
                if (r is not null) { r.Sanitize(); return r; }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new OrganizerRules();
    }

    public static void SaveRules(OrganizerRules rules)
    {
        rules.Sanitize();
        Directory.CreateDirectory(AppPaths.UserDataRoot);
        File.WriteAllText(RulesFile, JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Aktuelle Symbole mit Position und Kategorie (ohne Änderung).</summary>
    public (IReadOnlyList<DesktopItem> Items, string? Error) ReadLayout(OrganizerRules rules)
    {
        try
        {
            using var view = DesktopView.Open();
            return (view.ReadItems(rules), null);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException)
        {
            return ([], "Desktop-Ansicht nicht erreichbar: " + ex.Message);
        }
    }

    /// <summary>Ordnet alle Symbole nach Regeln an. Sichert vorher das Layout.</summary>
    public OperationResult Organize(OrganizerRules rules)
    {
        rules.Sanitize();
        try
        {
            using var view = DesktopView.Open();
            if (view.IsAutoArrange)
                return OperationResult.Fail("„Symbole automatisch anordnen“ ist auf dem Desktop aktiv. Bitte per Rechtsklick auf den Desktop → Ansicht deaktivieren und erneut versuchen.");

            var items = view.ReadItems(rules);
            if (items.Count == 0) return OperationResult.Fail("Keine Desktopsymbole gefunden.");
            var backupFile = SaveBackup(items);

            var spacing = view.Spacing;
            var area = GetWorkArea();
            int margin = rules.EdgeMargin;
            int rowsPerColumn = Math.Max(1, (area.Bottom - area.Top - 2 * margin) / Math.Max(1, spacing.Y));
            int maxColumns = Math.Max(1, (area.Right - area.Left - 2 * margin) / Math.Max(1, spacing.X));

            // Gruppenreihenfolge: System → Regeln → Andere
            var order = new List<string> { SystemCategory };
            order.AddRange(rules.Categories.Select(c => c.Name));
            order.Add(OtherCategory);

            var targets = new Dictionary<string, POINT>(StringComparer.OrdinalIgnoreCase);
            int column = 0;
            foreach (var group in order)
            {
                var members = items.Where(i => i.Category == group);
                members = rules.SortDescending
                    ? members.OrderByDescending(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                    : members.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase);
                var list = members.ToList();
                if (list.Count == 0) continue;

                int row = 0;
                foreach (var item in list)
                {
                    if (row >= rowsPerColumn) { row = 0; column++; }
                    int colIndex = Math.Min(column, maxColumns - 1);
                    int x = rules.StartSide == DesktopSide.Left
                        ? area.Left + margin + colIndex * spacing.X
                        : area.Right - margin - (colIndex + 1) * spacing.X;
                    int y = area.Top + margin + row * spacing.Y;
                    targets[item.ParsingName] = new POINT(x, y);
                    row++;
                }
                column += 1 + rules.GroupGap;
            }

            var moved = view.Position(targets);
            AppLog.Info("DesktopOrganizer", "Desktop organisiert", $"{moved} Symbole, Backup {Path.GetFileName(backupFile)}");
            return OperationResult.Ok($"{moved} Symbole angeordnet. Das vorherige Layout wurde gesichert.");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or InvalidCastException or IOException)
        {
            AppLog.Error("DesktopOrganizer", "Organisieren fehlgeschlagen", ex);
            return OperationResult.Fail("Desktop konnte nicht organisiert werden: " + ex.Message);
        }
    }

    /// <summary>"Letztes Desktop Layout wiederherstellen".</summary>
    public OperationResult RestoreLastLayout()
    {
        var file = ListBackups().FirstOrDefault();
        if (file is null) return OperationResult.Fail("Es ist noch kein gesichertes Desktop-Layout vorhanden.");
        try
        {
            var backup = JsonSerializer.Deserialize<DesktopLayoutBackup>(File.ReadAllText(file));
            if (backup is null || backup.Items.Count == 0) return OperationResult.Fail("Backup ist leer oder beschädigt.");
            using var view = DesktopView.Open();
            if (view.IsAutoArrange) return OperationResult.Fail("Bitte zuerst „Symbole automatisch anordnen“ deaktivieren.");
            var targets = backup.Items
                .GroupBy(i => i.ParsingName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => new POINT(g.First().X, g.First().Y), StringComparer.OrdinalIgnoreCase);
            var moved = view.Position(targets);
            AppLog.Info("DesktopOrganizer", "Desktop-Layout wiederhergestellt", Path.GetFileName(file));
            return OperationResult.Ok($"Layout vom {backup.Created:dd.MM.yyyy HH:mm} wiederhergestellt ({moved} Symbole).");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or IOException or InvalidCastException)
        {
            return OperationResult.Fail("Wiederherstellung fehlgeschlagen: " + ex.Message);
        }
    }

    public static IReadOnlyList<string> ListBackups()
        => Directory.GetFiles(AppPaths.BackupDirectory, "desktop-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList();

    private static string SaveBackup(IReadOnlyList<DesktopItem> items)
    {
        var file = Path.Combine(AppPaths.BackupDirectory, $"desktop-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(new DesktopLayoutBackup(DateTime.Now, items.ToList()), new JsonSerializerOptions { WriteIndented = true }));
        // Nur die letzten 20 Backups behalten.
        foreach (var old in ListBackups().Skip(20))
        {
            try { File.Delete(old); } catch (IOException) { }
        }
        return file;
    }

    internal static string Categorize(string parsingName, bool isFolder, OrganizerRules rules)
    {
        if (parsingName.StartsWith("::", StringComparison.Ordinal)) return SystemCategory; // Papierkorb, Dieser PC …
        if (isFolder)
        {
            var folderCat = rules.Categories.FirstOrDefault(c => c.Name.Equals("Ordner", StringComparison.OrdinalIgnoreCase));
            if (folderCat is not null) return folderCat.Name;
        }
        var ext = Path.GetExtension(parsingName).ToLowerInvariant();
        foreach (var c in rules.Categories)
            if (ext.Length > 0 && c.ExtensionSet.Contains(ext)) return c.Name;
        return OtherCategory;
    }

    /// <summary>Kapselt die COM-Objekte der Desktop-Ansicht und gibt sie zuverlässig frei.</summary>
    private sealed class DesktopView : IDisposable
    {
        private readonly List<object> _com = [];
        private readonly IFolderView _view;
        private readonly IShellFolder _folder;

        private DesktopView(IFolderView view, IShellFolder folder, IEnumerable<object> com)
        {
            _view = view;
            _folder = folder;
            _com.AddRange(com);
        }

        public static DesktopView Open()
        {
            var type = Type.GetTypeFromCLSID(CLSID_ShellWindows) ?? throw new InvalidOperationException("ShellWindows nicht verfügbar.");
            var shellWindowsObj = Activator.CreateInstance(type) ?? throw new InvalidOperationException("ShellWindows nicht verfügbar.");
            var shellWindows = (IShellWindows)shellWindowsObj;

            object loc = 0;            // CSIDL_DESKTOP (VT_I4)
            object empty = null!;      // VT_EMPTY
            var hr = shellWindows.FindWindowSW(ref loc, ref empty, SWC_DESKTOP, out _, SWFO_NEEDDISPATCH, out var dispatch);
            if (hr != S_OK || dispatch is null) throw new InvalidOperationException("Desktop-Fenster nicht gefunden (Explorer läuft nicht?).");

            var sp = (ShellInterop.IServiceProvider)dispatch;
            var sid = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            Marshal.ThrowExceptionForHR(sp.QueryService(ref sid, ref iid, out var browserObj));
            var browser = (IShellBrowser)browserObj;
            Marshal.ThrowExceptionForHR(browser.QueryActiveShellView(out var shellView));
            var view = (IFolderView)shellView;
            var folderIid = IID_IShellFolder;
            Marshal.ThrowExceptionForHR(view.GetFolder(ref folderIid, out var folderObj));
            var folder = (IShellFolder)folderObj;

            return new DesktopView(view, folder, [shellWindowsObj, dispatch, browserObj, shellView, folderObj]);
        }

        public bool IsAutoArrange => _view.GetAutoArrange() == S_OK;

        public POINT Spacing
        {
            get
            {
                var p = new POINT();
                if (_view.GetSpacing(ref p) != S_OK || p.X <= 0 || p.Y <= 0) _view.GetDefaultSpacing(out p);
                if (p.X <= 0 || p.Y <= 0) p = new POINT(100, 100);
                return p;
            }
        }

        public List<DesktopItem> ReadItems(OrganizerRules rules)
        {
            var result = new List<DesktopItem>();
            Marshal.ThrowExceptionForHR(_view.ItemCount(SVGIO_ALLVIEW, out var count));
            for (int i = 0; i < count; i++)
            {
                if (_view.Item(i, out var pidl) != S_OK || pidl == IntPtr.Zero) continue;
                try
                {
                    var parsing = GetDisplayName(_folder, pidl, SHGDN_FORPARSING);
                    var display = GetDisplayName(_folder, pidl, SHGDN_NORMAL) ?? parsing;
                    if (parsing is null || display is null) continue;
                    _view.GetItemPosition(pidl, out var pt);
                    var isFolder = !parsing.StartsWith("::", StringComparison.Ordinal) && Directory.Exists(parsing);
                    result.Add(new DesktopItem(parsing, display, Categorize(parsing, isFolder, rules), pt.X, pt.Y));
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            return result;
        }

        /// <summary>Positioniert Symbole anhand ihres Parsing-Namens. Liefert die Anzahl bewegter Symbole.</summary>
        public int Position(IReadOnlyDictionary<string, POINT> targets)
        {
            Marshal.ThrowExceptionForHR(_view.ItemCount(SVGIO_ALLVIEW, out var count));
            int moved = 0;
            for (int i = 0; i < count; i++)
            {
                if (_view.Item(i, out var pidl) != S_OK || pidl == IntPtr.Zero) continue;
                try
                {
                    var parsing = GetDisplayName(_folder, pidl, SHGDN_FORPARSING);
                    if (parsing is null || !targets.TryGetValue(parsing, out var pt)) continue;
                    if (_view.SelectAndPositionItems(1, [pidl], [pt], SVSI_POSITIONITEM) == S_OK) moved++;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
            return moved;
        }

        public void Dispose()
        {
            foreach (var o in _com.Distinct())
            {
                try { if (Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
                catch (ArgumentException) { }
            }
        }
    }
}
