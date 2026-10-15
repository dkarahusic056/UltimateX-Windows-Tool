# ULTIMATE X Windows Control 1.2.0 – Release Notes

Version 1.2 erweitert die bestehende App, ohne sie neu zu schreiben: neue Seiten, neue Services, gleiche Architektur (MVVM, `CommandCatalog`-Allowlist, Admin-Helper), gleiches Design (Dark/Light, Deutsch/Englisch).

**Grundsatz „keine Fantasiewerte“:** Was nicht gemessen werden kann, steht als **N/A** da. Schätzungen (Leistungsklassen, FPS, erwartete Werte) sind immer als **ESTIMATED** gekennzeichnet.

## Neue Navigation

Dashboard · Hardware · **BIOS / UEFI** · Live Monitor · Sensorquellen · **Benchmark** · **Memory Test** · **Storage** · Komponententest · Netzwerk · Speedtest · Netzwerk-Scanner · **Gaming Check** · **Can My PC Run It** · **Upgrade Advisor** · **New PC Setup** · Updates · Treiber (mit **Driver Health**) · Programme · Autostart · Prozesse · Speicher bereinigen · Desktop Organizer · Windows Health (mit **Windows Health Score**) · Absturzanalyse · Ereignisse · Dienste · **System Snapshot** · Security Center · **Open Source Security** · Technician · Tools · Protokoll · PC-Bericht · Einstellungen

## Neu

### Benchmark Center
- **CPU:** Ganzzahl, Gleitkomma, Krypto (SHA-256), Kompression, Dekompression, AES und Rendering – je auf einem und auf allen Kernen; optional 2 Minuten Dauerlast mit **Thermal-Throttling-Erkennung**. Takt, Temperatur und Leistungsaufnahme werden – sofern ein Sensor sie liefert – während des Tests mitgeschrieben, sonst N/A.
- **GPU:** Direct3D-11-Compute-Shader messen FP32-Rechenleistung (GFLOPS) und Speicherbandbreite; optional 60-s-Stabilitätstest (Ergebnisvergleich Bit für Bit). Keine FPS-Angabe ohne Spiel (N/A).
- **RAM:** Lesen, Schreiben, Kopieren, Latenz. **Datenträger:** sequentiell und 4K am Windows-Cache vorbei, IOPS, Zugriffszeit, Modi Quick/Normal/Extended. **Netzwerk:** Ping, Jitter, Paketverlust, DNS, Gateway, Verbindungsgeschwindigkeit, WLAN-Signal, IPv4/IPv6, optional Speedtest.
- **START COMPLETE BENCHMARK:** CPU → GPU → RAM → SSD/HDD → Network → Cooling → Stability, danach **ULTIMATE X PERFORMANCE SCORE** (0–100) mit Einstufung ENTRY LEVEL · STANDARD · GAMING · HIGH-END GAMING · ENTHUSIAST (Skala geschätzt). Fällt ein Teil aus, zählt er nicht in die Wertung.
- **Expected vs Actual:** Messwert gegen Herstellerangabe bzw. Schnittstellenwert (ESTIMATED) mit Abweichung in % und möglichen Ursachen.
- **Bottleneck Analyzer:** CPU-/GPU-/RAM-/VRAM-/Storage-/PCIe-Limit, Throttling, Energieprofil, Hintergrundlast.

### Memory Test (RAM-Stabilität)
- Quick / Standard / Extended mit acht Testmustern (Nullen, Einsen, Schachbrett, invertiert, wandernde Eins/Null, Adresse-in-Adresse, Zufall), Fehleradresse, Anzahl gekippter Bits, PASS/WARNING/FAIL.
- **Boot Memory Test:** plant die Windows-Speicherdiagnose (mdsched.exe) für den nächsten Neustart und zeigt deren Ergebnis (Ereignisse 1201/1202).
- RAM-Konfiguration: Typ, Kanäle (Single/Dual/Quad, geschätzt falls nicht aus dem Slotnamen erkennbar), aktueller Takt vs. Nenntakt, **XMP/EXPO-Hinweis** (Nenntakt auch aus der Teilenummer, gekennzeichnet).

### Storage
- Laufwerksgesundheit aus SMART/Windows: Zustand GOOD / WARNING / CRITICAL / N/A mit Begründung (Verschleiß, Temperatur, nicht korrigierbare Fehler, Betriebsstunden, Einschaltvorgänge), Belegung je Volume.

### Gaming
- **Gaming Check** mit GAMING SCORE: Hardware, GPU-Treiberalter, Spielmodus, HAGS, Resizable BAR, Energieprofil, Temperaturen, Hintergrundprozesse, Ping, Secure Boot/TPM (Anti-Cheat).
- **Can My PC Run It:** lokale Datenbank mit Anforderungen von 42 Spielen; Bewertung EXCELLENT … NOT RECOMMENDED, **FPS-Schätzung** je Auflösung/Qualität/Raytracing/Upscaler (ESTIMATED), FPS-Ziele 30–240, erkannte **installierte Spiele** (Steam, Epic, Xbox, GOG, EA, Ubisoft), Websuche nach Anforderungen und Software im Standardbrowser.
- **Upgrade Advisor:** empfiehlt nur, wenn Messung oder Befund es stützen; kostenlose Maßnahmen (XMP, RAM-Kanäle, Energieprofil, Treiber, PCIe-Steckplatz) zuerst.

### New PC Setup
- Prüft vor der Installation Internet, Speicherplatz, ausstehenden Neustart, Windows-Update-Stand, Defender und winget.
- Profile BASIC / OFFICE / GAMING / DEVELOPER / CUSTOM, „Recommended for this PC“, bereits installierte Programme werden erkannt. **Nichts wird ohne Auswahl installiert.**
- Installation ausschließlich über den Windows-Paket-Manager (winget-Quelle / Microsoft Store). Vor jeder Installation: Installer-URL aus dem Manifest muss HTTPS sein und zur **offiziellen Herstellerdomain** passen (bei GitHub nur das offizielle Repository), Filehoster/Download-Portale sind gesperrt, SHA-256 muss im Manifest stehen.
- **Microsoft 365 Apps** über das offizielle Office Deployment Tool (Download von officecdn.microsoft.com, Authenticode „Microsoft Corporation“ geprüft, Datei bis zum Start gegen Austausch gesperrt). Keine Lizenzumgehung.
- Installationsprotokoll (`install-log.jsonl`) und **SET UP THIS PC** (One-Click-Ablauf von Hardware-Scan bis PDF-Bericht).

### Open Source Security (ClamAV)
- Optionaler Zusatz-Scanner: Installation erst nach Online-Prüfung des Projekts (aktiv gepflegt, nicht archiviert), Signatur-Update, Quick/Full/USB/Download/Custom Scan, optionale Quarantäne, Scanverlauf.
- **Microsoft Defender bleibt der primäre Echtzeitschutz und wird nie deaktiviert oder verändert.**

### Diagnose & Bericht
- **Driver Health:** Geräte mit Fehlercode, unbekannte Geräte, unsignierte und sehr alte Treiber, Grafiktreiberalter.
- **BIOS/UEFI Center** (nur lesend): UEFI/Legacy, Secure Boot, TPM, Virtualisierung, Speicherintegrität/VBS, Hyper-V, BIOS-Alter – mit Erklärung, keine Änderungen.
- **Windows Health Score** (100 minus nachvollziehbare Abzüge).
- **System Snapshot:** Systemstand speichern und BEFORE/AFTER vergleichen (Hardware, Treiber, Programme, Dienste, Autostart, Netzwerk, Updates – ohne IP/MAC/Seriennummern).
- **Complete PC Check** um Treiber, Arbeitsspeicher, BIOS/UEFI und Performance Score erweitert; **Visual PC Map** zeigt RAM-Kanäle/Takt, Firmware-Status und Performance Score.
- **PC-Bericht:** neuer Abschnitt „Bewertung und Empfehlungen“ (Scores, RAM-Test, Laufwerke, Probleme, Empfehlungen).

## Sicherheit
- Neue Befehle nur über die `CommandCatalog`-Allowlist (winget show/install, Store-Install mit 12-stelliger Produkt-ID, ClamAV version/update/scan mit Pfadprüfung, ODT); Argumente als `ArgumentList`, keine Shell-Strings aus Benutzereingaben.
- Der Admin-Helper lehnt das Office Deployment Tool ab (keine erhöhte Ausführung aus benutzerbeschreibbaren Ordnern über den Helper); die App startet es selbst per UAC nach Signaturprüfung mit Dateisperre.
- Details: [THREAT_MODEL.md](THREAT_MODEL.md) T-35 bis T-41.

## Datenschutz
- Keine Telemetrie. Neue Internetzugriffe nur auf Benutzeraktion – siehe [DEPENDENCIES.md](DEPENDENCIES.md#externe-dienste-nur-auf-benutzeraktion).

## Tests
- 316 Unit-Tests (v1.1: 182) – neu u. a. Scores, Hardware-Erkennung, RAM-Takt/Kanäle, Testmuster, Laufwerks-/Netzwerk-/Windows-Bewertung, Spielekompatibilität und FPS-Schätzung, Bottleneck/Upgrade/Gaming Check, Quellen-/Hash-/Signaturprüfung, winget-Ausgabe, Snapshot-Vergleich, Berichtsabschnitt, ClamAV-Pfadprüfung, Übersetzungen.

## Bekannte Einschränkungen
- Leistungsklassen von CPUs/GPUs und alle FPS-Werte sind Schätzungen aus einer lokalen Datenbank (Stand 2026); unbekannte oder mobile GPUs → N/A.
- Die Punkteskala des Performance Scores ist geschätzt (100 ≈ aktuelle Spitzenklasse für Endanwender) und noch nicht an vielen Systemen kalibriert.
- Ohne HWiNFO/LibreHardwareMonitor fehlen CPU-Temperatur/-Leistungsaufnahme (N/A); Kühlungs-Score und Throttling-Erkennung sind dann eingeschränkt.
- Resizable BAR ist unter Windows ohne Herstellerwerkzeug nicht zuverlässig auslesbar (N/A).
- Der In-App-RAM-Test prüft nur den freien Speicher im laufenden Windows; für nahezu den gesamten RAM den Boot Memory Test verwenden.
- ClamAV bietet keinen Echtzeitschutz; Full Scans können Stunden dauern.
