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
| `UltimateX.Services` | Kapselung aller Systemzugriffe | Core, System.Management, System.Diagnostics.EventLog/PerformanceCounter |
| `UltimateX.AdminHelper` | einziger erhöhter Prozess | Core |
| `UltimateX.App` | WPF-Oberfläche (MVVM) | Core, Services, CommunityToolkit.Mvvm |
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

## Erweiterbarkeit (§103)

Neue Module = neuer Service in `UltimateX.Services` + `PageViewModel` + `DataTemplate` in `Views/`. Navigation, Suche (`Keywords`), Busy-/Fehlerbehandlung und Layout erbt jede Seite automatisch. Neue Systembefehle erfordern einen Eintrag in `CommandCatalog` (Code-Review + Threat-Model-Update).
