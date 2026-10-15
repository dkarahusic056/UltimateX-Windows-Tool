# ULTIMATE X Windows Control

**Your Complete Windows Control Center** – Hardware-Monitor, PC-Manager, Windows-Diagnose & -Reparatur, Backup-Center, Network Center, Speedtest, Treiber-/Programm-Inventur, Autostart-Manager, In-Game-Overlay und Security Center in einer modernen Windows-App (WPF, .NET 8).

> **Benutzerhandbuch:** [docs/HANDBUCH.md](docs/HANDBUCH.md) (Deutsch) · [docs/MANUAL_EN.md](docs/MANUAL_EN.md) (English)
> Umsetzungsstand je Abschnitt des Lastenhefts: [docs/ROADMAP.md](docs/ROADMAP.md)

## Neu in Version 1.3

- **Backup-Center**: System-Abbild mit Windows-Bordmitteln (wbadmin) inkl. Anleitung zum Zurückspielen, **Datei-Backup-Profile mit Versionen** (Hardlinks für unveränderte Dateien, Prüfung nach jeder Sicherung, Aufbewahrung), Wiederherstellen von Dateien/Ordnern, Zeitplan (täglich, wöchentlich, beim Anstecken der Platte), **Treiber-Sicherung**, **Rettungs-USB-Stick**
- **Can My PC Run It online**: Spiele-Anforderungen aus dem Steam-Shop ohne Konto laden und lokal speichern
- **Benchmarks**: neu kalibrierter Performance Score, Ursachen je Komponente, Wertung immer für das Systemlaufwerk, **Geekbench-Karte**
- **In-Game-Overlay** mit FPS, Frametime und 1 % Low über Windows-ETW – ohne Injection ins Spiel; Sitzungsaufzeichnung; **Überwachung im Infobereich** (Temperatur, Laufwerke, SMART, Bluescreens, Virenschutz, überfällige Backups)
- **Netzwerk-Werkzeuge** (Traceroute, Port-Prüfung, DNS, Netzwerk-Reset, Wake-on-LAN), **Sicherheits-Check** mit Security Score, **Geplante Aufgaben**, **Datenschutz**-Einstellungen
- **Zeitgesteuertes Herunterfahren/Neustart/Sperren/Abmelden/Energiesparen**, **PC Lifetime**, **Speicherplatz-Analyse**, **Zeitleiste „Was ist passiert?“**, **speedtest.ch eingebettet**
- Details: [docs/RELEASE_NOTES_1.3.1.md](docs/RELEASE_NOTES_1.3.1.md)

## Neu in Version 1.2

- **Benchmark Center**: CPU (7 Lasten, Dauerlast mit Throttling-Erkennung), GPU (Direct3D-11-Compute: GFLOPS, Speicherbandbreite, Stabilität), RAM, Datenträger, Netzwerk – und **START COMPLETE BENCHMARK** mit **ULTIMATE X PERFORMANCE SCORE** und Einstufung, **Expected vs Actual**, **Bottleneck Analyzer**
- **Memory Test**: RAM-Stabilitätstest Quick/Standard/Extended mit 8 Mustern, Boot Memory Test (Windows-Speicherdiagnose), Kanal-/Takt-/XMP-Prüfung
- **Storage**: Laufwerksgesundheit GOOD/WARNING/CRITICAL mit Begründung
- **Gaming Check**, **Can My PC Run It** (42 Spiele, FPS-Schätzung ESTIMATED, installierte Spiele, Websuche), **Upgrade Advisor**
- **New PC Setup**: Umgebungsprüfung, Profile, Empfehlungen, Installation nur aus offiziellen Quellen (winget/Store mit Domain- und SHA-256-Prüfung), Microsoft 365 über das Office Deployment Tool, Installationsprotokoll, **SET UP THIS PC**
- **Open Source Security**: ClamAV als optionaler Zusatz-Scanner – Microsoft Defender bleibt aktiv
- **Driver Health**, **BIOS/UEFI Center** (nur lesend), **Windows Health Score**, **System Snapshot** (BEFORE/AFTER), erweiterter Complete PC Check, PC Map und PDF-Bericht mit Bewertung
- Werte ohne Messquelle erscheinen als **N/A**, Schätzungen als **ESTIMATED** – Details: [docs/RELEASE_NOTES_1.3.1.md](docs/RELEASE_NOTES_1.3.1.md)

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
- **Speedtest**: Download/Upload/Ping/Jitter/Paketverlust, Tacho, Verlauf (SQLite), 7/30-Tage-Vergleich; v1.3: speedtest.ch eingebettet (WebView2, Ergebnis manuell übernehmen)
- **Netzwerk-Werkzeuge** (v1.3): Traceroute, Port-Prüfung (max. 20 Ports, kein Portscanner), DNS-Cache leeren, IP erneuern, DNS-Server Cloudflare/Quad9/Google/DHCP, Netzwerk zurücksetzen, Wake-on-LAN
- **Updates**: Treiber- und Windows-Updates über Windows Update (signiert, mit Restore Point), Software-Updates über winget, sichere App-Updates
- **Treiber** (mit „neue Version“ aus Windows Update), **Programme**, **Prozesse** (Pfad, Herausgeber, Signatur), **Dienste**, **Windows Logs**
- **Autostart**: aktivieren/deaktivieren wie im Task-Manager (alle Benutzer per UAC), nie löschen, Backup + Wiederherstellung
- **Geplante Aufgaben** (v1.3): Aufgabenplanung mit Programm, Signatur und Markierungen (unsigniert, Programm fehlt, Temp/AppData), aktivieren/deaktivieren per UAC – nie löschen
- **Speicher bereinigen**: Temp, Windows-Temp, Miniaturansichten, Fehlerberichte, Shader-Cache, Papierkorb – mit Vorschau
- **Speicherplatz-Analyse** (v1.3): größte Ordner/Dateien, Duplikate (SHA-256), Dateitypen; Löschen nur in den Papierkorb, Systemordner gesperrt
- **Backup-Center** (v1.3): System-Abbild (wbadmin), Datei-Backup mit Versionen und Hardlinks, Wiederherstellung, Zeitplan, Treiber-Sicherung (pnputil), Rettungs-USB-Stick
- **Desktop Organizer**: Symbole nach Typ gruppieren, A–Z/Z–A, links/rechts, eigene Regeln, Layout-Backup und Wiederherstellung (nur Positionen, keine Dateioperationen)
- **Windows Health & Repair**: SFC, DISM (Check/Scan/Restore), Reparatur-Workflow, CHKDSK, Boot-Prüfung, Speicherdiagnose, Wiederherstellungspunkte, Dienste, Event-Log-Analyse, Bluescreens
- **Security Center**: Defender/Drittanbieter-AV, Firewall, Secure Boot, TPM, BitLocker, UAC, App-Integrität, Datei-Signatur- und Hash-Prüfung
- **Benchmarks** (v1.2): CPU (Ganzzahl, Gleitkomma, Krypto, Kompression, Dekompression, AES, Rendering; Dauerlast), GPU (D3D11-Compute), RAM (Lesen/Schreiben/Kopieren/Latenz), Datenträger (sequentiell, 4K, IOPS, Zugriffszeit, am Cache vorbei), Netzwerk – Verlauf, Vergleich, Complete Benchmark mit Performance Score; v1.3: Kalibrierung, Ursachen je Komponente, Systemlaufwerk-Wertung, Geekbench-Ergebnisse
- **Netzwerk-Scanner**: alle Geräte im eigenen Heimnetz (IP, MAC, Hersteller per IEEE-Liste, Name, Gerätetyp), neue Geräte werden gemeldet, eigene Bezeichnungen, Dienste-Check mit Sicherheitshinweisen
- **Absturzanalyse**: Bluescreens mit Klartext-Erklärung und Empfehlung, Hardwarefehler (WHEA), Grafiktreiber-Resets, wiederholte Programmabstürze, Minidump-Auswertung, optional Ursache per Microsoft-Debugger
- **Zeitleiste** (v1.3): Updates, Installationen, Treiber, Abstürze, Starts der letzten 7/30/90 Tage mit Ursachen-Hinweis (Absturz binnen 48 h nach Treiber/Update)
- **PC Lifetime** (v1.3): Betriebsstunden, Einschaltvorgänge und Verschleiß je Laufwerk, Windows-Installationsdatum, Starts und Laufzeit laut Ereignisprotokoll
- **Sicherheits-Check** (v1.3): SMB1, Remotedesktop, Firewall, Administratoren, Konten, Gastkonto, UPnP, AutoPlay, PowerShell 2.0, Windows-Support – Behebung nur nach Bestätigung; **Datenschutz** (v1.3): sechs Windows-Datenschutzeinstellungen mit Rückgängig
- **In-Game-Overlay** (v1.3): FPS/Frametime/1 % Low über ETW (ohne Injection), Auslastung, Temperaturen, Takt, VRAM, Ping; Profile, Tastenkürzel, Sitzungsaufzeichnung (CSV); Überwachung im Infobereich
- **Zweisprachig**: Deutsch und Englisch (automatisch nach Windows-Sprache oder manuell)
- **Tools & Power**: CMD/PowerShell (auch als Admin über UAC), Windows-Werkzeuge, Herunterfahren/Neustart mit Countdown, Sperren, Abmelden; v1.3: zeitgesteuert (in … / um …, bis 7 Tage, Warnung 5 und 1 Minute vorher)
- **Komponententest** (v1.1): CPU, RAM, GPU, VRAM, NVMe/SSD/HDD, Mainboard, BIOS/UEFI, TPM, Secure Boot, Ethernet, Wi-Fi, DNS, Gateway, Internet, Bluetooth, USB, Audio, Mikrofon, Lautsprecher, Kamera, Monitore, Tastatur, Maus, Akku, Sensoren, Lüfter, Dienste, Windows Update, Defender, Firewall, Ereignisprotokoll, WMI, Windows Search, Drucker, Speichercontroller, GPU-/Chipsatztreiber, Systemzeit, WinRE
- **PC-Bericht** (v1.1): PDF-Export und Druck mit Branding, Übersicht, Health Score, Hardware-Karten, Tabellen, Statusanzeigen, Warnungen und Diagrammen
- Windows-Benachrichtigungen, globale Suche (Strg+K), LOCK-Button, Dark/Light/System, Symbol im Infobereich (v1.3)

## Systemanforderungen

- Windows 10 64-Bit (ab 1809) oder Windows 11 64-Bit
- Keine separate .NET-Installation nötig (self-contained)
- Optional: Microsoft Edge WebView2-Runtime (nur für speedtest.ch eingebettet; bei Windows 11 vorinstalliert)

## Installation

`UltimateX-WindowsControl-<Version>-x64.msi` ausführen. Standardpfad: `C:\Program Files\ULTIMATE X Windows Control\`. Desktop-Verknüpfung optional, Upgrades werden erkannt, Deinstallation über Windows-Einstellungen. Schritt für Schritt: [Handbuch, Kapitel 1](docs/HANDBUCH.md#1-einleitung).

## Build

Voraussetzungen: Windows 10/11, .NET SDK 8.0.400 oder neuer (getestet mit .NET 10 SDK), optional Visual Studio 2022/2026.

```powershell
# Entwickeln
dotnet build UltimateX.sln -p:Platform=x64
dotnet run --project src\UltimateX.App

# Komplettes Release (Tests → Schwachstellen-Check → Publish → MSI → SHA-256)
.\build.ps1 -Version 1.3.1
# mit Authenticode-Signatur
.\build.ps1 -Version 1.3.1 -CertThumbprint <THUMBPRINT>
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

[docs/DEPENDENCIES.md](docs/DEPENDENCIES.md) – ausschließlich Microsoft-/.NET-Foundation-Pakete (MIT; v1.3: `Microsoft.Diagnostics.Tracing.TraceEvent` 3.1.13 MIT, `Microsoft.Web.WebView2` 1.0.2903.40 BSD-3-Clause), SQLitePCLRaw (Apache-2.0), WiX (MS-RL, nur Build).

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
- **v1.2:** CPU-/GPU-Leistungsklassen, „Can My PC Run It“ und alle FPS-Angaben sind Schätzungen aus einer lokal mitgelieferten Datenbank (ESTIMATED); unbekannte oder mobile GPUs ergeben N/A. Die Skala des Performance Scores ist geschätzt.
- **v1.2:** Der In-App-RAM-Test prüft nur freien Speicher im laufenden Windows; der Boot Memory Test (Windows-Speicherdiagnose) prüft nahezu den gesamten RAM nach einem Neustart.
- **v1.2:** Resizable BAR ist ohne Herstellerwerkzeug nicht zuverlässig auslesbar (N/A). ClamAV hat keinen Echtzeitschutz.
- **v1.3 Backup:** keine Schattenkopien (geöffnete Dateien werden übersprungen und gemeldet); Zeitpläne laufen nur, solange ULTIMATE X läuft (minimiert/Infobereich genügt); keine Netzwerkfreigaben, keine Cloud, keine eigene Verschlüsselung; Hardlink-Versionen nur auf NTFS. System-Abbild (wbadmin): Ziel NTFS, je Platte und PC zuverlässig nur das neueste Abbild, Abbruch aus der App nicht möglich.
- **v1.3 Overlay/FPS:** kein Overlay über exklusivem Vollbild; FPS-Messung benötigt Adminrechte oder die Gruppe „Leistungsprotokollbenutzer“ (sonst N/A); OpenGL und manche Vulkan-Spiele liefern keine FPS.
- **v1.3 Online-Spieldaten:** nur im Steam-Shop gelistete Spiele, Steam begrenzt Abfragen (HTTP 429), keine Raytracing-/Upscaling-Angaben (N/A).
- **v1.3:** Zeitgesteuertes Sperren/Abmelden/Energiesparen nur bei laufender App (kein Nachholen), Planung höchstens 7 Tage. speedtest.ch benötigt die WebView2-Runtime; Werte werden von Hand übernommen. Port-Prüfung bewusst kein Portscanner (ein Ziel, max. 20 Ports). PC Lifetime ohne TBW-Werte (N/A).

## Internetzugriffe (nur auf Benutzeraktion, keine Telemetrie)

Vollständige Liste: [docs/DEPENDENCIES.md](docs/DEPENDENCIES.md#externe-dienste-nur-auf-benutzeraktion) und [Handbuch, Abschnitt 1.7](docs/HANDBUCH.md#17-datenschutz). Ausnahme ohne Klick: die abschaltbare Update-Suche beim Start (Einstellungen).

Neu in 1.3: `store.steampowered.com` (Spielname bzw. Steam-AppID, nur auf Knopfdruck), `www.speedtest.ch` (eingebettet, erst nach Umschalten), `www.geekbench.com/download/` und die WebView2-Downloadseite (nur Browser-Link), Traceroute/Port-Prüfung zu vom Benutzer genannten Zielen, Overlay-Ping zu `1.1.1.1` (nur wenn gewählt). Nur lokal: UPnP-SSDP-Multicast (Sicherheits-Check), Wake-on-LAN-Broadcast.

Neu in 1.2: winget-/Microsoft-Store-Quellen und Herstellerdownloads (New PC Setup), `officecdn.microsoft.com` (Office Deployment Tool), `api.github.com` (Projektprüfung ClamAV), ClamAV-Signaturserver (`database.clamav.net`, über freshclam), Websuche im Standardbrowser (`www.google.com`, nur Klick).

## Troubleshooting

Ausführlich: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) · Häufige Fragen v1.3: [Handbuch, Kapitel 10](docs/HANDBUCH.md#10-häufige-fragen-und-problembehandlung)

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
| speedtest.ch: „WebView2-Runtime ist nicht installiert“ | Runtime über den Knopf in der App von Microsoft installieren, App neu starten |
| Overlay zeigt „FPS: N/A“ | Konto zur Gruppe „Leistungsprotokollbenutzer“ hinzufügen, neu anmelden (Handbuch 6.2) |
| Overlay im Spiel unsichtbar | Spiel auf „Randloses Fenster“ statt exklusivem Vollbild stellen |
| Geplantes Backup lief nicht | Zeitpläne nur bei laufender App – „Beim Schließen in den Infobereich minimieren“ aktivieren |
| System-Abbild schlägt fehl | Ziel: externe NTFS-Platte, nicht C:, freier Platz ≥ belegter Platz + 10 %, UAC bestätigen |

## Release-Prozess

1. Version taggen (`v1.2.3`) → CI baut, testet, prüft Secrets und verwundbare Pakete.
2. Signierung auf dem Release-Runner (Zertifikat in HSM/Key Vault): EXE/DLL → MSI.
3. `signtool verify /pa` und SHA-256 veröffentlichen.
4. Release Notes inkl. aktualisiertem SBOM.

## Lizenz

MIT License – Copyright (c) 2026 Dennis Karahusic. Siehe [LICENSE](LICENSE).

Der Installer zeigt die Endbenutzer-Lizenzvereinbarung mit Haftungsausschluss an ([docs/EULA.md](docs/EULA.md), im MSI: `installer/License.rtf`); die Rechte am Quellcode richten sich nach der MIT-Lizenz.
