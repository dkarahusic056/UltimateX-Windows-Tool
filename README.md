# ULTIMATE X Windows Control

**Your Complete Windows Control Center** – Hardware-Monitor, PC-Manager, Windows-Diagnose & -Reparatur, Network Center, Speedtest, Treiber-/Programm-Inventur, Autostart-Manager und Security Center in einer modernen Windows-App (WPF, .NET 8).

> Umsetzungsstand je Abschnitt des Lastenhefts: [docs/ROADMAP.md](docs/ROADMAP.md)

## Neu in Version 1.1

- **Futuristic Gaming Control Center**: neues Schwarz/Rot-Design mit abgeschrägten Karten, HUD-Statusleiste, Ring-Gauges mit Skala und Glow
- **Dashboard** mit SYSTEM HEALTH, CPU/GPU/RAM/VRAM/STORAGE/NETWORK-Ringen, Temperaturen, Live Performance und den Aktionen RUN PC CHECK · TEST COMPONENTS · NETWORK TEST · EXPORT REPORT · GAME MODE · SECURITY CHECK
- **Sensorquellen**: HWiNFO, LibreHardwareMonitor, Windows nativ, ACPI – mit Status, automatischem Failover und einheitlichem Sensormodell (Current/Min/Ø/Max/Quelle)
- **Netzwerk-Scanner** mit PING, LIVE PING (Live-Diagramm), DETAILS, SERVICES, COPY IP/MAC und QUICK PING für IP, Hostname oder lokalen Namen
- **System Component Test Center**: 42 sichere Tests mit PASS/WARNING/FAIL/NOT AVAILABLE/USER ACTION REQUIRED, interaktive Tests, Verlauf und Export
- **PC-Bericht als echtes PDF** und Druck über den Windows-Druckdialog, mit Vorschau, Abschnittsauswahl und Datenschutzmodus
- **Abgesicherte Named-Pipe-Verbindung** zwischen App und Admin-Helper

## Features

- **6 Layouts**: Modern Control Center (Standard), Gaming Mode, Technician Mode, Visual PC Map, Simple Mode, Admin/Service Mode – umschaltbar unter Einstellungen
- **Dashboard**: PC-Status, Ring-Anzeigen CPU/GPU/RAM/Speicher, Temperaturen, Internet-Tachos, Security, Windows Health, Updates, Live-Performance-Chart, Schnellzugriff, PC Health Score mit Verlauf, Complete PC Check, Systembericht (TXT/JSON)
- **Hardware**: Windows, CPU, GPU (exakter VRAM), RAM-Module, Mainboard/BIOS/UEFI/TPM/Secure Boot, Monitore (Auflösung, Hz, HDR, Skalierung, GPU), Datenträger inkl. SMART (Temperatur, Betriebsstunden, Lebensdauer, Fehler), Akku-Gesundheit, Hardware-Topologie
- **Live Monitor & Sensor-Center**: CPU, GPU, RAM, VRAM, Datenträger, Download/Upload – 60 s / 5 min / 15 min; GPU-Temperatur/Lüfter für AMD, Intel und NVIDIA (Windows-Grafikkernel), ACPI-Thermalzonen, echte CPU-Kerntemperaturen automatisch aus HWiNFO oder LibreHardwareMonitor
- **Netzwerk**: Adapter, IPv4/IPv6, Gateway, DNS, DHCP, MAC (maskiert, kopierbar), WLAN (SSID, Signal, Band, Kanal, Wi-Fi-Standard, Raten, Sicherheit), Diagnose, Ping, DNS-Test je Server, öffentliche IP
- **Speedtest**: Download/Upload/Ping/Jitter/Paketverlust, Tacho, Verlauf (SQLite), 7/30-Tage-Vergleich
- **Updates**: Treiber- und Windows-Updates über Windows Update (signiert, mit Restore Point), Software-Updates über winget, sichere App-Updates
- **Treiber** (mit „neue Version“ aus Windows Update), **Programme**, **Prozesse** (Pfad, Herausgeber, Signatur), **Dienste**, **Windows Logs**
- **Autostart**: aktivieren/deaktivieren wie im Task-Manager (alle Benutzer per UAC), nie löschen, Backup + Wiederherstellung
- **Speicher bereinigen**: Temp, Windows-Temp, Miniaturansichten, Fehlerberichte, Shader-Cache, Papierkorb – mit Vorschau
- **Desktop Organizer**: Symbole nach Typ gruppieren, A–Z/Z–A, links/rechts, eigene Regeln, Layout-Backup und Wiederherstellung (nur Positionen, keine Dateioperationen)
- **Windows Health & Repair**: SFC, DISM (Check/Scan/Restore), Reparatur-Workflow, CHKDSK, Boot-Prüfung, Speicherdiagnose, Wiederherstellungspunkte, Dienste, Event-Log-Analyse, Bluescreens
- **Security Center**: Defender/Drittanbieter-AV, Firewall, Secure Boot, TPM, BitLocker, UAC, App-Integrität, Datei-Signatur- und Hash-Prüfung
- **Benchmarks**: CPU (Single/Multi-Core, Ganzzahl/Gleitkomma/Krypto), RAM (Bandbreite, Latenz), Datenträger (sequentiell, 4K QD1, am Cache vorbei) – mit Verlauf und Vergleich zum letzten Lauf
- **Netzwerk-Scanner**: alle Geräte im eigenen Heimnetz (IP, MAC, Hersteller per IEEE-Liste, Name, Gerätetyp), neue Geräte werden gemeldet, eigene Bezeichnungen, Dienste-Check mit Sicherheitshinweisen
- **Absturzanalyse**: Bluescreens mit Klartext-Erklärung und Empfehlung, Hardwarefehler (WHEA), Grafiktreiber-Resets, wiederholte Programmabstürze, Minidump-Auswertung, optional Ursache per Microsoft-Debugger
- **Zweisprachig**: Deutsch und Englisch (automatisch nach Windows-Sprache oder manuell)
- **Tools & Power**: CMD/PowerShell (auch als Admin über UAC), Windows-Werkzeuge, Herunterfahren/Neustart mit Countdown, Sperren, Abmelden
- **Komponententest** (v1.1): CPU, RAM, GPU, VRAM, NVMe/SSD/HDD, Mainboard, BIOS/UEFI, TPM, Secure Boot, Ethernet, Wi-Fi, DNS, Gateway, Internet, Bluetooth, USB, Audio, Mikrofon, Lautsprecher, Kamera, Monitore, Tastatur, Maus, Akku, Sensoren, Lüfter, Dienste, Windows Update, Defender, Firewall, Ereignisprotokoll, WMI, Windows Search, Drucker, Speichercontroller, GPU-/Chipsatztreiber, Systemzeit, WinRE
- **PC-Bericht** (v1.1): PDF-Export und Druck mit Branding, Übersicht, Health Score, Hardware-Karten, Tabellen, Statusanzeigen, Warnungen und Diagrammen
- Windows-Benachrichtigungen, globale Suche (Strg+K), LOCK-Button, Dark/Light/System

## Systemanforderungen

- Windows 10 64-Bit (ab 1809) oder Windows 11 64-Bit
- Keine separate .NET-Installation nötig (self-contained)

## Installation

`UltimateX-WindowsControl-<Version>-x64.msi` ausführen. Standardpfad: `C:\Program Files\ULTIMATE X Windows Control\`. Desktop-Verknüpfung optional, Upgrades werden erkannt, Deinstallation über Windows-Einstellungen.

## Build

Voraussetzungen: Windows 10/11, .NET SDK 8.0.400 oder neuer (getestet mit .NET 10 SDK), optional Visual Studio 2022/2026.

```powershell
# Entwickeln
dotnet build UltimateX.sln -p:Platform=x64
dotnet run --project src\UltimateX.App

# Komplettes Release (Tests → Schwachstellen-Check → Publish → MSI → SHA-256)
.\build.ps1 -Version 1.1.0
# mit Authenticode-Signatur
.\build.ps1 -Version 1.1.0 -CertThumbprint <THUMBPRINT>
```

Nach dem ersten `dotnet restore` die erzeugten `packages.lock.json` einchecken – CI stellt dann im Locked Mode wieder her.

## Architektur

Siehe [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Kurz: `App` (WPF/MVVM, ohne Adminrechte) → `Services` (alle Systemzugriffe) → `Core` (Allowlist, Pfadschutz, Signaturen, Logging). Adminaktionen laufen nur über `UltimateX.AdminHelper.exe` mit UAC und fester Befehlsliste.

## Security Model

- Least Privilege, UAC wird nie umgangen
- Feste Befehls-Allowlist, keine Shell, keine freien Kommandos
- Windows Hello für sensible Aktionen, Security Session, LOCK
- Sensible Daten standardmäßig maskiert und nicht exportiert
- Authenticode-Prüfung der eigenen Dateien beim Start
- Nur HTTPS, keine Telemetrie, Netzwerkzugriffe nur auf Benutzeraktion

- Named-Pipe-IPC zum Admin-Helper mit ACL, Peer-Prüfung, Nonce und Replay-Schutz
- PC-Bericht mit Datenschutzmodus (Standard: an)

Details: [docs/SECURITY.md](docs/SECURITY.md) · [docs/THREAT_MODEL.md](docs/THREAT_MODEL.md)

## Dependencies & Lizenzen

[docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) – ausschließlich Microsoft-/.NET-Foundation-Pakete (MIT), WiX (MS-RL, nur Build).

## Known Limitations

- **CPU-Kerntemperaturen** erfordern einen Kernel-Treiber; ULTIMATE X lädt bewusst keinen (WinRing0 ist verwundbar, CVE-2020-14979) und übernimmt die Werte von HWiNFO/LibreHardwareMonitor, wenn eines davon läuft. Ohne diese zeigt es ACPI-Thermalzonen (klar gekennzeichnet).
- GPU-Temperatur setzt einen WDDM-2.4+-Treiber voraus, der die Temperatur meldet (aktuelle AMD/Intel/NVIDIA-Treiber).
- Die Minidump-Ursachenanalyse benötigt die „Debugging Tools for Windows“ (Windows SDK).
- WLAN-Details erfordern ab Windows 11 24H2 die Freigabe „Standortzugriff für Apps“.
- Der Desktop Organizer setzt voraus, dass „Symbole automatisch anordnen“ deaktiviert ist.
- Windows-Toasts erscheinen erst mit der Startmenü-Verknüpfung aus dem MSI (sonst In-App-Hinweis).
- App-Updates sind aktiv, sobald der Herausgeber eine HTTPS-Manifest-URL hinterlegt; unsignierte Builds aktualisieren sich nie selbst.
- Treiber-Rollback erfolgt über den Geräte-Manager („Vorheriger Treiber“).
- Katalogsignierte Windows-Dateien erscheinen als „Keine eingebettete Signatur“.
- Englische Texte werden zur Laufzeit übersetzt; einzelne seltene Meldungen können deutsch bleiben.
- Der GPU-Komponententest enthält keinen Render-/Belastungstest; Kamera- und Mikrofontests benötigen die Windows-Datenschutzfreigabe für Desktop-Apps.
- Der WinRE-Status ist ohne Adminrechte nur lesbar, wenn `ReAgent.xml` für Benutzer lesbar ist.

## Troubleshooting

Ausführlich: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md)

| Problem | Lösung |
|---|---|
| WLAN zeigt keine SSID | Einstellungen → Datenschutz → Standort → „Apps Zugriff auf Ihren Standort erlauben“ |
| Desktop Organizer meldet „automatisch anordnen“ | Rechtsklick auf Desktop → Ansicht → „Symbole automatisch anordnen“ deaktivieren |
| winget fehlt | „App-Installer“ aus dem Microsoft Store installieren |
| „Admin-Helper nicht gefunden“ | Neu installieren bzw. `UltimateX.AdminHelper.exe` muss neben `UltimateX.exe` liegen |
| SFC meldet „konnte den Vorgang nicht ausführen“ | Neustart (ausstehende Updates), dann erneut |
| DISM RestoreHealth Fehler 0x800f081f | Internetverbindung/Windows Update prüfen |
| Windows Hello erscheint nicht | Hello in den Windows-Einstellungen einrichten; ohne Hello wird explizit bestätigt |
| Einstellungen zurücksetzen | `%LOCALAPPDATA%\UltimateX\settings.json` löschen |
| Logs | `%LOCALAPPDATA%\UltimateX\logs` bzw. Seite „Logs“ |

## Release-Prozess

1. Version taggen (`v1.2.3`) → CI baut, testet, prüft Secrets und verwundbare Pakete.
2. Signierung auf dem Release-Runner (Zertifikat in HSM/Key Vault): EXE/DLL → MSI.
3. `signtool verify /pa` und SHA-256 veröffentlichen.
4. Release Notes inkl. aktualisiertem SBOM.

## Lizenz

MIT License – Copyright (c) 2026 Dennis Karahusic. Siehe [LICENSE](LICENSE).
