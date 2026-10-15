# ULTIMATE X Windows Control 1.3.0 – Release Notes

Version 1.3 erweitert die bestehende App um Datensicherung, Netzwerk- und Sicherheitswerkzeuge, ein In-Game-Overlay und mehrere Diagnose-Seiten – ohne sie neu zu schreiben: gleiche Architektur (MVVM, `CommandCatalog`-Allowlist, Admin-Helper über abgesicherte Named Pipe), gleiches Design (Dark/Light, Deutsch/Englisch).

**Grundsatz „keine Fantasiewerte“ gilt weiter:** Was nicht gemessen werden kann, steht als **N/A** da; Schätzungen sind als **ESTIMATED** gekennzeichnet.

**Neu: Benutzerhandbuch** auf Deutsch ([HANDBUCH.md](HANDBUCH.md)) und Englisch ([MANUAL_EN.md](MANUAL_EN.md)).

## Neue Navigation

Dashboard · Hardware · BIOS / UEFI · Live Monitor · Sensorquellen · Benchmarks · Memory Test · Storage · **PC Lifetime** · Komponententest · Netzwerk · Speedtest · Netzwerk-Scanner · **Netzwerk-Werkzeuge** · Gaming Check · Can My PC Run It · Upgrade Advisor · **Overlay** · New PC Setup · Updates · Treiber · Programme · Autostart · **Geplante Aufgaben** · Prozesse · Speicher bereinigen · **Speicherplatz-Analyse** · Desktop Organizer · Windows Health · Absturzanalyse · **Zeitleiste** · Windows Logs · Dienste · System Snapshot · **Backup-Center** · Security Center · **Sicherheits-Check** · **Datenschutz** · Open Source Security · Technician Mode · Tools & Power · Logs · PC-Bericht · Einstellungen · Gaming Mode · Visual PC Map · Admin / Service Mode

## Neu

### Backup-Center
- **System-Abbild** mit Windows-Bordmitteln (`wbadmin`, UAC): Systemlaufwerk samt EFI- und Wiederherstellungspartition auf eine externe NTFS-Platte. Vorabprüfung des Ziels (nicht das Systemlaufwerk, lokal, NTFS, freier Platz ≥ belegter Platz + 10 %), Fortschritt je Volume, Liste vorhandener Abbilder, **Abbild durchsuchen** (VHDX einbinden) und eine Schritt-für-Schritt-Anleitung zum Zurückspielen inkl. Sprung in den erweiterten Start.
- **Datei-Backup-Profile mit Versionen:** jede Version ist ein vollständiger, im Explorer lesbarer Ordner; unveränderte Dateien werden per **Hardlink** aus der Vorversion übernommen (NTFS), sonst kopiert. Vorlagen (Desktop, Dokumente, Bilder, Videos, Musik, Downloads, OneDrive, Browser-Lesezeichen, Quellcode), eigene Ordner per Drag & Drop, Ausschlussmuster inkl. Vorlage „Programmierung“ (`.git` bleibt erhalten), optionale Hash-Erkennung, Prüfung nach jeder Sicherung (Vollständigkeit + SHA-256-Stichprobe), Aufbewahrung (Standard 10 Versionen), Zielplatten-Erkennung über `target.json` auch bei geändertem Laufwerksbuchstaben, Export/Import der Profile.
- **Wiederherstellen** von Dateien und Ordnern an den Originalort oder in einen anderen Ordner; bei Konflikten *behalten*, *beide behalten* oder *ersetzen* (alte Datei in den Papierkorb); Vorschau der betroffenen Dateien vor dem Schreiben.
- **Zeitplan:** manuell, täglich, wöchentlich oder beim Anstecken der Zielplatte; verpasste Sicherungen werden beim nächsten Start nachgeholt; Warnung bei überfälliger Sicherung (Übersicht und Infobereich).
- **Treiber-Sicherung** (`pnputil /export-driver`, UAC) in einen festen Ordner je Zeitstempel, mit Anleitung zum Zurückspielen.
- **Rettungs-USB-Stick:** startet den Windows-Assistenten „Wiederherstellungslaufwerk“ (UAC).

### Spiele
- **Spiele-Anforderungen online von Steam** (Can My PC Run It): Suche im öffentlichen Steam-Shop ohne Steam-Konto, Laden von Minimum/Empfohlen, **lokale Speicherung** (`games-user.json`) für die Offline-Nutzung, Quellenangabe mit AppID und Abrufdatum. „Fehlende Anforderungen online laden“ ergänzt erkannte installierte Spiele (Steam per AppID, andere Launcher per Name) mit 1,5 s Pause zwischen den Abrufen und Abbruch bei HTTP 429. Nur ausdrücklich genannte Werte werden übernommen; Raytracing/Upscaling für Online-Spiele = N/A mit Begründung.
- **In-Game-Overlay** (Seite *Overlay*): durchklickbares Fenster über dem Spiel mit FPS, Frametime, 1 % Low, CPU/GPU-Auslastung, Temperaturen, Takt, GPU-Leistung, RAM, VRAM, Ping und Uhrzeit; Monitor, Ecke, Schriftgröße, Deckkraft, Hintergrund, Ping-Ziel (Router oder 1.1.1.1), globales Tastenkürzel (Standard Strg+Umschalt+O), Profile.
- **FPS über ETW ohne Injection:** FPS, Frametime und 1 % Low des Vordergrundprogramms aus den Windows-Ereignissen „Microsoft-Windows-DXGI“/„Microsoft-Windows-D3D9“ (Prinzip PresentMon) – kein Code im Spiel, Anti-Cheat-neutral. Benötigt Adminrechte oder die Gruppe „Leistungsprotokollbenutzer“, sonst FPS = N/A mit Anleitung.
- **Spiel-Sitzung aufzeichnen:** sekündliche CSV unter `Dokumente\ULTIMATE X\Sessions`, Auswertung mit FPS- und Temperaturverlauf.

### Benchmarks
- **Kalibrierung des Performance Scores:** neue Referenzwerte (100 ≈ aktuelle Spitzenklasse 2026, logarithmisch). In 1.2 stand die CPU wegen falscher Referenzen immer bei 100; gespeicherte Messwerte bleiben gültig, nur die Bewertung ändert sich.
- **Ursachen je Komponente:** liegt ein Wert > 10 % unter der Erwartung, nennt Expected vs Actual nur Ursachen, die zur Komponente passen (GPU, RAM, Datenträger, CPU), sonst die Prüfliste der Komponente.
- **Systemlaufwerk-Wertung:** In SSD-Score und Bottleneck-Analyse zählt immer das Windows-Systemlaufwerk, nicht der zuletzt getestete Datenträger; Erwartungswert je Schnittstelle (NVMe-Generation nur bei eindeutiger Erkennung, sonst N/A).
- **Geekbench-Karte:** startet ein bereits installiertes Geekbench 6/5 aus `Program Files`, öffnet sonst nur die offizielle Download-Seite im Browser; Ergebnisse werden von Hand eingetragen, mit Vorergebnis derselben Hauptversion verglichen und fließen nicht in den ULTIMATE X Score ein.

### Netzwerk & Sicherheit
- **Netzwerk-Werkzeuge:** Traceroute (max. 30 Hops, Abbruch nach 5 stummen Hops), Port-Prüfung (ein Ziel, max. 20 Ports, kein Portscanner), DNS-Cache leeren, IP-Adresse erneuern, DNS-Server umstellen (nur Cloudflare/Quad9/Google oder DHCP, IPv4/IPv6, UAC), Netzwerk zurücksetzen (Winsock + TCP/IP, UAC), Wake-on-LAN (Broadcast ins eigene Subnetz, Geräte aus dem Netzwerk-Scanner).
- **Sicherheits-Check** mit SECURITY SCORE: SMB1, Remotedesktop/NLA, Firewall-Profile, lokale Administratoren, Konten ohne Passwortpflicht, Gastkonto, UPnP am Router, Autorun/AutoPlay, PowerShell 2.0, Support-Status der Windows-Version. Behebungen nur nach Bestätigung + UAC: SMB1 deaktivieren, Remotedesktop ausschalten, Firewall überall einschalten.
- **Geplante Aufgaben:** alle Aufgaben der Aufgabenplanung mit Auslöser, Läufen, Programm und Signatur; Markierungen (Microsoft-signiert, unsigniert, Programm fehlt, Pfad in Temp/AppData …), Filter, Deaktivieren/Aktivieren (UAC) – nie löschen.
- **Datenschutz:** sechs Windows-Datenschutzeinstellungen (Werbe-ID, Tipps und Vorschläge, maßgeschneiderte Erfahrungen, Websuche im Startmenü, Aktivitätsverlauf, Diagnosedaten „Erforderlich“) einzeln umschalten; ursprüngliche Werte werden gesichert und lassen sich mit „Rückgängig“ wiederherstellen.

### System & Diagnose
- **Zeitgesteuertes Herunterfahren/Neustart/Sperren/Abmelden/Energiesparen** (Tools & Power): „in …“ oder „um …“ (heute/morgen), höchstens 7 Tage im Voraus, Warnung 5 und 1 Minute vorher mit Abbrechen/Verschieben, +15 min/+1 h, korrekte Behandlung der Zeitumstellung. Herunterfahren/Neustart plant Windows (`shutdown /t`), die übrigen Aktionen führt die laufende App aus.
- **PC Lifetime:** Betriebsstunden, Einschaltvorgänge und Verschleiß je Laufwerk (SMART), Windows-Installationsdatum, Starts und Gesamtlaufzeit laut Ereignisprotokoll, unerwartete Abschaltungen, eigene Laufzeit-Mitschrift; CPU/GPU/RAM ehrlich als „N/A – kein Stundenzähler“.
- **Speicherplatz-Analyse:** Ordner nach Größe (mit Navigation), größte Dateien (Top 100), Duplikate (gleiche Größe → erste 64 KB → vollständiger SHA-256), Dateitypen; Löschen nur angekreuzter Dateien in den Papierkorb, Systemordner gesperrt.
- **Zeitleiste „Was ist passiert?“:** Updates, Programm- und Treiberinstallationen, Bluescreens, unerwartete Neustarts, Programmabstürze, Start/Herunterfahren, Snapshots und Autostart-/Desktop-Sicherungen über 7/30/90 Tage; **Ursachen-Hinweis**, wenn der erste Absturz binnen 48 h nach einer Treiber-/Update-Installation auftritt.
- **Überwachung im Infobereich:** Symbol in der Taskleiste mit Menü (Öffnen, Overlay ein/aus, Überwachung pausieren, Beenden); Regeln für CPU-/GPU-Temperatur (mit Mindestdauer), Laufwerksbelegung, SMART-Warnung, neuen Bluescreen, ausgeschalteten Virenschutz und überfällige Sicherung; Prüfintervall und Wiederholungssperre einstellbar; optional beim Schließen in den Infobereich minimieren. Nur lesend, kein Internet.
- **speedtest.ch eingebettet** (Seite *Speedtest*): Test von speedtest.ch in einer abgeschotteten WebView2-Ansicht; Ergebnis wird von Hand übernommen und erscheint im Verlauf als „speedtest.ch (manuell)“. Der eigene Cloudflare-Speedtest bleibt unverändert.

### Design & Hilfe
- **Neues Logo** (App-Icon, Seitenleiste, Taskleiste, Startmenü) und **neuer Hintergrund** im Hauptfenster; im hellen Design stark abgedeckt, damit Texte gut lesbar bleiben.
- Installer mit neuen Grafiken (Willkommens- und Bannerbild).
- **Benutzerhandbuch als PDF** (Deutsch und Englisch) wird mitinstalliert: *Einstellungen → Handbuch öffnen* oder Startmenü → *ULTIMATE X Handbuch*.

## Sicherheit
- Alle neuen Systembefehle laufen über die `CommandCatalog`-Allowlist (neue Partial-Dateien `CommandCatalog.Backup/Network/System/Tools.cs`) mit festen Argumentlisten und streng geprüften Token-Parametern: Laufwerksbuchstabe (nie das Systemlaufwerk als Abbild-Ziel), Treiber-Token `<Laufwerk>-<yyyyMMdd-HHmmss>` mit festem Zielordner und Reparse-Point-Prüfung, Schnittstellen-Index + Anbieter-ID aus fester Liste (DNS), Base64url-Aufgabentoken (Geplante Aufgaben), Datenschutz-ID aus fester Tabelle, Sekunden 0–604 800 nur als Ziffern (`shutdown /t`, ohne Kommentartext). `RecoveryDrive.exe` und Geekbench werden ohne Parameter gestartet, Geekbench nur aus `Program Files`.
- Eingebettete Webansicht: Navigation nur zu `https://(*.)speedtest.ch` (`EmbeddedBrowserPolicy`), Pop-ups/Downloads/Berechtigungen/Autofill/Entwicklerwerkzeuge/Host-Objekte aus, eigener Profilordner.
- FPS-Messung ohne Injection und ohne Treiber; das Overlay-Fenster ist durchklickbar, wird nie aktiviert und erfasst keine Eingaben.
- Datenverlust-Schutz: Backup-Aufbewahrung löscht nur eigene, per Manifest erkannte Versionsordner; Wiederherstellung nie in geschützte Systemordner und nie stilles Überschreiben; Speicherplatz-Analyse löscht nur in den Papierkorb und nur außerhalb geschützter Pfade (`PathGuard`).
- Details: [THREAT_MODEL.md](THREAT_MODEL.md) T-42 bis T-58.

## Datenschutz
- Weiterhin **keine Telemetrie**. Neue Internetzugriffe nur auf Benutzeraktion:
  - `store.steampowered.com` (`/api/storesearch`, `/api/appdetails`) – nur Spielname bzw. Steam-AppID, auf Knopfdruck;
  - `www.speedtest.ch` – eingebettet, erst nach dem Umschalten auf „speedtest.ch (eingebettet)“;
  - `www.geekbench.com/download/` und die WebView2-Downloadseite von Microsoft – nur als Link im Standardbrowser;
  - Traceroute und Port-Prüfung – nur zu Zielen, die der Benutzer einträgt;
  - Overlay-Ping zu `1.1.1.1` – nur wenn als Ping-Ziel gewählt (Standard: Router, lokal).
- Nur im lokalen Netz: UPnP-Suche (SSDP-Multicast) des Sicherheits-Checks, Wake-on-LAN-Broadcast.
- Die Infobereich-Überwachung und alle Backups arbeiten ausschließlich lokal. Vollständige Liste: [DEPENDENCIES.md](DEPENDENCIES.md#externe-dienste-nur-auf-benutzeraktion) und Handbuch, Abschnitt 1.7.

## Neue Abhängigkeiten
- `Microsoft.Diagnostics.Tracing.TraceEvent` 3.1.13 (MIT) – FPS-Messung über ETW.
- `Microsoft.Web.WebView2` 1.0.2903.40 (BSD-3-Clause) – eingebettete Webansicht (nutzt die in Windows vorhandene Edge-WebView2-Runtime).

## Tests
- **797 Unit-Tests** (v1.2: 316) – neu u. a. Backup-Pfade/Ausschlüsse/Vorlagen/Planer/Aufbewahrung, wbadmin-Fortschritt (DE/EN), Abbild-Ziel, Wiederherstellungskonflikte, Backup-Befehle inkl. Injection und Reparse-Kette, Backup-Engine (Hardlinks, gesperrte Dateien, Abbruch, Löschschutz); Score-Kalibrierung, Ursachen je Komponente, Systemlaufwerk-Wertung, Geekbench; Steam-Parser und -Suche, Online-Schätzung, Benutzer-Spieledatei; Traceroute, Port-Prüfung, DNS, Wake-on-LAN, Sicherheits-Check, Aufgaben-Token, Datenschutz-Tabelle; Frame-Statistik, Warnregeln, Overlay-Einstellungen/Tastenkürzel, Overlay-Daten, manueller Speedtest, Browser-Navigationsregel; Zeitplanung inkl. Sommer-/Winterzeit, Laufzeitberechnung, Speicherplatz-Scan/Duplikate/Löschschutz, Zeitleisten-Korrelation. Übersicht: [TESTING.md](TESTING.md).

## Bekannte Einschränkungen
- **Backup:** ohne Schattenkopien (VSS) – geöffnete Dateien werden übersprungen und gemeldet; Zeitpläne laufen nur, solange ULTIMATE X läuft (minimiert/Infobereich genügt), es wird keine Windows-Aufgabe angelegt; Netzwerkfreigaben weder als Quelle noch als Ziel; keine Cloud-Sicherung und keine eigene Verschlüsselung (BitLocker To Go empfohlen); Hardlink-Versionen nur auf NTFS (sonst Vollkopie).
- **System-Abbild:** wbadmin behält je Platte und PC zuverlässig nur das neueste Abbild; Ziel muss NTFS sein; ein gestartetes Abbild lässt sich aus der App nicht abbrechen (nur die Anzeige). Treiber werden nie automatisch zurückgespielt.
- **Online-Spieldaten:** nur im Steam-Shop gelistete Spiele; Steam begrenzt die Abfragen (HTTP 429); mehrdeutige Namen werden nicht geraten; allgemeine Angaben („Quad-Core“) sind nicht vergleichbar (N/A); keine Raytracing-/Upscaling-Angaben. Alle FPS-Werte bleiben ESTIMATED.
- **Overlay/FPS:** kein Overlay über Spielen im exklusiven Vollbildmodus; Anti-Cheat/eigene Overlays können es überdecken; FPS-Messung benötigt Adminrechte oder „Leistungsprotokollbenutzer“; OpenGL und manche Vulkan-Spiele liefern keine FPS; Takt/Leistung nur mit HWiNFO/LibreHardwareMonitor.
- **Benchmarks:** Score-Skala weiterhin geschätzt; NVMe-Erwartungswert nur bei eindeutig erkennbarer PCIe-Generation; Geekbench nur aus `Program Files\Geekbench 6|5`, Werte werden von Hand eingetragen.
- **Netzwerk-Werkzeuge:** Traceroute per ICMP (in manchen Netzen blockiert); DNS nur feste Anbieter, Windows fragt je Adresse nach Adminrechten; Wake-on-LAN nur im eigenen Subnetz.
- **Sicherheits-Check:** UPnP-„keine Antwort“ ist kein Beweis für „aus“; SMB1 ohne Adminrechte ggf. N/A; ESU für Windows 10 nicht erkennbar; PowerShell 2.0, Gastkonto, Konten und UPnP nur Anzeige.
- **Geplante Aufgaben:** fremde/geschützte Aufgaben ohne Adminrechte ggf. unsichtbar; Aufgabenpfade über ca. 200 Zeichen nicht änderbar.
- **Datenschutz:** Diagnosedaten nur auf „Erforderlich“ begrenzbar; Gruppenrichtlinien können Werte überschreiben.
- **Zeitgesteuerte Aktionen:** höchstens 7 Tage, nur eine Planung; Sperren/Abmelden/Energiesparen nur bei laufender App und ohne Nachholen; beim geplanten Herunterfahren schließt Windows Programme zwangsweise.
- **PC Lifetime:** keine TBW-Werte (N/A); Ereignisprotokoll größenbegrenzt („mindestens“); Schnellstart/Ruhezustand zählen nicht als Start.
- **Speicherplatz-Analyse:** Hardlinks werden je Pfad gezählt; nur online verfügbare OneDrive-Dateien werden nicht gezählt; Platz wird erst nach Leeren des Papierkorbs frei.
- **Zeitleiste:** abhängig von der Größe der Windows-Ereignisprotokolle; Läufe des Backup-Centers erscheinen nicht in der Zeitleiste.
- **Infobereich-Überwachung:** Temperaturregeln nur mit Temperaturquelle; SMART-Regel nutzt die zuletzt gespeicherte SMART-Abfrage.
- **speedtest.ch:** benötigt die WebView2-Runtime; Werte werden nicht ausgelesen, sondern von Hand übernommen; es gelten die Bedingungen von speedtest.ch.
