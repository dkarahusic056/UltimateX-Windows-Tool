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

| `ScoreTests` (v1.2) | Log-/Linearskalen, gewichteter Mittelwert ohne fehlende Werte, Einstufungsgrenzen, Teilwertungen = N/A ohne Messung, Referenz = 100, Kühlung bei Throttling begrenzt, keine Einstufung ohne CPU+GPU |
| `HardwareDatabaseTests` (v1.2) | GPU-Erkennung (längster Schlüssel, mobile/unbekannte → null), CPU-Generationen Intel/Ryzen/X3D, eindeutige Schlüssel |
| `MemoryAnalysisTests`, `MemoryPatternTests` (v1.2) | XMP/EXPO aus Teilenummern (G.Skill, Kingston, Corsair), Kanäle aus Slotnamen bzw. geschätzt, Bandbreite, Unter-Nenntakt-Warnung; alle 8 Muster fehlerfrei, gekipptes Bit mit Adresse, Fehlerlimit, PASS/WARNING/FAIL |
| `HealthEvaluatorTests` (v1.2) | Laufwerk GOOD/WARNING/CRITICAL/N/A (Verschleiß, Fehler, Temperatur je Medium, N/A verdeckt kein CRITICAL), Network Health Score, Windows Health Score |
| `GameCompatibilityTests` (v1.2) | Datenbank eingebettet, Suche/Aliase, Einstufung, Mindestanforderung verfehlt, unbekannte GPU → N/A, FPS ESTIMATED und auflösungsabhängig, nicht unterstütztes RT/DLSS → N/A, FPS-Ziele |
| `AdvisorTests` (v1.2) | Bottleneck (CPU-Limit), Upgrade Advisor (HDD zuerst, kostenlose Maßnahmen, ohne Benchmark keine Empfehlung), Gaming Check |
| `SoftwareCatalogTests` (v1.2) | Katalog-IDs passieren die Allowlist, Profile, Empfehlungen, URL-Prüfung (HTTPS, Domain, Subdomain-Trick, Zugangsdaten, Filehoster, GitHub nur offizielles Repo), SHA-256, Herausgeber, `winget show` DE/EN, installierte Programme, URL-Kodierung der Suche |
| `ClamAvCommandTests` (v1.2) | Scanziel: UNC/`//`/Gerätepfade/relative Pfade/Optionen abgelehnt; eigene Daten ausgeschlossen; Ziel als letztes Argument |
| `SnapshotComparerTests`, `ReportAnalysisTests`, `V12LocalizationTests` (v1.2) | BEFORE/AFTER-Vergleich; Berichtsabschnitt „Bewertung und Empfehlungen“ im PDF; Übersetzung neuer Texte |

### v1.3 (`tests/UltimateX.Core.Tests/V13*.cs`)

| Datei · Testklasse | Prüft |
|---|---|
| `V13BackupTests` · `BackupPathTests` | Speicherpfade Roundtrip, ungültige Pfade aus Manifesten abgelehnt, nur lokale Laufwerkspfade, bereinigte/eindeutige Ordnernamen (keine reservierten Namen), strenge Versionsnamen, Wiederherstellen in Ordner ohne Ausbruch |
| `BackupExclusionTests`, `BackupTemplateTests` | Ausschlussmuster (Ordnergrenzen, ungültige Muster, Regex-Sonderzeichen wörtlich), Vorlage „Programmierung“ behält `.git`; Lesezeichen-Vorlage nimmt nur Lesezeichen-Dateien, fehlende Ordner entfallen, Quellen dedupliziert |
| `BackupPlannerTests` | unveränderte Dateien verlinkt, geänderte kopiert; Aufbewahrung behält neueste vollständige Versionen und ignoriert fremde Namen; tägliche/wöchentliche Zeitpläne holen verpasste Termine nach; „Beim Anstecken“ und Überfällig-Warnung; Stichprobe verteilt und begrenzt |
| `WbadminParserTests`, `SystemImageTargetTests` | wbadmin-Fortschritt DE/EN, stückweise Ausgabe, Zwischenstand/Fehler, Versionsliste; Regeln für das Abbild-Ziel (nicht Systemlaufwerk, NTFS, Platz) |
| `RestoreConflictTests` | vorhandene Dateien werden nie stumm überschrieben |
| `BackupCommandTests` | feste wbadmin-Argumente, Systemlaufwerk als Ziel abgelehnt, Injection-Versuche, Abbildliste mit/ohne Ziel, RecoveryDrive interaktiv/erhöht/ohne Parameter, Treiber-Token (Format, ungültige Tokens, fester existierender Ordner), Reparse-freie Pfadkette |
| `FileBackupEngineTests`, `BackupServiceTests` | Inkrementelle Läufe mit Hardlinks + Aufbewahrung, Rückfall auf Kopie, gesperrte Dateien übersprungen und gemeldet, Ziel in Quelle und Abbruch, Löschschutz außerhalb eigener Backup-Ordner, Konfliktmodi und Originalort beim Wiederherstellen, geschützte Systemordner; Profile speichern/validieren/exportieren/importieren, Zielplatten-Kennung und verschobenes Laufwerk, beschädigte Kennung ignoriert |
| `V13BenchTests` · `V13CalibrationTests` | Score-Kalibrierung (i9-13900K hoch, aber nicht gesättigt; RTX 4080 ≈ 80, RTX-5090-Klasse = 100; DDR5-6000 60–70; NVMe Gen4 ≈ 90), Sättigung nur über der Referenz, Monotonie, Referenz-PC = HIGH-END GAMING |
| `V13CauseTests`, `V13StorageTests`, `V13GeekbenchTests` | Ursachen nur passend zur Komponente bzw. Prüfliste; NVMe-Erwartung je Generation, N/A ohne eindeutige Generation, Wertung des Systemlaufwerks statt des zuletzt getesteten, N/A ohne Systemlaufwerk-Lauf, Geekbench nie im Score; Geekbench-Eingaben (Tausendertrennzeichen, ungültige Werte, Veränderung in %), Start nur aus festem Program-Files-Pfad ohne Parameter |
| `V13GamesTests` · `SteamRequirementsParserTests`, `SteamSearchTests` | Minimum/Empfohlen vollständig, VRAM aus Datenbank/Grafikzeile, MB/TB/Tausendertrennzeichen, HTML-Entities, SSD-Regeln, CPU-Alternativen, Unbekanntes bleibt leer (Bewertung N/A), fehlende Abschnitte, appdetails-Fehler, URLs nur HTTPS-Steam-Shop; bester Treffer per normalisiertem Namen, kein Treffer bei unähnlichem Namen, nur Apps |
| `OnlineGameEstimateTests`, `UserGamesTests` | Raytracing/Upscaling für Online-Spiele N/A mit Grund, N/A statt 0; eingebaute Datenbank weiter ladbar, Benutzer-Spiele ersetzen nie eingebaute, Speicherformat robust, atomares Laden/Entfernen, beschädigte Datei ohne Absturz |
| `V13NetSecTests` · `V13TracerouteTests`, `V13PortCheckTests` | Hop-Klassifizierung, stumme Hops „*“, Ende am Ziel/bei Unerreichbarkeit, max. 30 Hops, Ziel über `InputValidator`; Portlisten parsen/deduplizieren, ungültige Listen, max. 20 Ports, gängige Dienste passen in einen Lauf |
| `V13DnsTests`, `V13WakeOnLanTests` | feste DNS-Adressen, DHCP, ungültige Parameter abgelehnt, Parameter-Roundtrip; MAC-Formate, Magic-Packet-Aufbau, Broadcast des eigenen Subnetzes |
| `V13SecurityAuditTests` | gehärteter PC = 100 ohne Behebungen, Behebungen für SMB1/RDP/Firewall, RDP mit NLA nur Warnung, SMB1 ohne Adminrechte = N/A, Konten/Gast/Admins, unbekannte Fakten = N/A ohne Wertung, UPnP/PowerShell 2.0, Windows-Support-Tabelle inkl. ESU-Hinweis |
| `V13ScheduledTaskTests`, `V13PrivacyTests`, `V13NetSecCommandTests` | Aufgaben-Token (Roundtrip, ungültige/zu lange Pfade, Injection im Base64, nicht kanonische Tokens), Microsoft-Aufgaben, Markierungen, Pfadnormalisierung ohne Dateizugriff; Datenschutz-Tabelle fest, `reg`-Argumente, ungültige Parameter, Zustandsbewertung, Sicherung nur des ersten Stands; feste Befehle mit exakten Argumenten, parameterlose Befehle lehnen Parameter ab, Firewall wird nur eingeschaltet |
| `V13OverlayTests` · `V13FrameStatsTests` | 60 FPS = 16,7 ms, Ruckler senken 1 % Low, veraltete Daten, Pausen zählen nicht, Reset, ungültige/ungeordnete Zeitstempel, 10-s-Fenster |
| `V13AlertRulesTests`, `V13OverlaySettingsTests`, `V13OverlayDataTests` | Temperaturregeln erst nach Mindestdauer, Rücksetzen, Wiederholungssperre 30 min, GPU-Standard 85 °C, Laufwerk Warnung/kritisch, fehlende Werte übersprungen, Grenzwerte begrenzt; Standardwerte, JSON-Roundtrip, beschädigte Datei → Standardwerte, Profile, Tastenkürzel gültig/ungültig; Zeilen ohne Quelle ausgeblendet, FPS N/A nur bei fehlender Berechtigung, Sitzungs-CSV (Roundtrip, Zellbereinigung), manueller Speedtest, Browser nur `https://*.speedtest.ch` |
| `V13SystemTests` · `V13PowerScheduleTests` | strenge Uhrzeit, max. 7 Tage, „morgen“ nach Mitternacht, vergangene Uhrzeit abgelehnt, Sommer-/Winterzeit (Lücke, doppelte Stunde, kürzere/längere Nacht), Aufrunden, Verschieben, Warnungen bei 5 und 1 min, Restzeitformat, `shutdown`-Argumente ohne Kommentar, Speicher-Roundtrip |
| `V13LifetimeTests`, `V13SpaceAnalysisTests`, `V13TimelineTests` | Laufzeit aus Mitschrift/Heartbeat, überlappende Sitzungen, Starts/Laufzeit aus dem Ereignisprotokoll, Absturz behält letzten Heartbeat; Ordnersummen, Abbruch, Duplikate nur bei identischem Inhalt, unlesbare Dateien, Löschsperre in Systemordnern/Laufwerkswurzeln; Ursachen-Hinweis binnen 48 h, kein Hinweis bei früheren Abstürzen, Filter/Suche/Gruppierung, MSI-Ereignis ersetzt Registry-Datum |

Ausführen: `dotnet test tests\UltimateX.Core.Tests -p:Platform=x64` (v1.3: **797 Tests**; v1.2: 316)

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
| v1.3: Backup-Profil auf externe NTFS-Platte, zweiter Lauf | zweite Version mit Hardlinks, Prüfung OK, Aufbewahrung löscht nur eigene Versionen |
| v1.3: Backup bei geöffneter Outlook-PST | Datei übersprungen und im Bericht aufgeführt, Lauf „mit Hinweisen“ |
| v1.3: Wiederherstellen mit „Ersetzen“ | alte Datei im Papierkorb, Sicherung zurückgeschrieben |
| v1.3: System-Abbild auf externe Platte, „Anzeige beenden“, Abbilder anzeigen | wbadmin läuft weiter, Abbild erscheint in der Liste; Systemlaufwerk als Ziel nicht wählbar |
| v1.3: Treiber sichern, Rettungs-USB-Stick | Ordner mit Paketen; Windows-Assistent startet nach UAC |
| v1.3: Can My PC Run It – „Online suchen“ (bekanntes/unbekanntes/mehrdeutiges Spiel), offline erneut öffnen | Quelle Steam mit Datum; mehrdeutig → Trefferliste; gespeicherte Daten offline verfügbar |
| v1.3: Overlay im randlosen Fenster mit/ohne „Leistungsprotokollbenutzer“ | FPS/1 % Low bzw. „FPS: N/A“ mit Anleitung; Klicks gehen ans Spiel |
| v1.3: Infobereich – Fenster schließen mit „In den Infobereich minimieren“ | App läuft weiter, Menü Öffnen/Overlay/Pausieren/Beenden |
| v1.3: Zeitgesteuertes Herunterfahren in 6 min, App beenden | Warnung bei 5 min (wenn App läuft); Windows fährt trotzdem herunter; Sperren wird bei geschlossener App nicht nachgeholt (Hinweis beim Start) |
| v1.3: DNS auf Quad9 und zurück auf DHCP, UAC einmal ablehnen | Teiländerung gemeldet, DHCP stellt zurück |
| v1.3: Sicherheits-Check mit aktivem SMB1 / RDP ohne NLA | FAIL mit Behebung; nach Behebung + Neustart PASS |
| v1.3: speedtest.ch eingebettet ohne WebView2-Runtime / mit Werbelink | Hinweis + Download-Knopf; fremder Link blockiert |
| v1.3: Speicherplatz-Analyse C:\, Datei in C:\Windows ankreuzen | Kästchen gesperrt mit Grund; Löschen anderer Dateien landet im Papierkorb |
| v1.3: Zeitleiste nach Treiberinstallation + Absturz | ⚑-Markierung und Ursachen-Hinweis |
