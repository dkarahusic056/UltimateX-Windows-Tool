# Architektur – ULTIMATE X Windows Control

## Überblick

```
┌──────────────────────────────────────────────────────────────────────┐
│ UltimateX.App (WPF, MVVM, asInvoker – läuft OHNE Adminrechte)        │
│  Views (DataTemplates, kein Code-Behind) ◄── ViewModels              │
│  Infrastructure: AppServices · MonitorHub · SensitiveActionGuard     │
│                  ThemeManager · Dialogs · ReportRenderer (Vorschau/  │
│                  Druck) · UiTranslator                               │
│  Controls: ChamferBorder · Ring · Gauge · PerformanceChart ·         │
│            LatencyChart · Sparkline                                  │
└───────────────┬──────────────────────────────────────────────────────┘
                │ ruft ausschließlich Services auf (§98)
┌───────────────▼──────────────────────────────────────────────────────┐
│ UltimateX.Services                                                   │
│  Hardware · Monitoring · Network · Speedtest · Drivers · Software    │
│  Startup · Health · Security · Authentication · SystemTools · Reports│
│  Sensors (SensorHub + SensorManager/Failover) · Diagnostics (Compo-  │
│  nent Test Center) · Reports.Pdf (Layout + PDF-Writer) · LivePing    │
│  (WMI, Registry, Win32, WinRT, EventLog – alles gekapselt)           │
└───────────────┬──────────────────────────────────────────────────────┘
                │
┌───────────────▼──────────────────────────────────────────────────────┐
│ UltimateX.Core (UI-frei, vollständig testbar)                        │
│  Security: CommandCatalog (Allowlist) · PathGuard · InputValidator   │
│            SignatureVerifier (WinVerifyTrust) · SensitiveMasker      │
│  Processes: ProcessRunner · ElevatedCommandRunner · HelperPipeProtocol│
│  Logging: AppLog (JSON Lines) · Settings: SettingsStore              │
└───────────────┬──────────────────────────────────────────────────────┘
                │ UAC ("runas") + Named Pipe (Ausgabe, abgesichert)
┌───────────────▼──────────────────────────────────────────────────────┐
│ UltimateX.AdminHelper.exe (requireAdministrator)                     │
│  nimmt NUR: <AllowedCommand> <RunId> [Parameter]                     │
│  validiert erneut, führt SFC/DISM/CHKDSK/fsutil/mdsched aus und      │
│  interne Aktionen: SMART, Restore Point, HKLM-Autostart, WU-Install  │
└──────────────────────────────────────────────────────────────────────┘
```

## Projekte

| Projekt | Zweck | Abhängigkeiten |
|---|---|---|
| `UltimateX.Core` | Sicherheitsbausteine, Prozessausführung, Logging, Settings | nur .NET |
| `UltimateX.Services` | Kapselung aller Systemzugriffe | Core, System.Management, System.Diagnostics.EventLog/PerformanceCounter, Microsoft.Data.Sqlite.Core, v1.3: Microsoft.Diagnostics.Tracing.TraceEvent (ETW) |
| `UltimateX.AdminHelper` | einziger erhöhter Prozess | Core |
| `UltimateX.App` | WPF-Oberfläche (MVVM) | Core, Services, CommunityToolkit.Mvvm, v1.3: Microsoft.Web.WebView2, Windows Forms (nur `NotifyIcon` im Infobereich) |
| `UltimateX.Core.Tests` | xUnit-Tests (Security, Auswertungslogik) | Core, Services |
| `installer/` | WiX-v5-MSI | WixToolset.Sdk |

## Prinzipien

1. **Least Privilege** – Die App läuft immer als Standardbenutzer. Adminrechte nur für einzelne Allowlist-Befehle über den Helper und UAC.
2. **Keine freien Befehle** – Es existiert kein Codepfad, der einen beliebigen String an eine Shell übergibt. `ProcessRunner` nutzt `UseShellExecute=false` + `ArgumentList`.
3. **Keine erfundenen Werte** – Jeder Wert trägt einen `DataState` (`Ok/Warning/Problem/NotAvailable/RequiresAdmin`). Nicht ermittelbar = „nicht verfügbar“.
4. **Fail Securely** – WMI-, Registry-, Netzwerk- und Parserfehler werden als Status zurückgegeben, nie als Absturz. Globale Handler protokollieren unerwartete Fehler.
5. **Authenticate → Explain → Confirm → Execute → Log** – zentral in `SensitiveActionGuard`.
6. **Ressourcenschonend** – Ein einziger 1-Hz-Sampler (`MonitorHub`) für alle Live-Werte, Messung auf dem Threadpool; Diagramme zeichnen direkt per `OnRender`.

## Datenhaltung (lokal, keine Cloud)

| Daten | Ort | Format |
|---|---|---|
| Einstellungen | `%LOCALAPPDATA%\UltimateX\settings.json` | JSON, atomar geschrieben, bei Beschädigung → Backup + Standardwerte |
| Logs | `%LOCALAPPDATA%\UltimateX\logs\app-*.jsonl`, `security-*.jsonl` | JSON Lines, 30 Tage |
| Backups | `%LOCALAPPDATA%\UltimateX\backups\` | JSON (z. B. Autostart vor Änderung) |
| Speedtest-/Health-Verlauf | `%LOCALAPPDATA%\UltimateX\history.db` | SQLite, 1 Jahr |
| Desktop-Regeln | `%LOCALAPPDATA%\UltimateX\desktop-rules.json` | JSON |
| SMART-Zwischenspeicher | `%LOCALAPPDATA%\UltimateX\smart-cache.json` | JSON |
| App-Update | `%LOCALAPPDATA%\UltimateX\updates\UltimateX-update.msi` | nur nach SHA-256- und Signaturprüfung |
| Helper-Ausgaben | keine Dateien mehr (v1.1): Named Pipe `UltimateX.Helper.<RunId>` | ACL, Peer-Prüfung, Nonce, Sequenz, Limits |
| Komponententest-Verlauf | `%LOCALAPPDATA%\UltimateX\ComponentTests\history.json` | JSON, max. 20 Läufe, atomar geschrieben |

Verlaufsdaten (Speedtest, PC Health Score) liegen in `%LOCALAPPDATA%\UltimateX\history.db` (SQLite, nur parametrisierte Statements, §71). Einstellungen und Regeln bleiben bewusst JSON (einfach prüfbar, atomar ersetzbar).

## v1.1-Bausteine

| Baustein | Aufgabe |
|---|---|
| `SensorManager` | Ordnet Rohwerte aus HWiNFO, LibreHardwareMonitor, Windows (D3DKMT, NVML, Leistungsindikatoren) und ACPI dem einheitlichen Modell zu (`UnifiedSensorId`), wählt je Sensor die Quelle mit der höchsten Priorität (Failover) und führt Min/Ø/Max. Aktualisierung alle 2 s im `LiveMonitorService`. |
| `ComponentTestService` | 42 nicht-destruktive Tests in fünf Partial-Dateien (Hardware, Storage, Network, Peripherals, Windows); teure Abfragen (PnP-Geräte, Sicherheit, Adapter, Datenträger) einmal je Lauf zwischengespeichert. Interaktive Tests (Ton, Mikrofon, Kamera, Farbtest, Eingabe, Testseite, Schreibtest) nur über die Oberfläche. |
| `LivePingService` | Ping-Serien/Dauerping über `System.Net.NetworkInformation.Ping`, Auswertung (`PingEvaluation`). |
| `PcReportBuilder` → `ReportDocument` | Geräteunabhängige Zeichenliste (Rechtecke, Linien, Pfade, Text). `PdfWriter` schreibt daraus PDF 1.4 (Helvetica, Flate), `ReportRenderer` zeichnet dieselbe Liste in WPF für Vorschau und Druck – identisches Layout. |
| `HelperPipeProtocol` | Rahmenformat (Längenpräfix + JSON), Schema-/Replay-Prüfung, Pipe-ACL, Peer-Prüfung (siehe THREAT_MODEL T-28). |

## v1.2-Bausteine

| Baustein | Aufgabe |
|---|---|
| `UltimateX.Core/Analysis` | Reine, getestete Logik ohne Windows-Abhängigkeit: `ScoreMath`/`PerformanceScore` (Teilwertungen, Gewichtung CPU 25 · GPU 30 · RAM 15 · SSD 15 · Netzwerk 5 · Kühlung 10, Einstufung), `MemoryAnalysis` (Kanäle, Takt, XMP aus Teilenummer), `MemoryPatterns` (RAM-Testmuster), `HealthEvaluators` (Laufwerk, Netzwerk, Windows Health Score), `HardwareDatabase` (GPU-Kenndaten, CPU-Klassen), `GameCompatibility` + `games.json` (Anforderungen, FPS-Schätzung), `Advisors` (Bottleneck, Upgrade, Gaming Check), `SoftwareCatalog` (Pakete, Quellen-/Hash-/Signaturprüfung, winget-Parser, Suchanfragen), `SnapshotComparer`. |
| `Services/Benchmark` | `BenchmarkService` (CPU 7 Lasten + Dauerlast, RAM, Datenträger), `GpuBenchmarkService` (Direct3D 11 Compute über Funktionszeiger, ohne Zusatzpaket), `MemoryTestService` (RAM-Stabilität, Boot-Test-Ergebnisse), `LoadMonitor` (Takt/Temperatur/Leistung/Throttling während der Tests), `CompleteBenchmarkService`. |
| `Services/Hardware` | `SystemInfoService` (RAM-Module, PCIe-Link, Energieprofil, Spielmodus, HAGS, Hintergrundlast, Firmware/VBS), `DriveHealthService`. |
| `Services/Setup` | `SoftwareSetupService` (Umgebungsprüfung, winget show → Prüfung → Installation), `OfficeDeploymentService` (ODT), `InstallLogService` (`install-log.jsonl`). |
| `Services/Security/ClamAvService`, `Services/System/SnapshotService`, `Services/Gaming/InstalledGamesService`, `Services/Network/NetworkBenchmarkService` | ClamAV-Zusatzscanner, System Snapshots, installierte Spiele (nur lokale Bibliotheksdateien), Netzwerk-Benchmark. |
| `App/Infrastructure/FactsCollector` | Sammelt einmal alle Fakten (Hardware, Verlauf, Einstellungen) für Gaming Check, Upgrade Advisor, Can My PC Run It und Bericht. |
| `App/Views/SuiteViews.xaml` | DataTemplates der neuen Seiten; gemeinsame Vorlagen (`FindingTemplate`, `ScoreRowTemplate`, `StepRowTemplate`) in `Themes/Templates.xaml`. |

Neue Ablageorte: `%LOCALAPPDATA%\UltimateX\snapshots\snapshot-*.json`, `install-log.jsonl`, `setup\office\` (ODT), `clamav\` (Konfiguration, Signaturen, Quarantäne, Scanverlauf). Benchmark-Ergebnisse liegen weiter in `history.db` (Tabelle `benchmark`, Kennzahlen als JSON).

## v1.3-Bausteine

| Baustein | Aufgabe |
|---|---|
| `UltimateX.Core/NetSec` | Reine Logik für Netzwerk und Sicherheit: `NetworkTools` (Traceroute-Auswertung, Portlisten max. 20, feste `DnsPresets` Cloudflare/Quad9/Google/DHCP, Wake-on-LAN-Paket und Subnetz-Broadcast), `SecurityAudit` (Befunde, SECURITY SCORE, `WindowsSupport`-Tabelle Stand Oktober 2026), `ScheduledTaskLogic` (Markierungen, Base64url-Aufgabentoken), `PrivacyCatalog` (feste Tabelle der sechs Datenschutz-Einstellungen, Sicherung des Ursprungszustands). |
| `UltimateX.Core/Backup` | `BackupModels` (Profile, Manifest, Verlauf), `BackupPaths` (Speicherpfade, Versionsnamen, Ordnernamen), `BackupTemplates` (Vorlagen inkl. Lesezeichen-Dateien), `ExclusionMatcher`, `BackupPlanner` (Link-/Kopierentscheidung, Aufbewahrung, Zeitpläne inkl. Nachholen, Überfällig-Prüfung, Prüf-Stichprobe), `RestoreConflicts`, `SystemImageTargetCheck`, `WbadminProgressParser` (DE/EN). |
| `UltimateX.Core/Power` | `PowerSchedule`/`PowerScheduleMath`: „in …“/„um …“, max. 7 Tage, Sommer-/Winterzeit, Warnzeitpunkte, Verschieben, `shutdown`-Sekunden. |
| `UltimateX.Core/Lifetime` | `RuntimeCalculator`: Laufzeit und Starts aus Ereignisprotokoll und eigener Mitschrift (Heartbeats, überlappende Sitzungen). |
| `UltimateX.Core/SpaceAnalysis` | `SpaceScanner` (Ordnersummen ohne Reparse-Points, abbrechbar), `DuplicateFinder` (Größe → 64 KB → SHA-256), `SpaceDeletionGuard` (nur reguläre Dateien außerhalb geschützter Wurzeln, Papierkorb). |
| `UltimateX.Core/Timeline` | `TimelineModels`/`TimelineRules`: Ereignisarten, Deduplizierung von Installationen, Ursachen-Hinweis (erster Absturz binnen 48 h nach Treiber/Update). |
| `UltimateX.Core/Settings` | `OverlaySettings` (Werte, Position, Profile, Ping-Ziel), `BackgroundMonitorSettings` (Regeln, Intervalle), `HotkeyGesture` (Tastenkürzel prüfen), `JsonSettingsFile` (Größenlimit, atomar schreiben, Rückfall auf Standardwerte). |
| `UltimateX.Core/Analysis` (erweitert) | `SteamRequirements` (Steam-Antworten → Anforderungen, nur ausdrücklich genannte Werte), `FrameStats` (FPS/Frametime/1 % Low), `AlertRules` (Regeln der Infobereich-Überwachung), `OverlayData` (Zeilen, Sitzungs-CSV), `ManualSpeedtestEntry`, `BenchmarkAnalysis`/`PerformanceScore` (Kalibrierung, Ursachen je Komponente, Systemlaufwerk), Geekbench-Eingaben. |
| `UltimateX.Core/Security` (erweitert) | `CommandCatalog.Backup/Network/System/Tools.cs` (neue Allowlist-Einträge mit Token-Parametern, `IsReparseFreeChain`), `EmbeddedBrowserPolicy` (nur `https://*.speedtest.ch`). |
| `Services/Backup` | `BackupService` (Profile, Verlauf, Zeitplaner, Überfällig-Liste für den Infobereich), `FileBackupEngine` (Versionen mit Hardlinks, Prüfung, Aufbewahrung, Wiederherstellung), `SystemImageService` (wbadmin, VHDX), `DriverBackupService` (pnputil), `BackupNative` (Hardlinks). |
| `Services/NetSec` | `NetworkToolsService` (Traceroute/Port-Prüfung über .NET, DNS/Reset über den Helper), `SecurityAuditService` (nur lesend, SSDP im lokalen Netz), `ScheduledTaskService`, `PrivacyService`. |
| `Services/Insights` | `TimelineService`, `LifetimeService`, `EventLogReaderHelper` – lokale Auswertung der Ereignisprotokolle ohne Adminrechte. |
| `Services/Gaming` | `GameRequirementsOnlineService` (Steam-Shop, `games-user.json`), `FpsMonitorService` (ETW-Sitzung `UltimateX-FPS`, Vordergrundprozess), `InstalledGamesService`. |
| `Services/System`, `Services/Monitoring` (erweitert) | `ScheduledPowerService` + `PowerScheduleStore`, `UptimeRecorder` (eigene Laufzeit-Mitschrift), `BackgroundCheckService` (Laufwerke, SMART, Bluescreens, Defender – nur lesend). |
| `App/Infrastructure/TrayMonitor` | Symbol im Infobereich (`NotifyIcon`), Menü, Regelauswertung im Prüfintervall, Benachrichtigungen mit Wiederholungssperre, „in den Infobereich minimieren“. |
| `App/Infrastructure/OverlayController` | Overlay-Fenster (durchklickbar, nie aktiviert), globales Tastenkürzel (`RegisterHotKey`), Messwerte aus MonitorHub/SensorManager/FPS, Ping, Sitzungsaufzeichnung. |
| `App/Controls/SpeedtestChBrowser`, `App/Infrastructure/FolderDropBehavior` | abgeschottete WebView2-Ansicht für speedtest.ch; Drag & Drop von Ordnern ins Backup-Profil. |
| `App/Views/BackupViews.xaml`, `NetSecViews.xaml`, `SystemViews.xaml`, `OverlayViews.xaml`, `OverlayWindow.xaml` | DataTemplates der neuen Seiten bzw. das Overlay-Fenster. |

Neue Ablageorte (v1.3):

| Daten | Ort | Format |
|---|---|---|
| Online geladene Spiele | `%LOCALAPPDATA%\UltimateX\games-user.json` | JSON, atomar, beschädigt → `.broken` |
| Backup-Profile / -Verlauf | `%LOCALAPPDATA%\UltimateX\backup-profiles.json`, `backup-history.json` | JSON |
| Datei-Backup-Versionen | `<Ziel>\UltimateX-Backup\<Profil>\<yyyy-MM-dd_HHmmss>\…` mit `ultimatex-manifest.json`; Zielkennung `<Ziel>\UltimateX-Backup\target.json` | Dateien/Hardlinks + JSON-Manifest |
| Treiber-Sicherung | `<Laufwerk>:\UltimateX-Backup\Treiber\<yyyyMMdd-HHmmss>\` | pnputil-Export |
| System-Abbild | `<Laufwerk>:\WindowsImageBackup\<PC>\` | von Windows (wbadmin) verwaltet |
| Datenschutz-Ursprungswerte | `%LOCALAPPDATA%\UltimateX\privacy-backup.json` | JSON |
| Geplante Energieaktion | `%LOCALAPPDATA%\UltimateX\power-schedule.json` | JSON, höchstens eine Planung |
| Eigene Laufzeit-Mitschrift | `%LOCALAPPDATA%\UltimateX\uptime-log.json` | JSON (Start/Stop/Heartbeat alle 5 min) |
| Overlay / Infobereich | `%LOCALAPPDATA%\UltimateX\overlay.json`, `background-monitor.json` | JSON, max. 256 KB, Werte begrenzt |
| WebView2-Profil | `%LOCALAPPDATA%\UltimateX\WebView2\` | getrennt vom Edge-Profil |
| Spiel-Sitzungen | `Dokumente\ULTIMATE X\Sessions\*.csv` | CSV („;“, Punkt als Dezimaltrenner), nie automatisch gelöscht |
| Geekbench-Ergebnisse | `history.db` (Benchmark-Verlauf) | SQLite |

## Erweiterbarkeit (§103)

Neue Module = neuer Service in `UltimateX.Services` + `PageViewModel` + `DataTemplate` in `Views/`. Navigation, Suche (`Keywords`), Busy-/Fehlerbehandlung und Layout erbt jede Seite automatisch. Neue Systembefehle erfordern einen Eintrag in `CommandCatalog` (Code-Review + Threat-Model-Update).
