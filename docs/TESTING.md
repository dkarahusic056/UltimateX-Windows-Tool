# Test-Dokumentation (§104, §105)

## Automatisiert (`tests/UltimateX.Core.Tests`, xUnit)

| Testklasse | Prüft |
|---|---|
| `PathGuardTests` | Path Traversal (`..\..\Windows`), geschützte Systemordner, UNC/Device/relative Pfade, Präfix-Verwechslung (`Alice` vs. `Alice2`) |
| `CommandCatalogTests` | Injection-Versuche im Laufwerksparameter (`C: & del …`), ungültige Countdowns, absolute System32-Pfade aller Befehle, strikte Helper-Tokens |
| `InputValidatorTests` | Hostnamen/IP-Validierung, Ablehnung von Argument-Injection (`-n 1000 …`) |
| `MaskerTests` | Maskierung MAC/IP/Seriennummer |
| `RepairInterpretationTests` | SFC (DE/EN), DISM, CHKDSK-Exitcodes |
| `HealthScoreTests` | Nachvollziehbarkeit: Summe der Abzüge = 100 − Score, fehlende Daten ohne Abzug |
| `OutputDecodingTests` | UTF-16-Erkennung der SFC-Ausgabe |
| `CommandCatalogTests` (erweitert) | winget-Paket-IDs, Autostart-Token (Roundtrip, Sonderzeichen), Update-IDs nur als GUID, vertrauenswürdige Pfade aller Befehle |
| `PathGuardTests` (erweitert) | Windows\Temp-Inhalt bereinigbar, Ordner selbst und System32 geschützt |
| `WingetParserTests` | Tabellenauswertung sprachunabhängig, Müll-Ausgabe ergibt keine Updates |
| `DesktopOrganizerTests` | Kategorisierung, ungültige Endungen werden ignoriert |
| `DnsQueryTests` | DNS-Paketaufbau, zu lange Labels werden abgelehnt |
| `LocalizationTests` | Wörterbuch eingebettet, exakte Texte, Vorlagen mit Zahlen, Platzhalter-Übersetzung, mehrzeilige Meldungen, Unbekanntes bleibt unverändert |
| `ScannerTests` | Nur private Adressen, Subnetz-Berechnung, Begrenzung auf 1024 Adressen, IEEE-CSV-Parser, zufällige MACs |
| `CrashAnalysisTests` | BugCheck-Katalog, Hinweisregeln (WHEA, Treiber, Speicher), Auswertung der Debugger-Ausgabe |
| `SensorManagerTests` (v1.1) | Zuordnung zum einheitlichen Sensormodell (Intel/AMD/NVIDIA/SMART/Mainboard), ACPI nie als CPU-Temperatur, Failover HWiNFO → LHM → Windows, Rückfall bei Quellenausfall, Min/Ø/Max, Status INACTIVE/NOT AVAILABLE, keine erfundenen Werte |
| `LivePingTests` (v1.1) | Zielvalidierung inkl. Injection-Versuchen (`&`, `;`, `$()`, Argumente), Auswertung Verlust/Min/Ø/Max/Jitter, nicht erreichbar ohne erfundene Latenz |
| `ComponentTestLogicTests` (v1.1) | deterministische CPU-Kernel, RAM-Mustertest, Vollständigkeit aller geforderten Komponenten, Verlauf/Vergleich, beschädigter Verlauf, WAV-Testton, TXT/JSON-Bericht |
| `PdfReportTests` (v1.1) | gültige PDF-Struktur (xref/startxref), Datenschutzmodus maskiert MAC/IP/Benutzer im PDF-Inhalt, Abschnittsauswahl, String-Escaping/WinAnsi, Textumbruch |
| `HelperPipeProtocolTests` (v1.1) | Schema-Prüfung (Version, Typ, Run-ID, Nonce), Replay/Sequenzlücken, Datengröße, Exit-Code, Längenpräfix-Limit, ungültiges JSON, Nonce-Format |

Ausführen: `dotnet test tests\UltimateX.Core.Tests -p:Platform=x64`

## Manuelle Testmatrix

| Szenario | Erwartung |
|---|---|
| Windows 10 22H2 / Windows 11 23H2+ | Start, alle Seiten laden |
| Standardbenutzer ohne Adminrechte | App startet; Reparaturen zeigen UAC mit Admin-Anmeldung; Ablehnung → „nicht ausgeführt“ |
| Administrator | wie oben, UAC-Zustimmung |
| Kein Internet | Dashboard „Offline“, Diagnose zeigt Schritt, Speedtest meldet Fehler ohne Absturz |
| Intel/AMD-CPU, NVIDIA/AMD/iGPU, Laptop/Desktop | Hardwareseite ohne erfundene Werte |
| Mehrere Monitore | Fenster/Skalierung korrekt |
| Skalierung 100/125/150/200 % | Layout lesbar, keine abgeschnittenen Elemente (PerMonitorV2) |
| Windows Hello eingerichtet/nicht eingerichtet | Hello-Dialog bzw. Hinweis + Bestätigung |
| PC sperren während Security Session | Session endet, Maskierung aktiv |
| `settings.json` mit Unsinn überschreiben | Start mit Standardwerten, `.broken-*`-Backup vorhanden |
| Signierte App, Helper-DLL/EXE manipuliert | App startet nicht bzw. Helper wird abgelehnt |
| Autostart-Eintrag deaktivieren/aktivieren | Status identisch im Task-Manager, Backup-Datei erstellt |
| SFC/DISM/CHKDSK | Live-Ausgabe, verständliche Zusammenfassung, Originalausgabe einblendbar |
| Desktop Organizer mit/ohne „automatisch anordnen“ | Hinweis bzw. Anordnung; „Wiederherstellen“ stellt exakt die alten Positionen her |
| Cleaner bei geöffneten Dateien im Temp-Ordner | gesperrte Dateien übersprungen, Zähler stimmt |
| Treiber-Update aus Windows Update | Restore Point, UAC, Ergebnis, ggf. Neustart-Hinweis |
| winget-Update | Fortschritt, Exit Code, Status in der Liste |
| App-Update mit falschem SHA-256 / fremder Signatur | Update wird verworfen, Security-Log-Eintrag |
| NVIDIA / AMD / Intel GPU | Last/VRAM/Temperatur bei allen (aktueller Treiber) |
| HWiNFO mit Shared Memory / LibreHardwareMonitor gestartet | CPU-Kerntemperaturen erscheinen mit Quellenangabe |
| Benchmark CPU/RAM/Datenträger | Fortschritt, Abbrechen möglich, Testdatei danach gelöscht, Verlauf |
| Netzwerk-Scanner im Heimnetz | Router/eigener PC markiert, neue Geräte nach 2. Scan als NEU |
| Sprache English | Oberfläche, Dialoge und Benachrichtigungen auf Englisch |
| Windows 11 24H2 ohne Standortfreigabe | WLAN zeigt Hinweis statt SSID |
| Toasts mit MSI-Installation | Benachrichtigung erscheint im Info-Center |
| v1.1: Sensorquellen mit/ohne HWiNFO (Shared Memory an/aus) und LibreHardwareMonitor | Status CONNECTED/AVAILABLE/INACTIVE/NOT AVAILABLE korrekt, Failover beim Beenden von HWiNFO |
| v1.1: Netzwerk-Scanner → LIVE PING → Seite wechseln | Dauerping endet, keine Hänger |
| v1.1: TEST ALL SYSTEM COMPONENTS | alle Tests liefern einen Status, keine Änderungen am System, Verlauf gespeichert |
| v1.1: Kamera-/Mikrofontest mit gesperrter Datenschutzfreigabe | verständlicher Hinweis statt Absturz |
| v1.1: PDF-Export und Druck auf „Microsoft Print to PDF“ | identisches Layout, Datenschutzmodus maskiert IP/MAC |
| v1.1: Admin-Befehl (z. B. SFC) | Live-Ausgabe über Named Pipe, kein Ordner `%ProgramData%\UltimateX\HelperRuns` mehr nötig |
