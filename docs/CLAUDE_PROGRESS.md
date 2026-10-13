# CLAUDE_PROGRESS – ULTIMATE X v1.1.0

## Completed
- **Phase 1 – Analyse, Security Review, Design System:** Red/Black-Theme (Dark/Light), `ChamferBorder`, neue Control-Styles, HUD-Statusleiste, Seitenübergänge, alle Karten auf abgeschrägtes Design umgestellt, Ring-Gauges mit Skala/Glow, Diagramme mit Fläche; neues Dashboard mit allen geforderten Bereichen und sechs zentralen Aktionen.
- **Phase 2 – Sensoren:** `SensorManager` (einheitliches Modell, Failover HWiNFO → LHM → Windows → ACPI, Min/Ø/Max, Quellenstatus inkl. INACTIVE), native Werte (CPU-Last/-Takt, GPU-Last, NVML), Seite *Sensorquellen*.
- **Phase 3 – Netzwerk:** `LivePingService`, Geräteaktionen im Scanner, Live-Ping mit `LatencyChart`, Quick Ping.
- **Phase 4 – Component Test Center:** 42 Tests, interaktive Tests (Ton, Mikrofon, Kamera, Farbtest, Tastatur/Maus, Testseite, Speicherdiagnose, Schreibtest), Verlauf/Vergleich, TXT/JSON-Export.
- **Phase 5 – PC-Bericht:** eigener PDF-Writer + Layout-Engine, WPF-Vorschau/Druck aus derselben Zeichenliste, Abschnittsauswahl, Datenschutzmodus (Standard an, Abschalten mit Windows Hello).
- **Security:** Named-Pipe-IPC App ↔ Admin-Helper (ACL, Squatting-Schutz, Peer-Prüfung, Identification-Level, Nonce, Sequenz, Limits, Schema, fail closed).
- **Phase 6 – Tests & Doku:** neue Testklassen, README, ROADMAP, ARCHITECTURE, THREAT_MODEL (T-28–T-34), SECURITY, DEPENDENCIES, TROUBLESHOOTING, TESTING, Release Notes; Übersetzungen (en.json) für alle neuen Texte.
- **Phase 7 – Release:** Version 1.1.0, `build.ps1` mit MSI-Prüfung (Eigenschaften, Dateitabelle, administrative Extraktion, keine Entwicklungsdateien, self-contained) und Release-Ordner (MSI, SHA256SUMS, Release Notes, Build-Log, Testergebnisse, SBOM).

## In Progress
- Visuelle Abnahme am Gerät (Screenshots) nach Installation der 1.1.0-MSI.

## Next Step
- Installer-Test durch den Benutzer: Installation/Upgrade, Startmenü, Start der App, Deinstallation.

## Build Status
- Letzter Build auf dem Windows-PC: erfolgreich (Release, x64, self-contained, MSI).

## Test Status
- xUnit: alle Tests bestanden (siehe `artifacts/release/TEST_RESULTS.txt`).

## Known Issues
- CPU-Kerntemperaturen nur mit HWiNFO/LibreHardwareMonitor (kein eigener Kernel-Treiber – bewusst).
- Kein GPU-Render-/Belastungstest.
- Build ist unsigniert (kein Code-Signing-Zertifikat vorhanden).

## Last Successful MSI
- `artifacts/release/UltimateX-WindowsControl-1.1.0-x64.msi` (SHA-256 in `SHA256SUMS.txt`)
