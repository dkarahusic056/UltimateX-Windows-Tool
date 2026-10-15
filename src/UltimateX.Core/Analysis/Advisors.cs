namespace UltimateX.Core.Analysis;

/// <summary>Gemeinsame Eingaben für Bottleneck-Analyse, Upgrade-Berater und Gaming Check. Null = unbekannt.</summary>
public sealed record SystemFacts
{
    public ComponentScores Scores { get; init; } = ComponentScores.Empty;
    public string? CpuName { get; init; }
    public int? CpuCores { get; init; }
    public string? GpuName { get; init; }
    public double? RamGb { get; init; }
    public double? RamUsedPercent { get; init; }
    public int? VramGb { get; init; }
    public double? VramUsedPercent { get; init; }
    public bool? RamBelowRatedSpeed { get; init; }
    public bool? RamSingleChannel { get; init; }
    public bool? SystemDriveIsSsd { get; init; }
    public double? CpuMaxTempC { get; init; }
    public double? GpuMaxTempC { get; init; }
    public bool? CpuThrottled { get; init; }
    public bool? GpuThrottled { get; init; }
    public string? PowerPlan { get; init; }
    public bool? PowerPlanIsSaver { get; init; }
    public IReadOnlyList<string> HeavyBackgroundProcesses { get; init; } = [];
    public int? PcieCurrentWidth { get; init; }
    public int? PcieMaxWidth { get; init; }
    public int? GpuDriverAgeDays { get; init; }
    public bool? GameModeOn { get; init; }
    public bool? HardwareSchedulingOn { get; init; }
    public bool? ResizableBarOn { get; init; }
    public double? PingMs { get; init; }
    public bool? SecureBootOn { get; init; }
    public bool? TpmReady { get; init; }
}

public sealed record BottleneckReport(string Summary, IReadOnlyList<Finding> Findings);

/// <summary>
/// BOTTLENECK ANALYZER (v1.2 §11): erkennt die wahrscheinlich begrenzende Komponente aus Messwerten und
/// Konfiguration. Aussagen sind bewusst vorsichtig formuliert („wahrscheinlich“), da ohne Spiel-Telemetrie
/// keine eindeutige Ursache feststellbar ist.
/// </summary>
public static class BottleneckAnalyzer
{
    public static BottleneckReport Analyze(SystemFacts f)
    {
        var list = new List<Finding>();
        var s = f.Scores;

        // CPU ↔ GPU-Verhältnis aus den Benchmarks.
        if (s.Cpu is { } cpu && s.Gpu is { } gpu)
        {
            var diff = gpu - cpu;
            list.Add(diff switch
            {
                >= 20 => new Finding("CPU Limit", CheckStatus.Warning, $"CPU {cpu}/100 · GPU {gpu}/100",
                    "Die Grafikkarte ist deutlich stärker als der Prozessor. In Spielen mit vielen Objekten oder hohen Bildraten bremst wahrscheinlich die CPU."),
                <= -20 => new Finding("GPU Limit", CheckStatus.Warning, $"GPU {gpu}/100 · CPU {cpu}/100",
                    "Der Prozessor ist deutlich stärker als die Grafikkarte. In hohen Auflösungen und Details begrenzt wahrscheinlich die GPU."),
                _ => new Finding("CPU/GPU-Balance", CheckStatus.Pass, $"CPU {cpu}/100 · GPU {gpu}/100", "CPU und GPU passen leistungsmäßig gut zusammen."),
            });
        }
        else list.Add(new Finding("CPU/GPU-Balance", CheckStatus.NotAvailable, "N/A", "Für diese Bewertung zuerst den Complete Benchmark ausführen."));

        if (f.RamGb is { } ram)
            list.Add(ram < 8 ? new Finding("RAM Limit", CheckStatus.Fail, $"{ram:0} GB", "Weniger als 8 GB Arbeitsspeicher – Windows lagert aus, Spiele und Browser stocken.")
                : ram < 16 ? new Finding("RAM Limit", CheckStatus.Warning, $"{ram:0} GB", "16 GB sind für aktuelle Spiele die übliche Empfehlung.")
                : f.RamUsedPercent is > 85 ? new Finding("RAM Limit", CheckStatus.Warning, $"{f.RamUsedPercent:0} % belegt", "Der Arbeitsspeicher ist aktuell fast voll – Programme im Hintergrund schließen.")
                : new Finding("RAM Limit", CheckStatus.Pass, $"{ram:0} GB", "Ausreichend Arbeitsspeicher."));
        else list.Add(new Finding("RAM Limit", CheckStatus.NotAvailable, "N/A"));

        if (f.RamBelowRatedSpeed == true)
            list.Add(new Finding("RAM-Takt", CheckStatus.Warning, "unter Nenntakt", "Der RAM läuft langsamer als vorgesehen – XMP/EXPO im BIOS aktivieren."));
        if (f.RamSingleChannel == true)
            list.Add(new Finding("RAM-Kanäle", CheckStatus.Warning, "Single Channel", "Nur ein Speicherkanal belegt – Dual Channel (zwei Module) bringt deutlich mehr Bandbreite."));

        if (f.VramGb is { } vram)
            list.Add(vram < 6 ? new Finding("VRAM Limit", CheckStatus.Warning, $"{vram} GB", "Wenig Grafikspeicher – Texturdetails reduzieren.")
                : f.VramUsedPercent is > 90 ? new Finding("VRAM Limit", CheckStatus.Warning, $"{f.VramUsedPercent:0} % belegt", "Der Grafikspeicher ist nahezu voll.")
                : new Finding("VRAM Limit", CheckStatus.Pass, $"{vram} GB"));
        else list.Add(new Finding("VRAM Limit", CheckStatus.NotAvailable, "N/A"));

        list.Add(f.SystemDriveIsSsd switch
        {
            false => new Finding("Storage Limit", CheckStatus.Warning, "Systemlaufwerk ist eine HDD", "Windows und Spiele auf einer Festplatte laden langsam – eine SSD ist die wirksamste Aufrüstung."),
            true when s.Storage is < 40 => new Finding("Storage Limit", CheckStatus.Warning, $"Datenträger {s.Storage}/100", "Der gemessene Datenträger ist langsam."),
            true => new Finding("Storage Limit", CheckStatus.Pass, "SSD"),
            _ => new Finding("Storage Limit", CheckStatus.NotAvailable, "N/A"),
        });

        var throttled = f.CpuThrottled == true || f.GpuThrottled == true;
        if (f.CpuThrottled is null && f.GpuThrottled is null)
            list.Add(new Finding("Thermal Throttling", CheckStatus.NotAvailable, "N/A", "Wird während des Benchmarks erfasst."));
        else
            list.Add(throttled
                ? new Finding("Thermal Throttling", CheckStatus.Fail, f.CpuThrottled == true ? "CPU drosselt" : "GPU drosselt",
                    "Unter Last wurde der Takt wegen hoher Temperatur gesenkt – Kühlung, Lüfter und Staub prüfen.")
                : new Finding("Thermal Throttling", CheckStatus.Pass, "Keine Drosselung erkannt"));

        list.Add(f.HeavyBackgroundProcesses.Count switch
        {
            0 => new Finding("Hintergrundprozesse", CheckStatus.Pass, "Keine auffällige Last"),
            var n => new Finding("Hintergrundprozesse", CheckStatus.Warning, $"{n} Prozess(e) mit hoher Last",
                "Hohe CPU-Last durch: " + string.Join(", ", f.HeavyBackgroundProcesses.Take(5))),
        });

        if (f.PcieCurrentWidth is { } w && f.PcieMaxWidth is { } mw && mw > 0)
            list.Add(w < mw
                ? new Finding("PCIe Limit", CheckStatus.Warning, $"x{w} statt x{mw}",
                    "Die Grafikkarte ist mit weniger PCIe-Lanes angebunden als möglich – Steckplatz (oberster x16-Slot) und BIOS prüfen.")
                : new Finding("PCIe Limit", CheckStatus.Pass, $"x{w}"));
        else list.Add(new Finding("PCIe Limit", CheckStatus.NotAvailable, "N/A"));

        list.Add(f.PowerPlanIsSaver switch
        {
            true => new Finding("Energieprofil", CheckStatus.Warning, f.PowerPlan ?? "Energiespar-Profil", "Das Energiesparprofil begrenzt die Leistung – „Ausbalanciert“ oder „Höchstleistung“ wählen."),
            false => new Finding("Energieprofil", CheckStatus.Pass, f.PowerPlan ?? "OK"),
            _ => new Finding("Energieprofil", CheckStatus.NotAvailable, "N/A"),
        });

        var worst = list.Where(x => x.Status is CheckStatus.Fail).Concat(list.Where(x => x.Status is CheckStatus.Warning)).FirstOrDefault();
        var summary = worst is null
            ? list.All(x => x.Status == CheckStatus.NotAvailable)
                ? "Noch keine ausreichenden Daten – Complete Benchmark ausführen."
                : "Kein deutlicher Engpass erkannt."
            : $"Wahrscheinlichster Engpass: {worst.Area} – {worst.Advice ?? worst.Value}";
        return new BottleneckReport(summary, list);
    }
}

public sealed record UpgradeRecommendation(string Component, int Priority, string Reason);

public sealed record UpgradeAdvice(IReadOnlyList<UpgradeRecommendation> Upgrades, IReadOnlyList<string> FreeFixes, string Headline);

/// <summary>
/// UPGRADE ADVISOR (v1.2 §18): empfiehlt nur dann eine Aufrüstung, wenn eine Messung oder ein klarer Befund dafür spricht.
/// Konfigurationsprobleme (XMP, Energieprofil, Kühlung) werden zuerst als kostenlose Maßnahmen genannt.
/// </summary>
public static class UpgradeAdvisor
{
    public static UpgradeAdvice Advise(SystemFacts f)
    {
        var ups = new List<UpgradeRecommendation>();
        var free = new List<string>();
        var s = f.Scores;

        if (f.SystemDriveIsSsd == false)
            ups.Add(new UpgradeRecommendation("Storage", 1, "Windows läuft auf einer Festplatte (HDD). Eine SSD beschleunigt Start, Programme und Ladezeiten am stärksten."));

        if (f.RamGb is < 8)
            ups.Add(new UpgradeRecommendation("RAM", 1, $"Nur {f.RamGb:0} GB Arbeitsspeicher – mindestens 16 GB einbauen."));
        else if (f.RamGb is < 16)
            ups.Add(new UpgradeRecommendation("RAM", 2, $"{f.RamGb:0} GB Arbeitsspeicher – für aktuelle Spiele und viele Browser-Tabs sind 16–32 GB sinnvoll."));

        var gpu = HardwareDatabase.FindGpu(f.GpuName);
        if (s.Gpu is { } g && s.Cpu is { } c && g + 20 <= c)
            ups.Add(new UpgradeRecommendation("GPU", 2, $"Die Grafikkarte ({g}/100) liegt deutlich unter dem Prozessor ({c}/100)."));
        else if (gpu is { GameIndex: < 35 })
            ups.Add(new UpgradeRecommendation("GPU", 3, $"{gpu.Name} ist für aktuelle Spiele schwach (geschätzt)."));
        if (f.VramGb is < 6 && !ups.Any(u => u.Component == "GPU"))
            ups.Add(new UpgradeRecommendation("GPU", 3, $"Nur {f.VramGb} GB Grafikspeicher – neuere Spiele benötigen 8 GB und mehr."));

        if (s.Cpu is { } cc && s.Gpu is { } gg && cc + 20 <= gg)
            ups.Add(new UpgradeRecommendation("CPU", 2, $"Der Prozessor ({cc}/100) liegt deutlich unter der Grafikkarte ({gg}/100)."));

        if (f.CpuThrottled == true || f.GpuThrottled == true || f.CpuMaxTempC >= 95 || f.GpuMaxTempC >= 90)
            ups.Add(new UpgradeRecommendation("Cooling", 2, "Unter Last werden sehr hohe Temperaturen bzw. Drosselung gemessen. Zuerst reinigen und Lüfterkurve prüfen; reicht das nicht, einen besseren Kühler einbauen."));

        if (f.RamBelowRatedSpeed == true) free.Add("XMP/EXPO-Profil im BIOS aktivieren – der RAM läuft unter seinem Nenntakt.");
        if (f.RamSingleChannel == true) free.Add("RAM-Module auf beide Kanäle verteilen (Handbuch des Mainboards: meist Slot A2 + B2).");
        if (f.PowerPlanIsSaver == true) free.Add("Energieprofil von „Energiespar-Profil“ auf „Ausbalanciert“ umstellen.");
        if (f.GpuDriverAgeDays is > 180) free.Add("Grafiktreiber aktualisieren (älter als 6 Monate).");
        if (f.PcieCurrentWidth is { } w && f.PcieMaxWidth is { } mw && w < mw) free.Add("Grafikkarte im obersten x16-Steckplatz betreiben.");

        var ordered = ups.OrderBy(u => u.Priority).ToList();
        var headline = ordered.Count == 0
            ? (s.Cpu is null && s.Gpu is null ? "Für eine Empfehlung zuerst den Complete Benchmark ausführen." : "NO UPGRADE CURRENTLY REQUIRED")
            : $"Best Upgrade: {ordered[0].Component}";
        return new UpgradeAdvice(ordered, free, headline);
    }
}

public sealed record GamingCheckResult(int? Score, IReadOnlyList<Finding> Findings);

/// <summary>GAMING CHECK (v1.2 §12): bewertet Hardware und Windows-Einstellungen für Spiele.</summary>
public static class GamingCheck
{
    public static GamingCheckResult Evaluate(SystemFacts f)
    {
        var list = new List<Finding>();
        var cpu = HardwareDatabase.EstimateCpu(f.CpuName, f.CpuCores);
        var gpu = HardwareDatabase.FindGpu(f.GpuName);

        list.Add(!cpu.Recognized ? new Finding("CPU", CheckStatus.NotAvailable, f.CpuName ?? "N/A", "Modell nicht in der Datenbank.")
            : new Finding("CPU", cpu.GameIndex >= 95 ? CheckStatus.Pass : cpu.GameIndex >= 65 ? CheckStatus.Warning : CheckStatus.Fail,
                $"{f.CpuName} (Klasse {cpu.GameIndex}, geschätzt)"));
        list.Add(gpu is null ? new Finding("GPU", CheckStatus.NotAvailable, f.GpuName ?? "N/A", "Modell nicht in der Datenbank.")
            : new Finding("GPU", gpu.GameIndex >= 60 ? CheckStatus.Pass : gpu.GameIndex >= 30 ? CheckStatus.Warning : CheckStatus.Fail,
                $"{gpu.Name} (Klasse {gpu.GameIndex}, geschätzt)"));
        list.Add(f.RamGb is { } r ? new Finding("RAM", r >= 15.5 ? CheckStatus.Pass : r >= 7.5 ? CheckStatus.Warning : CheckStatus.Fail, $"{r:0} GB")
            : new Finding("RAM", CheckStatus.NotAvailable, "N/A"));
        list.Add(f.VramGb is { } v ? new Finding("VRAM", v >= 8 ? CheckStatus.Pass : v >= 4 ? CheckStatus.Warning : CheckStatus.Fail, $"{v} GB")
            : new Finding("VRAM", CheckStatus.NotAvailable, "N/A"));
        list.Add(f.SystemDriveIsSsd switch
        {
            true => new Finding("SSD", CheckStatus.Pass, "Systemlaufwerk ist eine SSD"),
            false => new Finding("SSD", CheckStatus.Fail, "Systemlaufwerk ist eine HDD", "Spiele auf einer SSD installieren."),
            _ => new Finding("SSD", CheckStatus.NotAvailable, "N/A"),
        });
        list.Add(f.GpuDriverAgeDays is { } age
            ? new Finding("GPU Driver", age <= 120 ? CheckStatus.Pass : age <= 365 ? CheckStatus.Warning : CheckStatus.Fail, $"{age} Tage alt",
                age > 120 ? "Neuen Treiber vom Hersteller (NVIDIA/AMD/Intel) oder über Windows Update installieren." : null)
            : new Finding("GPU Driver", CheckStatus.NotAvailable, "N/A"));
        list.Add(Bool("Game Mode", f.GameModeOn, "Aktiv", "Aus", "Einstellungen → Spielen → Spielmodus einschalten."));
        list.Add(Bool("HAGS", f.HardwareSchedulingOn, "Hardwarebeschleunigte GPU-Planung aktiv", "Aus",
            "Einstellungen → System → Anzeige → Grafikeinstellungen → Hardwarebeschleunigte GPU-Planung.", missingIsWarning: false));
        list.Add(f.ResizableBarOn switch
        {
            true => new Finding("Resizable BAR", CheckStatus.Pass, "Aktiv"),
            false => new Finding("Resizable BAR", CheckStatus.Warning, "Aus", "Im BIOS „Above 4G Decoding“ und „Resizable BAR“ aktivieren (sofern unterstützt)."),
            _ => new Finding("Resizable BAR", CheckStatus.NotAvailable, "N/A", "Unter Windows ohne Herstellerwerkzeug nicht zuverlässig auslesbar – im Treiber-Kontrollzentrum (NVIDIA/AMD) prüfen."),
        });
        list.Add(f.PowerPlanIsSaver switch
        {
            true => new Finding("Energieprofil", CheckStatus.Warning, f.PowerPlan ?? "Energiespar-Profil", "Auf „Ausbalanciert“ oder „Höchstleistung“ umstellen."),
            false => new Finding("Energieprofil", CheckStatus.Pass, f.PowerPlan ?? "OK"),
            _ => new Finding("Energieprofil", CheckStatus.NotAvailable, "N/A"),
        });
        var hottest = new[] { f.CpuMaxTempC, f.GpuMaxTempC }.Where(t => t is not null).Max();
        list.Add(hottest is { } h
            ? new Finding("Temperaturen", h >= 90 ? CheckStatus.Fail : h >= 82 ? CheckStatus.Warning : CheckStatus.Pass, $"max. {h:0} °C")
            : new Finding("Temperaturen", CheckStatus.NotAvailable, "N/A", "Keine Temperaturquelle – siehe Seite „Sensoren“."));
        list.Add(f.HeavyBackgroundProcesses.Count == 0
            ? new Finding("Hintergrundprozesse", CheckStatus.Pass, "Keine auffällige Last")
            : new Finding("Hintergrundprozesse", CheckStatus.Warning, string.Join(", ", f.HeavyBackgroundProcesses.Take(4)), "Vor dem Spielen schließen."));
        list.Add(f.PingMs is { } p
            ? new Finding("Netzwerk", p <= 30 ? CheckStatus.Pass : p <= 70 ? CheckStatus.Warning : CheckStatus.Fail, $"Ping {p:0} ms")
            : new Finding("Netzwerk", CheckStatus.NotAvailable, "N/A"));
        list.Add(Bool("Secure Boot", f.SecureBootOn, "Aktiv", "Aus", "Einige Spiele (Anti-Cheat) setzen Secure Boot voraus."));
        list.Add(Bool("TPM", f.TpmReady, "Bereit", "Nicht bereit", "Einige Spiele (Anti-Cheat) setzen TPM 2.0 voraus."));

        // Punktwert: Hardware zählt doppelt, Einstellungen einfach.
        string[] hardware = ["CPU", "GPU", "RAM", "VRAM", "SSD"];
        var parts = list.Select(x => (Score: x.Status switch
        {
            CheckStatus.Pass => (int?)100,
            CheckStatus.Warning => 60,
            CheckStatus.Fail => 15,
            _ => null,
        }, Weight: hardware.Contains(x.Area) ? 2.0 : 1.0));
        return new GamingCheckResult(ScoreMath.WeightedAverage(parts), list);
    }

    private static Finding Bool(string area, bool? value, string on, string off, string advice, bool missingIsWarning = true)
        => value switch
        {
            true => new Finding(area, CheckStatus.Pass, on),
            false => new Finding(area, missingIsWarning ? CheckStatus.Warning : CheckStatus.Warning, off, advice),
            _ => new Finding(area, CheckStatus.NotAvailable, "N/A"),
        };
}
