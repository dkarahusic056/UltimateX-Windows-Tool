# Umsetzungsstand

Referenz: Abschnittsnummern des Master-Prompts. ✅ = umgesetzt, ◐ = umgesetzt mit dokumentierter Einschränkung, ✖ = bewusst nicht umgesetzt.

| Bereich | § | Status | Hinweis |
|---|---|---|---|
| Produkt, Zielsysteme, Erkennung von Edition/Build/Rechten/Hardware | 1, 2 | ✅ | |
| MSI-Installer (Pfad wählbar, Startmenü, optionale Desktop-Verknüpfung, Upgrade, Deinstallation), Signierung | 3, 63 | ✅ | WiX v5, `build.ps1` |
| Design: Dark/Light/System, Statusfarben (v1.1: Futuristic Gaming Control Center, siehe unten) | 4 | ✅ | |
| Layouts: Modern Control Center, Gaming, Technician, Visual PC Map, Simple, Admin/Service + Layout-Switcher | 5–11 | ✅ | |
| Dashboard, Quick Actions, Start-Dashboard | 12, 13, 100 | ✅ | |
| Desktop Organizer inkl. Regeln, Backup, Wiederherstellen | 14 | ✅ | Shell-API `IFolderView`; „Automatisch anordnen“ muss aus sein |
| Papierkorb | 15 | ✅ | |
| Systeminformationen, Mainboard-Ansicht, Topologie | 16–18 | ◐ | Belegte PCIe/M.2-Slots sind per Windows-API nicht zuverlässig ermittelbar → nicht erfunden |
| Live Monitor (CPU, GPU, RAM, Disk, Netz, 60 s/5/15 min) | 19 | ✅ | |
| Temperature Center / Sensor-Center | 20 | ✅ | GPU-Temperatur/Lüfter für AMD, Intel, NVIDIA über den Windows-Grafikkernel (D3DKMT, wie Task-Manager); ACPI-Thermalzonen; SSD/NVMe über SMART; echte CPU-Kerntemperaturen automatisch aus HWiNFO (Shared Memory) oder LibreHardwareMonitor (WMI), falls der Benutzer eines davon nutzt. Kein eigener Kernel-Treiber (T-24). |
| Multi-Monitor (Auflösung, Hz, HDR, Skalierung, Position, Hauptmonitor, GPU) | 21 | ✅ | |
| Storage-Visualisierung | 22 | ✅ | |
| Speedtest inkl. Tacho, Historie (SQLite), 7/30-Tage-Vergleich | 23 | ✅ | |
| Network Center, MAC, WLAN, Diagnose, Ping, DNS-Test | 24–29 | ✅ | WLAN-Details unter Win 11 24H2+ nur mit Standortfreigabe |
| Driver Manager + Updater + Security | 30–32 | ✅ | Ausschließlich Windows Update; Rollback über Geräte-Manager |
| Software Update Manager (winget), Programm-Manager | 33, 34 | ✅ | Deinstallation über Windows „Apps & Features“ |
| Startup Manager (HKCU direkt, HKLM per UAC), Backup/Restore | 35, 85 | ✅ | |
| Power Control, Restart Control | 36, 37 | ✅ | Neustart verschieben über Windows-Nutzungszeit |
| CMD/PowerShell-Tools | 38 | ✅ | |
| Health & Repair: SFC, DISM, Workflow, CHKDSK, SMART, Dienste, Event Logs, BSOD, Memory Diagnostic | 39–48 | ✅ | |
| Complete PC Check, PC Health Score + Verlauf, Windows Health | 49–51, 103 | ✅ | |
| Storage Cleaner | 52 | ✅ | |
| Security Center, Secure by Design, Least Privilege, Auth (Windows Hello), Sensitive Actions, Session, Maskierung, LOCK | 53–61 | ✅ | |
| Application Integrity, Secure Updates, Netzwerk-/Download-Sicherheit | 62–66 | ✅ | App-Update aktiv, sobald der Herausgeber eine Manifest-URL hinterlegt |
| Command-Injection-/Path-Traversal-Schutz, Safe File Ops, Secrets, SQLite-Sicherheit | 67–71 | ✅ | |
| Defender, Firewall, Secure Boot, TPM, BitLocker | 72–76 | ✅ | nur lesend |
| Prozesse mit Signatur, File Signature Checker, Hash Tool | 77–79 | ✅ | |
| Security Log, Logging, System Report | 80–82 | ✅ | |
| Toast-Benachrichtigungen | 83 | ✅ | benötigt die Startmenü-Verknüpfung des MSI |
| Settings inkl. Sprache Deutsch/Englisch/Automatisch | 84 | ✅ | Laufzeit-Übersetzung, siehe `docs/LOCALIZATION.md` |
| Restore Points | 86 | ✅ | |
| Command Output Viewer, CMD-Allowlist | 87, 88 | ✅ | |
| Performance, Fehlerbehandlung | 89, 90 | ✅ | |
| Threat Model, Dependency Security, SBOM, Static Analysis, CI/CD | 91–95 | ✅ | |
| Technologie, Architektur, MVVM | 96–98 | ✅ | |
| First Start | 99 | ✅ | |
| PC Map, Globale Suche | 101, 102 | ✅ | |
| Future Modules: Gaming Mode, Speed History, BIOS Details, Health History, Fan Info (NVIDIA), Battery Health, System Restore, Troubleshooting-Links | 103 | ✅ | Dazu: Benchmarks (CPU/RAM/Datenträger + Verlauf), Netzwerk-Scanner (Heimnetz, Hersteller, neue Geräte, Dienste-Check), Absturzanalyse (Bluescreens, WHEA, TDR, App-Abstürze, Minidump-Köpfe, optional kd.exe) |
| Tests, Security-Tests, Datenschutz, keine falschen Versprechen | 104–107 | ✅ | |

## v1.1 – Erweiterung

| Bereich | Phase | Status | Hinweis |
|---|---|---|---|
| Design-System „Futuristic Gaming Control Center“ (Schwarz/Graphit/Rot, abgeschrägte Karten, HUD-Leiste, Ring-Gauges mit Skala, Glow, animierte Seitenwechsel) | 1 | ✅ | `ChamferBorder`, `Themes/Dark.xaml`, `Themes/Controls.xaml`; Light-Theme mit gleicher Struktur |
| Dashboard: SYSTEM HEALTH, CPU, GPU, RAM, VRAM, STORAGE, NETWORK, SECURITY, WINDOWS HEALTH, TEMPERATURES, LIVE PERFORMANCE; Aktionen RUN PC CHECK, TEST COMPONENTS, NETWORK TEST, EXPORT REPORT, GAME MODE, SECURITY CHECK | 1 | ✅ | |
| HWiNFO-Integration (Shared Memory), LibreHardwareMonitor (WMI), Windows nativ, ACPI | 2 | ✅ | Status inkl. „läuft, aber Shared Memory aus“ |
| Seite SENSOR SOURCES, automatisches Failover HWiNFO → LHM → Windows → ACPI, einheitliches Sensormodell (CPU_PACKAGE_TEMP …), Min/Ø/Max/Quelle/Stand | 2 | ✅ | `SensorManager` |
| Netzwerk-Scanner: PING, LIVE PING, DETAILS, SERVICES, COPY IP, COPY MAC je Gerät; Auswertung (Status, Latenz, Verlust, Min/Ø/Max, Jitter); Modi 1×/4×/10×/Dauer (abbrechbar) mit Live-Diagramm; QUICK PING (IP/Hostname/lokaler Name) | 3 | ✅ | nur .NET-Ping, keine Shell |
| SYSTEM COMPONENT TEST CENTER: 42 Tests, PASS/WARNING/FAIL/NOT AVAILABLE/NOT TESTED/USER ACTION REQUIRED, interaktive Tests (Testton, Mikrofonpegel, Kamera, Farbtest, Tastatur/Maus, Testseite, Speicherdiagnose), optionaler Schreibtest, Verlauf mit Vergleich, Export TXT/JSON | 4 | ✅ | GPU-Render-Test nicht enthalten (siehe Einschränkungen) |
| PC-Bericht: EXPORT PC REPORT (echtes PDF), PRINT PC REPORT (Windows-Druckdialog), Vorschau mit Abschnittsauswahl, Datenschutzmodus standardmäßig an | 5 | ✅ | eigener PDF-Writer ohne Fremdbibliothek |
| Named-Pipe-IPC App ↔ Admin-Helper (ACL, Peer-Prüfung, Nonce, Replay-Schutz, Limits, Schema) | Security | ✅ | ersetzt die Ausgabedateien in `%ProgramData%` |
| Tests, Security-Tests, Doku, Release-Build 1.1.0 | 6, 7 | ✅ | |

## Offene Punkte

- **Signierung:** Für Release-Builds ein Code-Signing-Zertifikat hinterlegen (`build.ps1 -CertThumbprint …`). Unsignierte Entwicklungs-Builds laufen, App-Updates werden dann aber bewusst nicht installiert.
- **CPU-Temperatur ohne Zusatzprogramm:** Windows stellt Kerntemperaturen nur über einen Kernel-Treiber bereit. ULTIMATE X lädt bewusst keinen (Sicherheitsrisiko, vgl. WinRing0-Schwachstelle) und übernimmt stattdessen die Werte von HWiNFO/LibreHardwareMonitor.
- **GPU-Render-Test:** Ein Belastungs-/Rendertest mit eigener 3D-Szene ist nicht enthalten; der GPU-Test prüft Treiber, WDDM, DirectX-Laufzeit, TDR-Ereignisse und Sensorwerte.
- **WinRE ohne Adminrechte:** Der Status wird aus `ReAgent.xml` gelesen; ist die Datei nicht lesbar, meldet der Test NOT AVAILABLE.
- **Weitere Sprachen:** Mechanismus ist sprachneutral; neue Sprachen = neues Wörterbuch (`tools/localization`).
