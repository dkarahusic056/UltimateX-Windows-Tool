# CLAUDE_PROGRESS – ULTIMATE X v1.3.1

## Completed (v1.3)
- **Analyse:** Bestandsaufnahme vorhanden/teilweise/neu – siehe [ROADMAP.md](ROADMAP.md#v13--erweiterung).
- **Spiele:** Can My PC Run It lädt Anforderungen online aus dem Steam-Shop (ohne Konto) und speichert sie lokal (`games-user.json`); fehlende Anforderungen installierter Spiele nachladen.
- **Benchmarks:** Score-Kalibrierung, Ursachen je Komponente, Systemlaufwerk-Wertung, NVMe-Erwartung je Generation, Geekbench-Karte.
- **Backup-Center:** System-Abbild (wbadmin) inkl. Durchsuchen und Anleitung zum Zurückspielen, Datei-Backup-Profile mit Versionen/Hardlinks, Prüfung, Aufbewahrung, Wiederherstellung, Zeitplan, Treiber-Sicherung (pnputil), Rettungs-USB-Stick.
- **Netzwerk & Sicherheit:** Netzwerk-Werkzeuge (Traceroute, Port-Prüfung, DNS, Reset, Wake-on-LAN), Sicherheits-Check, Geplante Aufgaben, Datenschutz.
- **System:** zeitgesteuertes Herunterfahren/Neustart/Sperren/Abmelden/Energiesparen, PC Lifetime, Speicherplatz-Analyse, Zeitleiste.
- **Gaming/Überwachung:** In-Game-Overlay mit FPS über ETW (ohne Injection), Sitzungsaufzeichnung, Überwachung im Infobereich, speedtest.ch eingebettet (WebView2).
- Neue Abhängigkeiten: `Microsoft.Diagnostics.Tracing.TraceEvent` 3.1.13 (MIT), `Microsoft.Web.WebView2` 1.0.2903.40 (BSD-3-Clause).
- **Design:** neues Logo/Icon, Hintergrundbild im Hauptfenster, Installer-Grafiken (WixDialog.bmp/WixBanner.bmp).
- **Handbuch-PDF** (DE/EN) im MSI unter `Docs\`, Button in den Einstellungen, Startmenü-Verknüpfung.
- **1.3.1:** NVIDIA-Grafiktreiber automatisch (New PC Setup, Karte GRAFIKTREIBER), neue Programme (OneDrive, WinRAR, HWiNFO, Intel DSA u. a.), Office über der Programmliste, Overlay-Flackern behoben.
- **Doku:** Benutzerhandbuch [HANDBUCH.md](HANDBUCH.md) (DE) und [MANUAL_EN.md](MANUAL_EN.md) (EN), [RELEASE_NOTES_1.3.0.md](RELEASE_NOTES_1.3.0.md), README, DEPENDENCIES, THREAT_MODEL (T-42 bis T-58), TESTING, ARCHITECTURE (v1.3-Bausteine), ROADMAP.

## Next Step
- MSI 1.3.0 installieren und die neuen Seiten prüfen (Deutsch/Englisch, Dark/Light), besonders Backup-Center mit externer Platte, Overlay im Spiel, Infobereich und speedtest.ch.
- Danach: `ScoreReferences` mit weiteren echten Messwerten nachjustieren.

## Build Status
- Branch `feature/v1.3`: Debug- und Release-Build auf Windows fehlerfrei; Smoke-Test: App startet, alle 47 Seiten geöffnet, keine neuen Fehler.

## Test Status
- xUnit: 830+ bestanden (v1.2: 316) – neue Testklassen in `tests/UltimateX.Core.Tests/V13*.cs`, siehe [TESTING.md](TESTING.md).

## Known Issues
- Datei-Backup ohne Schattenkopien (geöffnete Dateien werden übersprungen); Zeitpläne nur bei laufender App.
- FPS-Messung benötigt Adminrechte oder die Gruppe „Leistungsprotokollbenutzer“; kein Overlay über exklusivem Vollbild.
- Schätzungen (Leistungsklassen, FPS, Performance-Score-Skala) weiterhin ESTIMATED.
- CPU-Kerntemperaturen nur mit HWiNFO/LibreHardwareMonitor (kein eigener Kernel-Treiber – bewusst).
- Build ist unsigniert, solange kein Code-Signing-Zertifikat hinterlegt ist.
- Logo/Hintergrund zeigen ein Fenster-Symbol und „WINDOWS X“ – vor öffentlicher Verteilung markenrechtlich prüfen (Microsoft-Marken).

## Last Successful MSI
- `artifacts/release/UltimateX-WindowsControl-1.3.1-x64.msi` → abgelegt als `_Installer\UltimateX\UltimateX-Setup-1.3.1.msi` und `release\v1.3.1\`.
