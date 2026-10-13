# ULTIMATE X Windows Control 1.1.0 – Release Notes

## Neu

### Futuristic Gaming Control Center (Design)
- Komplett neues Schwarz/Graphit/Rot-Design: abgeschrägte Karten (`ChamferBorder`) mit Akzentkante, roter Glow, HUD-Statusleiste mit Live-Werten (CPU/GPU/RAM/Temperaturen/Netz), Ring-Gauges mit Skala, Diagramme mit Verlaufsfläche, animierte Seitenwechsel, rote aktive Navigation, neue Tabs, Tabellen, Schalter und Tooltips.
- Neues Dashboard: SYSTEM HEALTH (Health-Score-Ring), CPU · GPU · RAM · VRAM · STORAGE · NETWORK, TEMPERATURES, LIVE PERFORMANCE, SECURITY, WINDOWS HEALTH, NETWORK, UPDATES und die zentralen Aktionen **RUN PC CHECK · TEST COMPONENTS · NETWORK TEST · EXPORT REPORT · GAME MODE · SECURITY CHECK**.

### Sensorquellen (HWiNFO · LibreHardwareMonitor · Windows · ACPI)
- Neue Seite **Sensorquellen** mit Status je Quelle (CONNECTED / AVAILABLE / INACTIVE / NOT AVAILABLE), Anzahl Sensoren, verwendete Sensoren, erkannte Hardware und letzte Aktualisierung.
- Automatisches Failover nach Priorität HWiNFO → LibreHardwareMonitor → Windows nativ → ACPI.
- Einheitliches Sensormodell (CPU_PACKAGE_TEMP, CPU_CORE_TEMP, CPU_CLOCK, CPU_POWER, GPU_CORE_TEMP, GPU_HOTSPOT_TEMP, GPU_MEMORY_TEMP, GPU_CLOCK, GPU_POWER, CPU_FAN, GPU_FAN, NVME_TEMP, …) mit Aktuell/Min/Ø/Max/Quelle/Stand und Verlaufsgrafik.
- Erkennt „HWiNFO läuft, aber Shared Memory ist aus“ und gibt eine konkrete Anleitung.

### Netzwerk
- Netzwerk-Scanner: je Gerät **PING, LIVE PING, DETAILS, SERVICES, COPY IP, COPY MAC**.
- Ping-Auswertung: Status (erreichbar / instabil / nicht erreichbar), Latenz, gesendet/empfangen, Verlust, Min/Ø/Max, Jitter.
- Live-Ping-Modi 1× / 4× / 10× / dauerhaft (jederzeit abbrechbar) mit Live-Latenzdiagramm (Verluste als eigene Balken).
- **QUICK PING** für IP-Adresse, Hostname oder lokalen DNS-Namen – ausschließlich über die .NET-Ping-API.

### System Component Test Center
- **TEST ALL SYSTEM COMPONENTS**: 42 sichere, nicht-destruktive Tests (CPU, RAM, GPU, VRAM, NVMe, SSD, HDD, Mainboard, BIOS/UEFI, TPM, Secure Boot, Ethernet, Wi-Fi, DNS, Gateway, Internet, Bluetooth, USB, Audio, Mikrofon, Lautsprecher, Kamera, Monitore, Tastatur, Maus, Akku, Sensoren, Lüfter, Dienste, Windows Update, Defender, Firewall, Sicherheitsfunktionen, Ereignisprotokoll, WMI/CIM, Windows Search, Drucker, Speichercontroller, GPU-Treiber, Chipsatztreiber, Systemzeit, WinRE).
- Status PASS / WARNING / FAIL / NOT AVAILABLE / NOT TESTED / USER ACTION REQUIRED mit Details.
- Interaktive Tests nur auf Klick: Testton links/rechts, Mikrofon-Live-Pegel (ohne Aufnahme), Kamera-Zugriffstest (ohne Bild), Bildschirm-Farbtest, Tastatur-/Maustest im eigenen Fenster, Windows-Testseite, Windows-Speicherdiagnose, optionaler Datenträger-Schreibtest mit eigener Temp-Datei.
- Testverlauf mit Vergleich zum vorherigen Lauf, Export als TXT/JSON.

### PC-Bericht
- **EXPORT PC REPORT** als echtes PDF (Vektorgrafik, A4) mit ULTIMATE-X-Branding, Übersicht, Health Score, Hardware-Karten, Tabellen, Statusanzeigen, Warnungen, Diagrammen, Berichtsdatum, Bericht-ID und App-Version.
- **PRINT PC REPORT** über den Windows-Druckdialog (Drucker oder „Microsoft Print to PDF“).
- Vorschau mit Abschnittsauswahl (Hardware, Sensoren, Netzwerk, Sicherheit, Treiber, Windows Health, Komponententests, Benchmarks, Absturzanalyse).
- **Datenschutzmodus standardmäßig an** (maskiert IP/MAC, Benutzer-/Computername, Seriennummern, Gateway/DNS); Abschalten nur mit Windows-Hello-Bestätigung.

## Sicherheit
- Neue Named-Pipe-Verbindung zwischen App und Admin-Helper: restriktive ACL, Pipe-Squatting-Schutz, Peer-Prüfung beider Seiten, Impersonation-Level *Identification*, Sitzungs-Nonce, Replay-Schutz, Größen- und Zeitlimits, Schema-Prüfung, fail closed. Der Helper schreibt keine Ausgabedateien mehr.
- Threat Model um T-28 bis T-34 erweitert, neues Dokument `docs/SECURITY.md`.
- Weiterhin: keine Kernel-Treiber, keine freie Shell, keine Änderungen an Defender/Firewall/UAC, keine Telemetrie.

## Tests
- Neue Testklassen: `SensorManagerTests`, `LivePingTests`, `ComponentTestLogicTests`, `PdfReportTests`, `HelperPipeProtocolTests`.

## Bekannte Einschränkungen
- Echte CPU-Kerntemperaturen benötigen HWiNFO (Shared Memory) oder LibreHardwareMonitor – ULTIMATE X lädt bewusst keinen Kernel-Treiber.
- Der GPU-Test enthält keinen Render-/Belastungstest.
- Kamera/Mikrofon benötigen die Windows-Datenschutzfreigabe für Desktop-Apps.
- WinRE-Status ohne Adminrechte nur, wenn `ReAgent.xml` lesbar ist.
- Unsignierter Build: Für die Verteilung mit einem Code-Signing-Zertifikat bauen (`build.ps1 -CertThumbprint …`).
