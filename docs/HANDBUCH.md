# ULTIMATE X Windows Control – Benutzerhandbuch

**Version 1.3.1** · für Windows 10 (ab 1809) und Windows 11, 64-Bit
*Your Complete Windows Control Center*

Dieses Handbuch beschreibt alle Seiten der Anwendung in der Reihenfolge der Navigation sowie die wichtigsten Aufgaben Schritt für Schritt. Die englische Fassung finden Sie in [MANUAL_EN.md](MANUAL_EN.md).

---

## Inhalt

1. [Einleitung](#1-einleitung)
2. [Bedienung im Überblick](#2-bedienung-im-überblick)
3. [Die Seiten der Navigation](#3-die-seiten-der-navigation)
4. [PC sichern und wiederherstellen](#4-pc-sichern-und-wiederherstellen)
5. [Spiele: Can My PC Run It und Online-Anforderungen](#5-spiele-can-my-pc-run-it-und-online-anforderungen)
6. [In-Game-Overlay](#6-in-game-overlay)
7. [Benchmarks und Performance Score](#7-benchmarks-und-performance-score)
8. [Zeitgesteuertes Herunterfahren](#8-zeitgesteuertes-herunterfahren)
9. [Fehlersuche](#9-fehlersuche)
10. [Häufige Fragen und Problembehandlung](#10-häufige-fragen-und-problembehandlung)
11. [Anhang](#11-anhang)

---

# 1 Einleitung

## 1.1 Was ist ULTIMATE X?

ULTIMATE X Windows Control ist eine Windows-Anwendung, die Hardware-Informationen, Überwachung, Diagnose, Reparatur, Netzwerk, Sicherheit, Datensicherung und Spiele-Werkzeuge an einem Ort bündelt. Sie zeigt an, was Windows und die Hardware tatsächlich melden, erklärt es verständlich und bietet sichere Werkzeuge, um Probleme zu beheben.

Drei Grundsätze ziehen sich durch die ganze Anwendung:

| Grundsatz | Bedeutung für Sie |
|---|---|
| **Keine Fantasiewerte** | Was nicht gemessen werden kann, steht als **N/A** da. Es wird nichts geraten. |
| **Schätzungen sind gekennzeichnet** | Werte, die auf Datenbanken oder Erfahrungswerten beruhen (z. B. FPS-Schätzungen, Leistungsklassen, erwartete Werte), tragen den Zusatz **ESTIMATED**. |
| **Least Privilege** | Die App läuft ohne Administratorrechte. Für Aktionen, die Adminrechte brauchen, fragt Windows gezielt über die Benutzerkontensteuerung (**UAC**). ULTIMATE X umgeht UAC nie. |

Sensible Aktionen (z. B. Treiber installieren, sensible Daten anzeigen, Datenschutzmodus des Berichts abschalten) bestätigen Sie zusätzlich mit **Windows Hello** – ist Hello nicht eingerichtet, mit einer ausdrücklichen Bestätigung.

## 1.2 Systemanforderungen

| Anforderung | Wert |
|---|---|
| Betriebssystem | Windows 10 64-Bit ab Version 1809 (Build 17763) oder Windows 11 64-Bit |
| .NET | nicht nötig – die Anwendung bringt ihre Laufzeit mit (self-contained) |
| Rechte | normales Benutzerkonto genügt; Adminrechte nur für einzelne Aktionen per UAC |
| Optional: Microsoft Edge WebView2-Runtime | nur für das eingebettete speedtest.ch (bei Windows 11 normalerweise vorinstalliert) |
| Optional: HWiNFO oder LibreHardwareMonitor | für echte CPU-Kerntemperaturen, CPU-/GPU-Takt und Leistungsaufnahme |
| Optional: Debugging Tools for Windows (Windows SDK) | für die Ursachenanalyse von Absturzabbildern |
| Optional: Geekbench 5 oder 6 | nur, wenn Sie zusätzlich mit Geekbench vergleichen möchten |

## 1.3 Installation (MSI)

1. `UltimateX-WindowsControl-1.3.1-x64.msi` doppelklicken.
2. Die Endbenutzer-Lizenzvereinbarung lesen und annehmen (siehe [Anhang C](#c-lizenz)).
3. Im Setup-Assistenten können Sie den Installationsordner ändern und die **Desktop-Verknüpfung** ab- oder anwählen. Standardordner: `C:\Program Files\ULTIMATE X Windows Control\`.
4. Die Windows-Abfrage (UAC) bestätigen – das Installieren nach *Programme* erfordert Adminrechte.
5. Nach der Installation finden Sie **ULTIMATE X Windows Control** im Startmenü.

**Hinweise**
- Die Startmenü-Verknüpfung wird immer angelegt. Sie ist auch nötig, damit Windows-Benachrichtigungen (Toasts) erscheinen.
- Eine ältere Version wird beim Installieren automatisch ersetzt (Upgrade). Ein **Downgrade** ist gesperrt: Das MSI meldet dann „Eine neuere Version ist bereits installiert“.
- Das MSI prüft, ob ein 64-Bit-Windows ab Build 17763 vorliegt, und bricht sonst mit einer Meldung ab.

## 1.4 Deinstallation

1. Windows-Einstellungen → **Apps** → **Installierte Apps** (Windows 10: *Apps & Features*).
2. **ULTIMATE X Windows Control** → **Deinstallieren**, UAC bestätigen.

Programmdateien, Verknüpfungen und der Ordner `%ProgramData%\UltimateX` werden entfernt. **Ihre persönlichen Daten bleiben erhalten:** Einstellungen, Verläufe und Protokolle unter `%LOCALAPPDATA%\UltimateX`, PDF-Berichte und Spielsitzungen unter `Dokumente\ULTIMATE X` sowie Ihre Sicherungen auf externen Platten. Löschen Sie diese Ordner bei Bedarf selbst (siehe [Anhang B](#b-dateiablageorte)).

## 1.5 Erster Start

Beim ersten Start erscheint das Fenster **Willkommen bei ULTIMATE X**. Es erklärt kurz, was die Anwendung jetzt erkennt (System, Hardware, Netzwerkadapter, installierte Software, Autostart-Einträge, Sicherheits- und Windows-Status), dass sie ohne Adminrechte läuft, dass sensible Daten maskiert werden und dass es keine Telemetrie gibt. Bestätigen Sie mit **OK** – das Dashboard lädt im Hintergrund die ersten Werte.

Beim Start prüft ULTIMATE X außerdem die eigene digitale Signatur. Wurden Programmdateien verändert (ungültige Signatur), startet die App aus Sicherheitsgründen nicht und bittet um eine Neuinstallation.

## 1.6 Sprache und Design

Alles unter **Einstellungen → Darstellung**:

| Einstellung | Möglichkeiten |
|---|---|
| **Farbschema** | System (folgt Windows), Dunkel, Hell |
| **Layout** | Modern Control Center (Standard), Gaming Mode, Technician Mode, Visual PC Map, Simple Mode, Admin / Service Mode |
| **Sprache** | Automatisch (Windows-Anzeigesprache: deutsches Windows → Deutsch, sonst Englisch), Deutsch, English |

Die Sprache wird erst nach einem Neustart der App übernommen – dafür gibt es den Knopf **App neu starten**. Einzelne seltene Meldungen können in der englischen Oberfläche deutsch bleiben.

## 1.7 Datenschutz

**ULTIMATE X sendet keine Telemetrie.** Es gibt keine Nutzungsstatistik, kein Konto und keine Cloud. Alle Daten (Einstellungen, Protokolle, Verläufe, Sicherungen) bleiben auf Ihrem PC.

Internetzugriffe finden **nur statt, wenn Sie eine entsprechende Funktion auslösen** – mit einer Ausnahme, die Sie abschalten können: Die Einstellung *Beim Start im Hintergrund nach Updates suchen (Windows Update, winget, ULTIMATE X)* ist standardmäßig eingeschaltet und sucht etwa 45 Sekunden nach dem Start nach Updates.

### Alle Internetzugriffe auf einen Blick

| Funktion (Seite) | Ziel | Was wird gesendet |
|---|---|---|
| Update-Suche beim Start (Einstellungen, abschaltbar) und Seite **Updates** | Windows Update (Microsoft), winget-Quelle (Microsoft), Update-Manifest des Herausgebers (nur wenn eine Update-Quelle eingetragen ist) | Standardanfragen von Windows/winget; Manifest per HTTPS |
| **Öffentliche IP abfragen** (Netzwerk) | `api.ipify.org`, `api6.ipify.org` | nichts außer der Anfrage selbst |
| **Netzwerk analysieren** (Netzwerk) | `www.microsoft.com` (Erreichbarkeit, DNS-Test), Ping zu `1.1.1.1` und `8.8.8.8` | – |
| **Speedtest starten** (Speedtest, Dashboard, Netzwerk-Benchmark) | `speed.cloudflare.com` | Testdaten, keine Nutzerdaten |
| **speedtest.ch (eingebettet)** (Speedtest) | `www.speedtest.ch` – erst nach dem Umschalten auf diesen Bereich | was die Webseite selbst abfragt |
| Komponententest „Internet“, „DNS“, „Systemzeit“ | `1.1.1.1`, `8.8.8.8`, `www.microsoft.com`, `www.cloudflare.com`, `time.windows.com`, `pool.ntp.org` | – (die Uhrzeit wird nur gelesen) |
| **Herstellerliste laden (IEEE)** (Netzwerk-Scanner) | `standards-oui.ieee.org` | – |
| **Route verfolgen**, **Ports prüfen** (Netzwerk-Werkzeuge) | nur das Ziel, das Sie eintragen | Ping bzw. Verbindungsaufbau, keine Daten |
| Overlay-Zeile „Ping“ mit Ziel *Internet (1.1.1.1, Cloudflare)* | `1.1.1.1` (Standard ist der Router – bleibt im Heimnetz) | Ping alle 3 s, nur solange das Overlay sichtbar ist |
| **Online suchen**, **Fehlende Anforderungen online laden** (Can My PC Run It) | `store.steampowered.com` | nur Spielname bzw. Steam-AppID |
| **Anforderungen im Web suchen**, **FIND GAMES/SOFTWARE FOR MY PC** | öffnet `www.google.com` im Standardbrowser | Suchbegriff aus Hardwarenamen – ULTIMATE X ruft selbst nichts ab |
| **Offizielle Download-Seite öffnen** (Benchmarks → Geekbench) | öffnet `www.geekbench.com/download/` im Browser | – |
| **WebView2-Runtime bei Microsoft herunterladen** (Speedtest) | öffnet `developer.microsoft.com` im Browser | – |
| New PC Setup | winget-Quelle/Microsoft Store, offizielle Herstellerdomains, `officecdn.microsoft.com` (Office Deployment Tool) | Paket-ID; Download direkt vom Hersteller |
| Open Source Security | `api.github.com` (Projektprüfung ClamAV), `database.clamav.net` (Signaturen) | anonyme Anfragen |
| **Ursache ermitteln** (Absturzanalyse, mit Microsoft-Debugger) | Microsoft-Symbolserver `msdl.microsoft.com` | Symbol-Anfragen des Debuggers; das Absturzabbild verlässt den PC nicht |

**Nur im lokalen Netz (kein Internet):** Netzwerk-Scanner (Ping/ARP im eigenen privaten Subnetz), Wake-on-LAN (Broadcast ins eigene Subnetz), UPnP-Abfrage des Sicherheits-Checks (SSDP-Multicast an den Router), Overlay-Ping zum Router.

**Nicht von ULTIMATE X, aber gut zu wissen:** Die kostenlose Geekbench-Version lädt ihre Ergebnisse selbst ins Internet hoch. Die Infobereich-Überwachung greift nie auf das Internet zu.

---

# 2 Bedienung im Überblick

## 2.1 Hauptfenster

- **Navigation (links):** alle Seiten, in Gruppen angeordnet (siehe Kapitel 3).
- **Kopfzeile (HUD):** Live-Werte für CPU, GPU, RAM, Temperaturen und Netzwerk, Uhrzeit sowie Windows-Version; dazu Benutzer und ob die App als *Standardbenutzer* oder *Administrator* läuft.
- **Suchfeld:** öffnet die erste Seite, deren Name oder Stichwort passt (z. B. „smart“, „treiber“, „fps“). Mit **Strg+K** springen Sie direkt hinein, **Enter** sucht.
- **F5** lädt die aktuelle Seite neu.
- **LOCK:** beendet sofort die Security Session; sensible Daten werden wieder maskiert und sensible Aktionen erfordern eine neue Bestätigung.

## 2.2 Statusfarben und Kennzeichnungen

| Anzeige | Bedeutung |
|---|---|
| Grün / OK / PASS / GOOD | in Ordnung |
| Gelb / WARNING | Hinweis, genauer ansehen |
| Rot / FAIL / CRITICAL / Problem | Handlungsbedarf |
| **N/A** / NOT AVAILABLE | kein Messwert verfügbar – es wird nichts geschätzt |
| **ESTIMATED** | Schätzung aus einer lokalen Datenbank oder einem typischen Wert |
| **UAC** an einem Knopf | Windows fragt nach Administratorrechten |

## 2.3 Adminrechte (UAC)

Alle Aktionen mit Adminrechten laufen über das signierte Hilfsprogramm `UltimateX.AdminHelper.exe` mit einer festen Liste erlaubter Befehle. Windows zeigt dafür die UAC-Abfrage. Lehnen Sie sie ab, wird die Aktion **nicht** ausgeführt und die Seite meldet das. Freie Befehlseingaben gibt es nicht.

---

# 3 Die Seiten der Navigation

Die Navigation ist durch Trennlinien in Gruppen gegliedert. Die Tabelle zeigt alle Seiten in ihrer Reihenfolge; die Gruppennamen dienen nur der Orientierung in diesem Handbuch und erscheinen nicht in der App.

| Gruppe | Seiten |
|---|---|
| Start | Dashboard |
| Hardware | Hardware · BIOS / UEFI · Live Monitor · Sensorquellen · Benchmarks · Memory Test · Storage · PC Lifetime · Komponententest |
| Netzwerk | Netzwerk · Speedtest · Netzwerk-Scanner · Netzwerk-Werkzeuge |
| Gaming | Gaming Check · Can My PC Run It · Upgrade Advisor · Overlay |
| Software & Wartung | New PC Setup · Updates · Treiber · Programme · Autostart · Geplante Aufgaben · Prozesse · Speicher bereinigen · Speicherplatz-Analyse · Desktop Organizer |
| Diagnose | Windows Health · Absturzanalyse · Zeitleiste · Windows Logs · Dienste · System Snapshot |
| Datensicherung | Backup-Center |
| Sicherheit | Security Center · Sicherheits-Check · Datenschutz · Open Source Security |
| Werkzeuge | Technician Mode · Tools & Power · Logs · PC-Bericht |
| Einstellungen | Einstellungen |
| Layouts | Gaming Mode · Visual PC Map · Admin / Service Mode |

*Neu in Version 1.3:* PC Lifetime, Netzwerk-Werkzeuge, Overlay, Geplante Aufgaben, Speicherplatz-Analyse, Zeitleiste, Backup-Center, Sicherheits-Check, Datenschutz.

---

## 3.1 Dashboard

**Zweck:** Die Startseite zeigt den Zustand des PCs auf einen Blick und bietet die wichtigsten Aktionen.

**Wichtigste Funktionen**
- **SYSTEM HEALTH** mit **HEALTH SCORE**, PC-Status, Betriebssystem, Betriebszeit, letztem Check und Bewertung.
- Ring-Anzeigen für CPU, GPU, RAM, VRAM, Speicher und Netzwerk, **TEMPERATURES**, **LIVE PERFORMANCE · 2 MIN**, Karten für Security, Windows Health, Netzwerk (letzter Speedtest) und Updates.
- Aktionen: **RUN PC CHECK** (kompletter Systemcheck, nur lesend), **TEST COMPONENTS**, **NETWORK TEST**, **SECURITY CHECK**, **EXPORT REPORT**, **GAME MODE**.
- **HEALTH SCORE · DETAILS:** Start bei 100, jeder Abzug wird mit Grund angezeigt; nicht messbare Werte fließen nicht ein.
- **SCHNELLZUGRIFF:** Windows prüfen, Cleanup, Desktop ordnen, Papierkorb leeren, CMD, PowerShell, PC sperren, Neu starten, Energiesparmodus, Herunterfahren.
- **Rohdaten (TXT/JSON) …** exportiert die erfassten Daten; sensible Daten nur nach Bestätigung.

**Schritt für Schritt – PC komplett prüfen**
1. Dashboard öffnen.
2. **RUN PC CHECK** klicken. Die Prüfschritte erscheinen unter **COMPLETE PC CHECK** mit Status.
3. Ergebnis lesen; Karten führen per Klick zur passenden Detailseite.
4. Optional **EXPORT REPORT**, um einen PDF-Bericht zu erstellen.

**Hinweise:** Der Health Score ist eine Orientierung, keine Garantie. Die SFC/DISM-Prüfung im Check erfordert Adminrechte (UAC).

## 3.2 Hardware

**Zweck:** Vollständige Hardware-Übersicht – nur Komponenten, die Windows tatsächlich meldet.

**Wichtigste Funktionen:** Windows (Edition, Version, Build), CPU, GPU (mit exaktem VRAM), RAM-Module, Mainboard/BIOS/UEFI/TPM/Secure Boot, **Monitore** (Auflösung, Hz, HDR, Skalierung, Hauptmonitor, GPU), **Laufwerke**, Akku-Gesundheit, **Systeminformationen**, **Hardware-Topologie**.

**Schritte**
1. Seite öffnen – die Erkennung läuft automatisch. **Neu erkennen** wiederholt sie.
2. Für Temperatur, Betriebsstunden, Lebensdauer und Fehler der Laufwerke **SMART-Details lesen (Adminrechte)** klicken und UAC bestätigen.

**Hinweise:** Seriennummern, MAC- und IP-Adressen sind maskiert; Anzeigen erfordert eine Bestätigung. **Grenzen:** Belegte/freie PCIe- und M.2-Steckplätze sind über Windows nicht zuverlässig ermittelbar und werden nicht angezeigt.

## 3.3 BIOS / UEFI

**Zweck:** Firmware und Sicherheitsfunktionen anzeigen und erklären – **nur lesend**.

**Funktionen:** UEFI/Legacy, Secure Boot, TPM, Virtualisierung (VT-x/AMD-V), Speicherintegrität/VBS, Hyper-V, Resizable BAR, Alter des BIOS – jeweils mit Erklärung.

**Hinweise:** ULTIMATE X ändert nichts am BIOS/UEFI. Änderungen nur nach Anleitung des Mainboard- bzw. PC-Herstellers. **Grenzen:** Resizable BAR ist ohne Herstellerwerkzeug nicht zuverlässig auslesbar (N/A).

## 3.4 Live Monitor

**Zweck:** Auslastung live verfolgen.

**Funktionen:** CPU, GPU, RAM, VRAM, Datenträger, Download/Upload als Verlauf; Zeitraum **60 Sekunden**, **5 Minuten** oder **15 Minuten**; Liste **Sensoren**.

**Schritte:** Seite öffnen, oben den **Zeitraum** wählen. **Echte CPU-Temperaturen?** erklärt, wie Sie CPU-Kerntemperaturen über HWiNFO oder LibreHardwareMonitor erhalten.

**Grenzen:** Ohne HWiNFO/LibreHardwareMonitor gibt es keine CPU-Kerntemperaturen (ULTIMATE X lädt bewusst keinen Kernel-Treiber). GPU-Temperatur erfordert einen aktuellen Grafiktreiber (WDDM 2.4+).

## 3.5 Sensorquellen

**Zweck:** Zeigt, woher Sensorwerte kommen, und wählt automatisch die beste Quelle.

**Funktionen:** Status der Quellen **HWiNFO** (Shared Memory), **LibreHardwareMonitor** (WMI), **Windows** (Grafikkernel, Leistungsindikatoren) und **ACPI** mit automatischem Failover; **UNIFIED SENSOR MODEL** mit Aktuell/Min/Ø/Max/Quelle/Stand; **SENSOR DETAILS** für den gewählten Sensor; **Min/Ø/Max zurücksetzen**; **Einrichtung** (Hilfe zu HWiNFO/LibreHardwareMonitor).

**Schritte – echte CPU-Temperaturen einrichten**
1. HWiNFO starten und in dessen Einstellungen **Shared Memory Support** aktivieren (oder LibreHardwareMonitor als Administrator starten).
2. In ULTIMATE X die Seite **Sensorquellen** öffnen – die Quelle erscheint als aktiv, die CPU-Werte erhalten ihre Quellenangabe.

**Hinweise:** Statistik (Min/Ø/Max) läuft seit dem Programmstart. ACPI-Thermalzonen werden als „Mainboard“ angezeigt, nie als CPU-Temperatur.

## 3.6 Benchmarks

**Zweck:** Leistung von CPU, GPU, RAM, Datenträger und Netzwerk messen und mit früheren Läufen vergleichen; **ULTIMATE X PERFORMANCE SCORE**; Geekbench-Ergebnisse festhalten.

Ausführlich in [Kapitel 7](#7-benchmarks-und-performance-score).

## 3.7 Memory Test

**Zweck:** Arbeitsspeicher auf Fehler prüfen.

**Funktionen**
- **RAM STABILITY TEST** in drei Stufen:

| Modus | Umfang | Dauer (ca.) |
|---|---|---|
| Quick Memory Test | 25 % des freien Speichers · 3 Muster · 1 Durchlauf | 1–3 Minuten |
| Standard Memory Test | 50 % des freien Speichers · 8 Muster · 2 Durchläufe | 5–15 Minuten |
| Extended Memory Test | 70 % des freien Speichers · 8 Muster · 4 Durchläufe | 15–45 Minuten |

- Fehlerliste **FEHLER (ERROR ADDRESS)** mit Adresse, Muster, Durchlauf, erwartetem/gelesenem Wert und Anzahl gekippter Bits; Ergebnis PASS/WARNING/FAIL.
- **BOOT MEMORY TEST:** plant die Windows-Speicherdiagnose für den nächsten Neustart (prüft nahezu den gesamten RAM außerhalb von Windows) und zeigt danach deren Ergebnisse.
- **RAM-KONFIGURATION:** Typ, Kanäle, aktueller Takt gegenüber Nenntakt, XMP/EXPO-Hinweis.

**Schritte**
1. Modus wählen, **Test starten**. Mit **Abbrechen** jederzeit beenden.
2. Für den gründlichen Test **Boot-Test planen …**, bestätigen und den PC neu starten. Das Ergebnis erscheint nach dem Neustart unter **BOOT MEMORY TEST – ERGEBNISSE**.

**Grenzen:** Der In-App-Test prüft nur freien Speicher im laufenden Windows. Die angezeigten Adressen sind virtuelle Adressen; um ein defektes Modul zu finden, Module einzeln testen.

## 3.8 Storage

**Zweck:** Laufwerksgesundheit (SMART) und Belegung.

**Funktionen:** Zustand **GOOD / WARNING / CRITICAL / N/A** je Laufwerk mit Begründung (Verschleiß, Temperatur, nicht korrigierbare Fehler, Betriebsstunden, Einschaltvorgänge), Belegung je Volume (**VOLUMES**).

**Schritte:** Seite öffnen; für vollständige SMART-Werte **SMART lesen (UAC)** klicken. Die Werte werden zwischengespeichert und auch von *PC Lifetime* und der Infobereich-Überwachung verwendet.

## 3.9 PC Lifetime

**Zweck:** Wie lange und wie oft PC und Laufwerke schon in Betrieb sind – ausschließlich gemessene Werte.

**Schritte**
1. Seite **PC Lifetime** öffnen. Die Werte werden automatisch geladen (das Auswerten des Ereignisprotokolls kann einige Sekunden dauern).
2. Fehlen Laufwerkswerte, **SMART lesen (UAC)** klicken. Es sind dieselben zwischengespeicherten Werte wie auf der Seite **Storage**.

**Was angezeigt wird**
- **Je Laufwerk:** Betriebsstunden, Einschaltvorgänge, Verschleiß (Restlebensdauer), Zustand.
- **PC / Windows:** Installationsdatum von Windows, Laufzeit seit dem letzten Start, Anzahl der Starts und Gesamtlaufzeit laut System-Ereignisprotokoll („mindestens …, seit <Datum>“), unerwartete Abschaltungen.
- **Eigene Laufzeit-Mitschrift:** ULTIMATE X notiert bei jedem Start und Ende sowie alle 5 Minuten einen Zeitstempel (nur lokal).
- **CPU, GPU, RAM:** „N/A – kein Stundenzähler“ – diese Bauteile führen keinen auslesbaren Betriebsstundenzähler.

**Hinweise und Grenzen**
- Geschriebene/gelesene Datenmenge (TBW) stellt Windows nicht herstellerübergreifend bereit (N/A) – dafür das Werkzeug des Laufwerksherstellers nutzen.
- Das Installationsdatum setzt Windows bei großen Funktionsupdates neu.
- Das Ereignisprotokoll ist größenbegrenzt; deshalb „mindestens“.
- Starts aus Schnellstart oder Ruhezustand zählen nicht als Start; Energiesparphasen nicht als Laufzeit. Nach Absturz oder Stromausfall wird nur bis zum letzten Lebenszeichen gezählt.
- Die eigene Mitschrift zählt nur Zeiten, in denen ULTIMATE X lief.

## 3.10 Komponententest

**Zweck:** *System Component Test Center* – über 40 sichere, nicht-destruktive Prüfungen (CPU, RAM, GPU, VRAM, Laufwerke, Mainboard, BIOS/UEFI, TPM, Secure Boot, Netzwerk, Bluetooth, USB, Audio, Kamera, Monitore, Eingabegeräte, Akku, Sensoren, Dienste, Windows Update, Defender, Firewall, Ereignisprotokoll, WMI, Drucker, Systemzeit, WinRE u. a.).

**Funktionen:** **TEST ALL SYSTEM COMPONENTS**, Einzeltests, Status PASS / WARNING / FAIL / NOT AVAILABLE / USER ACTION, **INTERAKTIVE TESTS** (PLAY TEST SOUND, MIKROFON-PEGEL START / STOP, KAMERA PRÜFEN, BILDSCHIRM-FARBTEST, TASTATUR- & MAUSTEST, WINDOWS-TESTSEITE DRUCKEN, WINDOWS-SPEICHERDIAGNOSE), optionaler **SCHREIBTEST**, **TESTVERLAUF**, **Bericht exportieren (TXT/JSON)**.

**Schritte**
1. **TEST ALL SYSTEM COMPONENTS** klicken; **Abbrechen** ist möglich.
2. Befunde mit WARNING/FAIL über **DETAILS** ansehen.
3. Interaktive Tests nur bei Bedarf einzeln starten.

**Hinweise:** Standardmäßig laufen nur lesende Prüfungen. Interaktive Tests nehmen nichts auf und speichern nichts. Kamera und Mikrofon benötigen die Windows-Datenschutzfreigabe für Desktop-Apps. **Grenzen:** Kein GPU-Render-/Belastungstest; der WinRE-Status ist ohne Adminrechte ggf. NOT AVAILABLE.

## 3.11 Netzwerk

**Zweck:** Netzwerkadapter und Verbindung prüfen.

**Funktionen:** **Netzwerkadapter** (IPv4/IPv6, Gateway, DNS, DHCP, MAC – maskiert, **MAC kopieren**), **WLAN** (SSID, Signal, Band, Kanal, Wi-Fi-Standard, Raten, Sicherheit), **Netzwerkdiagnose**, **Ping** (Gateway, 1.1.1.1, 8.8.8.8; Ping (10×) mit Minimum, Maximum, Average, Packet Loss), **DNS testen**, **Öffentliche IP abfragen**.

**Schritte – Verbindungsproblem eingrenzen**
1. **Netzwerk analysieren** klicken. Die Diagnose prüft Schritt für Schritt Adapter, Gateway, DNS und Internet und zeigt, wo es hakt.
2. Bei Bedarf **DNS testen** (A-Abfrage www.microsoft.com).
3. Weiter mit den [Netzwerk-Werkzeugen](#314-netzwerk-werkzeuge) (DNS-Cache leeren, Netzwerk zurücksetzen).

**Hinweise:** Die öffentliche IP wird nur auf Knopfdruck über ipify.org (HTTPS) abgefragt. **Grenzen:** WLAN-Details erfordern ab Windows 11 24H2 die Freigabe „Standortzugriff für Apps“.

## 3.12 Speedtest

**Zweck:** Internetgeschwindigkeit messen und den Verlauf vergleichen.

**Funktionen**
- **Eigener Speedtest (Cloudflare):** Download, Upload, Ping, Jitter, Paketverlust, Tacho; **Letztes Ergebnis**, **Vergleich** der letzten 7 und 30 Tage, **Verlauf** mit Quelle.
- **speedtest.ch (eingebettet)** (neu in 1.3): den Test von speedtest.ch direkt in ULTIMATE X ausführen und das Ergebnis in den Verlauf übernehmen.

**Schritte – eigener Speedtest**
1. **Speedtest starten** klicken (Abbrechen möglich).
2. Ergebnis und Vergleich ablesen.

**Schritte – speedtest.ch**
1. Oben **speedtest.ch (eingebettet)** wählen. Erst jetzt wird www.speedtest.ch geladen.
2. Test auf der Webseite starten.
3. Rechts unter **Ergebnis übernehmen** Download und Upload (Mbit/s), Ping (ms) und optional Jitter (ms) abtippen → **Übernehmen**.
4. Der Eintrag erscheint im **Verlauf** mit der Quelle „speedtest.ch (manuell)“ und fließt in den 7-/30-Tage-Vergleich ein.

**Hinweise**
- Im eingebetteten Fenster sind nur Seiten von speedtest.ch erlaubt. Links zu anderen Seiten (z. B. Werbung), Pop-ups und Downloads werden blockiert und als Hinweis angezeigt.
- ULTIMATE X liest die Webseite **nicht** aus – die Werte übertragen Sie selbst.
- Gespeicherte Kennwörter, Formular-Autofill und Standort-/Kamera-/Mikrofonzugriffe sind abgeschaltet. Browserdaten (z. B. Cookies von speedtest.ch) liegen getrennt von Ihrem Edge-Profil unter `%LOCALAPPDATA%\UltimateX\WebView2`.
- Ohne eingetragenen Jitter zeigt der Verlauf „N/A“.

**Grenzen:** Benötigt die Microsoft Edge WebView2-Runtime. Fehlt sie, erscheint ein Hinweis mit dem Knopf **WebView2-Runtime bei Microsoft herunterladen**. Inhalte und Datenverbrauch des Tests bestimmt speedtest.ch; es gelten dessen Bedingungen.

## 3.13 Netzwerk-Scanner

**Zweck:** Welche Geräte sind in meinem Heimnetz?

**Funktionen:** **Netzwerk scannen** (IP, MAC, Hersteller, Name, Gerätetyp), neue Geräte werden als **NEU** markiert, **Eigene Bezeichnung**, je Gerät **PING** (4 Pings), **LIVE PING** (Dauerping mit Live-Diagramm, STOP beendet), **DETAILS**, **SERVICES** (gängige Dienste prüfen, nur auf Klick), **COPY IP**, **COPY MAC**, **Weboberfläche**; **QUICK PING** für IP, Hostname oder lokalen Namen (z. B. `fritz.box`, `drucker.local`); **Herstellerliste laden (IEEE)**; **Bekannte Geräte vergessen**.

**Schritte**
1. **Netzwerk scannen** klicken (beim ersten Mal Hinweis bestätigen).
2. Ein Gerät wählen und **PING**, **LIVE PING** oder **SERVICES** nutzen.
3. Optional eine eigene Bezeichnung vergeben und **Speichern**.

**Hinweise:** Gescannt wird nur das eigene private Netzwerk (z. B. 192.168.x.x), höchstens 1024 Adressen. Die Port-Prüfung ist kein Angriffswerkzeug. Viele Geräte beantworten keinen Ping – „nicht erreichbar“ heißt nicht „aus“. Der Live-Ping endet beim Seitenwechsel. Die hier gefundenen Geräte stehen bei **Wake-on-LAN** zur Auswahl.

## 3.14 Netzwerk-Werkzeuge

**Zweck:** Kleine Werkzeuge für typische Netzwerkprobleme an einem Ort. Alle Ausgaben erscheinen im Bereich **ERGEBNIS** und lassen sich mit **Kopieren** übernehmen.

### Traceroute (Route verfolgen)
1. Hostnamen oder IP-Adresse eingeben (z. B. `example.com` oder `1.1.1.1`).
2. Optional **Hostnamen der Router auflösen** ausschalten, wenn es schneller gehen soll.
3. **Route verfolgen** klicken. Jede Zeile zeigt eine Station (Hop) mit drei Antwortzeiten.

„*“ bedeutet, dass ein Router nicht antwortet – häufig gewollt und kein Fehler. Höchstens 30 Hops; nach 5 stummen Hops in Folge bricht die Verfolgung ab. Gemessen wird mit Ping (ICMP), den manche Netze blockieren.

### Port-Prüfung
1. Ziel (Hostname oder IP) eingeben.
2. Ports durch Komma getrennt eingeben (z. B. `80, 443, 3389`) oder **Gängige Dienste einsetzen** wählen.
3. **Ports prüfen** klicken. Ergebnis je Port: **offen**, **geschlossen** oder **keine Antwort (gefiltert)**.

Es wird nur ein Verbindungsaufbau versucht, keine Daten gesendet. Bewusst **kein Portscanner**: ein Ziel, höchstens 20 einzelne Ports, keine Bereiche. Prüfen Sie nur eigene Geräte oder Server, für die Sie berechtigt sind.

### DNS-Cache leeren / IP-Adresse neu anfordern
- **DNS-Cache leeren** hilft, wenn eine Webseite nach einer Änderung noch die alte Adresse liefert. Keine Adminrechte nötig.
- **IP-Adresse neu anfordern** fragt beim Router neue Adressen an; die Verbindung ist dabei kurz unterbrochen.

### DNS-Server umstellen
1. **Adapter** wählen (angezeigt: aktuelle DNS-Server, manuell oder automatisch).
2. **Neuer DNS-Server**: Cloudflare (1.1.1.1), Quad9 (9.9.9.9), Google (8.8.8.8) oder **Automatisch (DHCP)**.
3. Optional **Auch IPv6 umstellen**.
4. **DNS-Server übernehmen (UAC)** klicken und bestätigen.

Windows fragt für jede Adresse einzeln nach Adminrechten (2 bis 4 Abfragen). Wird eine abgelehnt, ist die Einstellung ggf. nur teilweise geändert – dann erneut einen Anbieter oder **Automatisch (DHCP)** wählen. Es gibt nur die feste Anbieterliste, keine freie Eingabe.

### Netzwerk zurücksetzen
Setzt Winsock-Katalog und TCP/IP-Einstellungen auf Windows-Standard zurück – hilfreich nach Deinstallation von VPN-, Proxy- oder Sicherheitssoftware.
1. **Netzwerk zurücksetzen …** klicken und die Warnung bestätigen (Adminrechte).
2. Den PC anschließend **neu starten**.

**Achtung:** Manuell eingetragene IP-Adressen gehen verloren; VPN-/Proxy-Software muss ggf. neu eingerichtet werden.

### Wake-on-LAN
1. Ein **Bekanntes Gerät (aus dem Netzwerk-Scanner)** wählen oder die **MAC-Adresse** eintippen (`AA:BB:CC:DD:EE:FF`).
2. **Aufwecken** klicken.

Das „Magic Packet“ geht nur als Broadcast ins eigene Heimnetz. Das Zielgerät muss Wake-on-LAN im BIOS/UEFI und im Netzwerktreiber aktiviert haben und ist meist per Kabel verbunden. Kein Aufwecken über das Internet.

## 3.15 Gaming Check

**Zweck:** Ist dieser PC optimal für Spiele eingerichtet?

**Funktionen:** **GAMING SCORE** und Befunde zu Hardware, Grafiktreiber-Alter, Spielmodus, HAGS, Resizable BAR, Energieprofil, Temperaturen, Hintergrundprozessen, Ping, Secure Boot/TPM (für Anti-Cheat). Knöpfe öffnen die passenden Windows-Einstellungen (**Spielmodus**, **Grafikeinstellungen (HAGS)**, **Energieeinstellungen**) oder **Can My PC Run It**; **Neu prüfen** wiederholt den Check.

**Hinweise:** CPU-/GPU-Klassen sind Schätzungen (ESTIMATED). ULTIMATE X ändert keine Einstellungen automatisch.

## 3.16 Can My PC Run It

**Zweck:** Spiele-Anforderungen mit diesem PC vergleichen, FPS schätzen, installierte Spiele bewerten; seit 1.3 Anforderungen online aus dem Steam-Shop laden.

Ausführlich in [Kapitel 5](#5-spiele-can-my-pc-run-it-und-online-anforderungen).

## 3.17 Upgrade Advisor

**Zweck:** Lohnt sich eine Aufrüstung – und welche?

**Funktionen:** **EMPFOHLENE AUFRÜSTUNG** (nur, wenn eine Messung oder ein Befund es stützt), **OHNE KAUF – ZUERST PRÜFEN** (kostenlose Maßnahmen wie XMP, RAM-Kanäle, Energieprofil, Treiber, PCIe-Steckplatz), **ANALYSE**.

**Schritte:** Zuerst einen Benchmark ausführen (**Benchmarks öffnen**), dann die Seite erneut öffnen. Ohne Benchmark gibt es keine Aufrüstempfehlung.

## 3.18 Overlay

**Zweck:** In-Game-Overlay mit FPS und Leistungswerten, Aufzeichnung von Spielsitzungen und die Überwachung im Hintergrund (Infobereich).

Ausführlich in [Kapitel 6](#6-in-game-overlay).

## 3.19 New PC Setup

**Zweck:** Einen frisch installierten PC sicher einrichten – nur aus offiziellen Quellen.

**Funktionen**
- **VOR DER INSTALLATION – PRÜFUNGEN:** Internet, Speicherplatz, ausstehender Neustart, Windows-Update-Stand, Defender, winget.
- **PROFIL & SOFTWARE:** Profile BASIC / OFFICE / GAMING / DEVELOPER / CUSTOM, **RECOMMENDED FOR THIS PC**, **Empfohlene auswählen**, **Auswahl leeren**; bereits installierte Programme werden erkannt.
- **GRAFIKTREIBER:** erkennt die Grafikkarte. Bei **NVIDIA** sucht ULTIMATE X automatisch den passenden neuesten Game-Ready-Treiber (WHQL) bei NVIDIA und zeigt installierte und neueste Version. Ist er neuer, wird er vorgemerkt; **NVIDIA-Treiber jetzt installieren** oder zusammen mit **Ausgewählte installieren** bzw. **SET UP THIS PC**. Geladen wird nur von `download.nvidia.com` (HTTPS); vor dem Start wird die Signatur „NVIDIA Corporation“ geprüft, dann öffnet sich der NVIDIA-Installer (UAC, „Express“ wählen). **AMD:** Knopf **AMD Treiber-Download** (automatische Erkennung von AMD). **Intel:** „Intel Driver & Support Assistant“ aus der Liste.
- **MICROSOFT OFFICE** (Microsoft 365 Apps, 64-Bit, über das Office Deployment Tool) direkt über der Programmliste, mit Auswahl Word, Excel, PowerPoint, Outlook, OneNote, Access.
- **Programme** u. a.: Browser (Firefox, Chrome, Brave), OneDrive, WinRAR, 7-Zip, VLC, Adobe Reader, LibreOffice, Hardware-Werkzeuge (HWiNFO, CPU-Z, GPU-Z, CrystalDiskInfo/-Mark), Spieleplattformen (Steam, Epic, GOG, EA app, Ubisoft Connect, Battle.net), Peripherie (Logitech G HUB, Corsair iCUE), Werkzeuge (Notepad++, Everything, ShareX, PowerToys), Medien (Spotify, GIMP, Paint.NET, Audacity, HandBrake, OBS), Sicherheit (Bitwarden, KeePassXC, Malwarebytes), TeamViewer und Entwicklerwerkzeuge.
- **Ausgewählte installieren**, **Firefox als Standardbrowser …**, **SET UP THIS PC** (Ablauf vom Hardware-Scan bis zum Bericht; optional ClamAV und Complete Benchmark), **INSTALLATION LOG**, **FIND SOFTWARE FOR MY PC**.

**Schritte**
1. Prüfungen abwarten bzw. **Erneut prüfen**.
2. Profil wählen und **Profil übernehmen** oder einzeln anhaken.
3. **Ausgewählte installieren**; jede Installation erscheint im Protokoll mit Quelle, Version, SHA-256 und Ergebnis.

**Hinweise:** Nichts wird ohne Ihre Auswahl installiert. Vor jeder Installation werden HTTPS, Herstellerdomain, Herausgeber, Version und SHA-256 geprüft; Portale, Mirrors, Filehoster, Cracks und Aktivatoren sind gesperrt. Office wird mit Ihrem Microsoft-Konto bzw. Ihrer gültigen Lizenz aktiviert – keine Lizenzumgehung. WinRAR ist eine Testversion (danach kostenpflichtig). Die NVIDIA-Treibersuche fragt beim Öffnen der Seite `www.nvidia.com` und `gfwsl.geforce.com` ab (nur Kartenmodell, keine persönlichen Daten). **Grenzen:** benötigt winget („App-Installer“ aus dem Microsoft Store); der NVIDIA-Treiber ist ca. 1 GB groß; Studio-Treiber werden nicht angeboten.

## 3.20 Updates

**Zweck:** Treiber-, Windows- und Software-Updates sowie Updates von ULTIMATE X selbst.

**Funktionen:** **Treiber-Updates (Windows Update)**, **Windows-Updates**, **Software-Updates (winget)** mit **Alle aktualisieren**, Bereich **ULTIMATE X** (**Nach App-Update suchen**, **Update installieren**), **Alles neu prüfen**, **Windows Update öffnen**.

**Schritte – Treiber-Update installieren**
1. **Suchen** im Bereich Treiber-Updates.
2. Beim gewünschten Update **Installieren**, mit Windows Hello bestätigen und UAC zulassen. Vorher wird ein Wiederherstellungspunkt angefordert.

**Hinweise:** Nur von Microsoft signierte, für die Hardware-ID freigegebene Treiber. Rollback: Geräte-Manager → Eigenschaften → Treiber → „Vorheriger Treiber“. App-Updates werden nur per HTTPS geladen und erst nach SHA-256- und Signaturprüfung (gleiches Zertifikat) installiert; ohne hinterlegte Update-Quelle gibt es keine App-Update-Prüfung, unsignierte Builds aktualisieren sich nie selbst.

## 3.21 Treiber

**Zweck:** Alle Treiber mit Version, Datum, Anbieter und Signatur – und **DRIVER HEALTH CENTER** (Geräte mit Fehlercode, unbekannte Geräte, unsignierte und sehr alte Treiber, Alter des Grafiktreibers).

**Funktionen:** Filter nach Gerät/Hersteller/Klasse, **Nur wichtige Geräteklassen**, **Nach Treiber-Updates suchen** (Spalte **Neue Version** aus Windows Update), **Windows Update**, **Geräte-Manager**.

**Hinweise:** Treiber-Updates kommen ausschließlich über Windows Update. Für neueste Grafiktreiber nutzen Sie die offiziellen Seiten von NVIDIA, AMD oder Intel.

## 3.22 Programme

**Zweck:** Installierte Programme mit Hersteller, Version, Größe, Installationsdatum und Bereich.

**Funktionen:** Filter, **Installationsordner öffnen**, **Deinstallieren …** (öffnet Windows „Apps & Features“).

**Hinweis:** Deinstallationsbefehle fremder Programme führt ULTIMATE X aus Sicherheitsgründen nie selbst aus.

## 3.23 Autostart

**Zweck:** Programme, die mit Windows starten, aktivieren oder deaktivieren – wie im Task-Manager.

**Funktionen:** Spalten Aktiv, Name, systemrelevant, Herausgeber, Signatur, Quelle, Pfad; **Letztes Backup wiederherstellen**, **Backups öffnen**.

**Schritte:** Haken in der Spalte **Aktiv** setzen oder entfernen. Einträge für alle Benutzer erfordern UAC.

**Hinweise:** Einträge werden nie gelöscht, nur aktiviert/deaktiviert. Vor jeder Änderung wird ein Backup erstellt.

## 3.24 Geplante Aufgaben

**Zweck:** Zeigt alle Aufgaben der Windows-Aufgabenplanung mit Pfad, Status, Auslöser, letztem/nächstem Lauf, Ergebnis, Programm und dessen digitaler Signatur. So erkennen Sie versteckte Autostarts und verdächtige Programme.

**Markierungen**

| Markierung | Bedeutung |
|---|---|
| Microsoft-signiert / Bekannter Herausgeber | Programm ist gültig signiert |
| Windows-Ordner (Katalogsignatur nicht geprüft) | Windows-Systemdatei ohne eingebettete Signatur (üblich) |
| Unsigniert | keine oder ungültige Signatur – genauer ansehen |
| Programm fehlt | Aufgabe zeigt auf eine nicht mehr vorhandene Datei (oft Reste deinstallierter Software) |
| Pfad in Temp/AppData | Programm liegt in einem vom Benutzer beschreibbaren Ordner – typisch für Schadsoftware, aber auch für manche Updater |
| Kein Programm (z. B. COM-Handler) | Aufgabe startet keine Datei |

**Schritte**
1. Seite öffnen; die Liste wird geladen (einige Sekunden).
2. Mit **Filter**, **Microsoft-Aufgaben ausblenden** und **Nur auffällige** eingrenzen.
3. Aufgabe auswählen und **Deaktivieren …** bzw. **Aktivieren …**; Rückfrage bestätigen (Adminrechte).

**Hinweise:** Es wird nie etwas gelöscht. Für Aufgaben unter `\Microsoft\Windows\` erscheint eine zusätzliche Warnung, da das Deaktivieren Wartung, Updates oder Sicherheitsfunktionen beeinträchtigen kann. **Grenzen:** Aufgaben anderer Benutzer oder geschützte Ordner können ohne Adminrechte unsichtbar sein („Nicht lesbare Ordner“). Sehr lange Aufgabenpfade (über ca. 200 Zeichen) lassen sich aus Sicherheitsgründen nicht ändern.

## 3.25 Prozesse

**Zweck:** Laufende Prozesse mit PID, CPU, RAM, Herausgeber, Signatur und Pfad.

**Funktionen:** Filter nach Name, Pfad oder Herausgeber, **Signaturen prüfen**, Ordner öffnen.

**Hinweis:** Nur Anzeige. Eine fehlende Signatur bedeutet nicht, dass ein Prozess schädlich ist – für eine Bewertung Windows-Sicherheit verwenden.

## 3.26 Speicher bereinigen

**Zweck:** Platz schaffen in festen, von Windows als temporär definierten Orten.

**Kategorien:** Temporäre Dateien (Benutzer), Temporäre Dateien (Windows), Miniaturansichten-Cache, Windows-Fehlerberichte, DirectX-Shader-Cache, Papierkorb.

**Schritte:** Kategorien anhaken, Vorschau (Dateianzahl, Größe, Pfade) prüfen, **Ausgewählte bereinigen**. **Neu analysieren** aktualisiert, **Windows-Speicheroptimierung** öffnet die Windows-Einstellung.

**Hinweise:** Dokumente, Desktop, Downloads und Bilder sind nie betroffen. Gesperrte Dateien werden übersprungen. Das Leeren des Papierkorbs erfordert eine Bestätigung.

## 3.27 Speicherplatz-Analyse

**Zweck:** Herausfinden, was den Speicherplatz belegt: größte Ordner, größte Dateien, doppelte Dateien und Dateitypen.

**Schritte**
1. Seite **Speicherplatz-Analyse** öffnen.
2. Ein **Laufwerk** wählen oder mit **Ordner wählen …** einen Ordner aussuchen.
3. **Scan starten**. Fortschritt (Dateien, Ordner, Größe, aktueller Ordner) wird angezeigt; **Abbrechen** ist jederzeit möglich (das bisherige Ergebnis bleibt sichtbar und ist als unvollständig markiert).
4. Ergebnis in vier Registerkarten:
   - **Ordner:** Unterordner nach Größe mit Balken; auf einen Namen klicken, um hineinzugehen; **↑ Nach oben** geht zurück.
   - **Größte Dateien (Top 100)**.
   - **Duplikate:** **Duplikate prüfen (SHA-256)** vergleicht Dateien ab 1 MB mit gleicher Größe zuerst anhand der ersten 64 KB und dann vollständig per SHA-256. Nur wirklich inhaltsgleiche Dateien werden gruppiert.
   - **Dateitypen:** Belegung je Dateiendung.
5. **Explorer** öffnet den Ordner bzw. markiert die Datei im Windows-Explorer.
6. Zum Aufräumen Dateien **ankreuzen** und **Ausgewählte Dateien in den Papierkorb …** wählen.

**Sicherheit**
- Der Scan **liest nur**; es wird nie automatisch gelöscht.
- Gelöscht wird nur, was Sie ankreuzen – immer **in den Papierkorb**.
- In **Windows**, **Programme** und **ProgramData** sowie bei Systemdateien und Laufwerkswurzeln ist das Löschen gesperrt (Kästchen ausgegraut, Grund als Tooltip).
- Von doppelten Dateien bleibt immer mindestens eine Kopie erhalten.

**Grenzen:** Verknüpfungen (Junctions/Symlinks) werden nicht verfolgt; nicht lesbare Ordner werden gezählt und übersprungen. Nur online verfügbare OneDrive-Dateien werden nicht gezählt (sonst würden sie heruntergeladen). Hardlinks (z. B. in `C:\Windows\WinSxS`) werden je Pfad gezählt – Summen können über der tatsächlichen Belegung liegen. Der Platz wird erst frei, wenn der Papierkorb geleert wird.

## 3.28 Desktop Organizer

**Zweck:** Desktopsymbole nach Typ gruppieren und anordnen – es werden nur Positionen geändert.

**Funktionen:** Sortierung A – Z / Z – A, Bildschirmseite (links/rechts beginnen), Abstand zwischen Gruppen, eigene **Regeln** (Kategorien mit Dateiendungen, Reihenfolge = Anordnung), **Vorschau aktualisieren**, **Desktop jetzt organisieren**, **Letztes Desktop Layout wiederherstellen**.

**Hinweise:** Keine Datei wird verschoben, umbenannt oder gelöscht; vor jeder Änderung wird das Layout gesichert. **Voraussetzung:** „Symbole automatisch anordnen“ muss deaktiviert sein (Rechtsklick auf den Desktop → Ansicht).

## 3.29 Windows Health

**Zweck:** Windows prüfen und reparieren, **WINDOWS HEALTH SCORE**.

Ausführlich in [Kapitel 9.3](#93-windows-health-prüfen-und-reparieren).

## 3.30 Absturzanalyse

**Zweck:** Bluescreens, Hardwarefehler und Programmabstürze verständlich erklärt.

Ausführlich in [Kapitel 9.2](#92-absturzanalyse).

## 3.31 Zeitleiste

**Zweck:** „Was ist passiert?“ – Änderungen und Probleme der letzten 7, 30 oder 90 Tage auf einer Zeitachse.

Ausführlich in [Kapitel 9.1](#91-zeitleiste--was-ist-passiert).

## 3.32 Windows Logs

**Zweck:** Ereignisse aus den Windows-Protokollen **System**, **Anwendung** und **Setup**.

**Funktionen:** Protokoll wählen, **inkl. Warnungen**, **Aktualisieren**; Spalten Zeit, Stufe, Quelle, Event-ID, Meldung. Siehe auch [Kapitel 9.4](#94-protokolle).

## 3.33 Dienste

**Zweck:** Windows-Dienste mit Status, Starttyp, Konto und Pfad anzeigen.

**Funktionen:** **Filtern nach Name**, **Nur laufende**, **Aktualisieren**.

**Hinweise:** Nur Anzeige. Orange = Starttyp „Automatisch“, aber nicht gestartet (bei verzögertem oder Trigger-Start normal). Änderungen nehmen Sie in der Windows-Diensteverwaltung vor.

## 3.34 System Snapshot

**Zweck:** Systemstand vor Änderungen sichern und vergleichen.

**Schritte**
1. Bezeichnung eingeben (z. B. „Vor Treiber-Update“) → **Snapshot erstellen**.
2. Änderung durchführen (z. B. Treiber installieren).
3. Zweiten Snapshot erstellen, unter **BEFORE** und **AFTER** auswählen → **Vergleichen**. Die Tabelle zeigt Bereich, Änderung und Eintrag.

**Hinweise:** Erfasst werden Hardware, Treiber, Programme, Windows-Version, Dienste, Autostart, Netzwerk, Ereignisse und Updates – ohne IP-/MAC-Adressen, Seriennummern oder Benutzerdaten. Gespeichert lokal (**Ordner öffnen**).

## 3.35 Backup-Center

**Zweck:** System-Abbild, Datei-Backup mit Versionen, Wiederherstellung, Treiber-Sicherung, Rettungs-USB-Stick.

Ausführlich in [Kapitel 4](#4-pc-sichern-und-wiederherstellen).

## 3.36 Security Center

**Zweck:** Sicherheitsstatus lesen: Defender bzw. anderer Virenschutz, Firewall, Secure Boot, TPM, BitLocker, UAC, App-Integrität.

**Funktionen:** **Windows-Sicherheit öffnen**, **Firewall-Einstellungen**, **Datei prüfen (Signatur & Hash)** mit **Datei auswählen …** (SHA-256, optional **MD5 zusätzlich (Legacy, nicht sicher)**).

**Hinweise:** ULTIMATE X liest diese Werte nur. Defender, Firewall, Secure Boot, TPM und BitLocker werden nie automatisch verändert; BitLocker-Wiederherstellungsschlüssel werden nie gelesen. Die geprüfte Datei wird nur gelesen, nie ausgeführt. Windows-Systemdateien sind oft katalogsigniert und zeigen „Keine eingebettete Signatur“.

## 3.37 Sicherheits-Check

**Zweck:** Ein schneller IT-Audit des PCs mit **SECURITY SCORE** (0–100). Die Prüfung liest nur; geändert wird ausschließlich, wenn Sie eine angebotene Behebung bestätigen.

| Prüfung | Bedeutung |
|---|---|
| **SMB1-Protokoll** | Veraltetes Dateifreigabe-Protokoll (Einfallstor u. a. für WannaCry). Sollte nicht installiert sein. Ohne Adminrechte ggf. „N/A ohne Adminrechte“. |
| **Remotedesktop** | Ist der PC per Remotedesktop erreichbar? Ohne Netzwerkebenen-Authentifizierung (NLA) = FAIL. |
| **Firewall-Profile** | Domäne, Privat und Öffentlich sollten eingeschaltet sein. |
| **Lokale Administratoren** | Warnung bei mehr als zwei Konten oder aktivem integriertem Administrator. |
| **Konten ohne Passwortpflicht** | Aktive lokale Konten, die ohne Passwort sein dürfen. |
| **Gastkonto** | Sollte deaktiviert sein. |
| **UPnP am Router** | Antwortet der Router auf eine UPnP-Suche, erscheint „Router meldet UPnP aktiv“. Keine Antwort beweist nicht, dass UPnP aus ist. |
| **Autorun / AutoPlay** | Ob Windows beim Einstecken von Datenträgern nachfragt. |
| **PowerShell 2.0** | Veraltete PowerShell-Version, die moderne Schutzfunktionen umgeht. |
| **Windows-Version** | Erhält Ihre Windows-Version noch Sicherheitsupdates? (Support-Tabelle, Stand Oktober 2026) |

**Schritte**
1. Seite öffnen – die Prüfung startet automatisch (**Erneut prüfen** wiederholt sie).
2. Befunde mit **WARNING** oder **FAIL** lesen; darunter steht jeweils ein Hinweis.
3. Falls angeboten, unter **BEHEBUNG (NACH BESTÄTIGUNG, UAC)** eine Schaltfläche wählen – **SMB1 deaktivieren …**, **Remotedesktop ausschalten …**, **Firewall überall einschalten …** –, die Rückfrage lesen und bestätigen. Windows fragt nach Adminrechten. Die Ausgabe steht unter **AUSGABE DER LETZTEN BEHEBUNG**.

**Hinweise:** Nach dem Deaktivieren von SMB1 ist ein Neustart nötig; sehr alte NAS-Geräte/Drucker, die nur SMB1 können, sind danach evtl. nicht mehr erreichbar. Sind Sie selbst per Remotedesktop verbunden, verhindert das Ausschalten künftige Verbindungen.

**Grenzen:** Microsoft Defender, Benutzerkontensteuerung (UAC) und Windows Update werden nie verändert. PowerShell 2.0, Gastkonto, Konten und UPnP werden nur angezeigt – ändern Sie diese in Windows bzw. im Router. Erweiterte Sicherheitsupdates (ESU) für Windows 10 kann die App nicht erkennen.

## 3.38 Datenschutz

**Zweck:** Wichtige Windows-Datenschutzeinstellungen einzeln einsehen und umschalten – jederzeit rückgängig.

| Einstellung | Wirkung von „Datenschutz aktivieren“ | Gilt für |
|---|---|---|
| **Werbe-ID** | Apps dürfen keine Werbe-ID nutzen | dieses Konto |
| **Tipps und Vorschläge (Start & Sperrbildschirm)** | keine vorgeschlagenen Inhalte/App-Vorschläge | dieses Konto |
| **Maßgeschneiderte Erfahrungen** | Diagnosedaten nicht für persönliche Tipps/Angebote | dieses Konto |
| **Websuche im Startmenü** | Startmenü-Suche ohne Bing-Websuche | dieses Konto |
| **Aktivitätsverlauf** | Windows sammelt keinen Aktivitätsverlauf | alle Benutzer (Adminrechte) |
| **Diagnosedaten nur „Erforderlich“** | nur die erforderlichen Diagnosedaten an Microsoft | alle Benutzer (Adminrechte) |

**Schritte**
1. Status je Einstellung prüfen: **Datenschutz aktiv**, **Teilweise** oder **Windows-Standard**.
2. **Datenschutz aktivieren** klicken. Bei Einstellungen für alle Benutzer erst die Rückfrage, dann die Windows-Adminabfrage bestätigen.
3. Zum Zurücknehmen **Rückgängig** klicken – es wird der Wert vor der ersten Änderung wiederhergestellt (ohne Sicherung: Windows-Standard).

**Hinweise:** Vor der ersten Änderung merkt sich ULTIMATE X den ursprünglichen Wert in `%LOCALAPPDATA%\UltimateX\privacy-backup.json`. Manche Änderungen wirken erst nach erneuter Anmeldung oder einem Neustart. Auf Firmen-PCs können Gruppenrichtlinien Einstellungen überschreiben. **Windows-Datenschutz öffnen** führt zu den Windows-Einstellungen.

**Grenzen:** Es werden keine Windows-Komponenten oder Apps entfernt, keine Dienste abgeschaltet und keine Hosts-Datei verändert. Diagnosedaten lassen sich nur auf „Erforderlich“ begrenzen, nicht ganz abschalten.

## 3.39 Open Source Security

**Zweck:** ClamAV als optionaler Zusatz-Scanner auf Anforderung – Microsoft Defender bleibt der primäre Echtzeitschutz.

**Funktionen:** **Projekt online prüfen** (aktiv gepflegt, nicht archiviert), **ClamAV installieren**, **Definitions Update**, Scans **Quick Scan**, **Full Scan**, **Custom Scan …**, **USB Scan**, **Download Folder Scan**, optional **Funde in Quarantäne verschieben**, **Quarantäne öffnen**, **SCAN HISTORY**.

**Schritte:** **Projekt online prüfen** → **ClamAV installieren** → **Definitions Update** → Scan wählen.

**Grenzen:** ClamAV hat keinen Echtzeitschutz; ein Full Scan kann Stunden dauern. ULTIMATE X deaktiviert oder verändert Defender nie.

## 3.40 Technician Mode

**Zweck:** Technische Ansicht für Fachleute: Baum mit System, Mainboard, CPU, Memory, GPU und weiteren Geräteklassen, je Gerät Status, Klasse, Hersteller, Device ID, Hardware IDs, Treiberversion/-datum/-anbieter und Signatur; dazu **Letzte Admin-Befehle (Exit Codes & Originalausgabe)**.

**Hinweis:** IDs und Seriennummern werden wie überall maskiert, bis Sie sie nach Bestätigung einblenden.

## 3.41 Tools & Power

**Zweck:** Windows-Werkzeuge und Energieaktionen.

**Funktionen**
- **Power:** Herunterfahren, Neustart, Abmelden (jeweils mit Bestätigung), Energiesparmodus (Standby), Sperren (Sperrbildschirm), **Geplantes Herunterfahren abbrechen**; Countdown vor Neustart/Herunterfahren (einstellbar unter *Einstellungen*).
- **Zeitgesteuert** (neu in 1.3): Herunterfahren, Neustart, Sperren, Abmelden oder Energiesparen zu einem Zeitpunkt – siehe [Kapitel 8](#8-zeitgesteuertes-herunterfahren).
- **Windows-Werkzeuge:** Eingabeaufforderung, CMD (Administrator), PowerShell, PowerShell (Administrator), Windows Terminal, Task-Manager, Geräte-Manager, Datenträgerverwaltung, Systeminformationen, Windows Update, Netzwerkadapter, Firewall-Einstellungen, Windows-Sicherheit, Apps & Features, Speichereinstellungen, Systemwiederherstellung, Standort-Datenschutz, Zuverlässigkeitsverlauf.

**Hinweis:** Werkzeuge mit „UAC“ erfordern eine Bestätigung (Windows Hello, falls eingerichtet) und anschließend die Administratorfreigabe durch Windows.

## 3.42 Logs

**Zweck:** Das eigene Aktions- und Sicherheitsprotokoll von ULTIMATE X.

**Funktionen:** Spalten Zeit, Stufe, Bereich, Aktion, Meldung, Details; **Nur Security-Ereignisse**, **Exportieren (TXT/JSON) …**, **Ordner öffnen**. Siehe [Kapitel 9.4](#94-protokolle).

## 3.43 PC-Bericht

**Zweck:** PDF-Export und Druck mit Vorschau – Datenschutzmodus standardmäßig an.

**Funktionen:** **EXPORT PC REPORT (PDF)**, **PRINT PC REPORT** (Windows-Druckdialog, dort auch „Microsoft Print to PDF“), **Daten neu erheben**, **ABSCHNITTE** (Hardware, Sensoren, Netzwerk, Sicherheit, Treiber, Windows Health, Komponententests, Benchmarks, Absturzanalyse, Bewertung & Empfehlungen), **Komponententest jetzt ausführen**, **Datenschutzmodus (empfohlen)**.

**Schritte**
1. Abschnitte an- oder abwählen; die Vorschau zeigt, was der Bericht enthält.
2. **EXPORT PC REPORT (PDF)** – die Datei landet unter `Dokumente\ULTIMATE X\UltimateX-PC-Bericht-<Datum>.pdf`.

**Hinweise:** Der Datenschutzmodus maskiert IP, MAC, Benutzer- und Computernamen, Seriennummern, Gateway/DNS. Abschalten erfordert eine Bestätigung per Windows Hello. Fehlt ein Abschnitt, ist er abgewählt oder es liegen noch keine Daten vor.

## 3.44 Einstellungen

**Zweck:** Darstellung, Sicherheit, Start und Updates.

| Bereich | Einstellungen |
|---|---|
| **Darstellung** | Farbschema (System/Dunkel/Hell), Layout (sechs Layouts), Sprache (Automatisch/Deutsch/English) + **App neu starten** |
| **Sicherheit** | **Sensible Daten maskieren (MAC, IP, Seriennummern)** – Abschalten erfordert Windows Hello; **Security Session nach erfolgreicher Bestätigung**: Immer erneut fragen, 1, 2 oder 5 Minuten |
| **Start & Power** | Mit Windows starten, Minimiert starten, Countdown vor Neustart/Herunterfahren (10/30/60 s) |
| **Benachrichtigungen & Updates** | Windows-Benachrichtigungen, Wiederherstellungspunkt vor Treiber-Updates und Systemreparaturen, Beim Start im Hintergrund nach Updates suchen, **Update-Quelle für ULTIMATE X** (HTTPS-Manifest des Herausgebers; leer = keine App-Update-Prüfung) |
| **Über** | Version, **Datenordner öffnen** |

**Hinweise:** Die Security Session endet sofort beim Sperren des PCs, beim Benutzerwechsel und über **LOCK**. ULTIMATE X fragt nie selbst nach Passwörtern oder PINs.

## 3.45 Gaming Mode

**Zweck:** Layout mit Live-Leistung auf einen Blick: CPU, GPU, RAM, VRAM, GPU- und CPU-Temperatur und **PERFORMANCEVERLAUF · 2 MIN**.

**Hinweis:** GPU-Last, VRAM und GPU-Temperatur stammen aus dem Windows-Grafikkernel (wie im Task-Manager). CPU-Temperaturen kommen aus ACPI bzw. HWiNFO/LibreHardwareMonitor. Für Werte direkt im Spiel nutzen Sie das [Overlay](#6-in-game-overlay).

## 3.46 Visual PC Map

**Zweck:** Grafische Übersicht der Komponenten – eine Komponente anklicken zeigt Details (u. a. RAM-Kanäle/Takt, Firmware-Status, Performance Score). **Technician Mode öffnen** springt in die technische Ansicht.

## 3.47 Admin / Service Mode

**Zweck:** Alle wichtigen Bereiche als Registerkarten für Service-Einsätze: Hardware, Drivers, Processes, Services, Startup, Network, Storage, Windows Logs, Diagnostics, Security und **Export**.

**Export:** **Systembericht exportieren (TXT/JSON) …** (Windows, CPU, GPU, RAM, Mainboard, Speicher, Netzwerk, Treiber, Updates, Autostart, Health; sensible Daten standardmäßig ausgeblendet, **Sensible Daten einschließen** erfordert Bestätigung) und **Aktions- und Security-Log exportieren (TXT/JSON) …**.

*Hinweis:* Das Layout **Simple Mode** hat keinen eigenen Navigationseintrag. Es zeigt große Kacheln (PC überprüfen, Internet testen, Updates suchen, Speicher bereinigen, Desktop aufräumen, Startup verwalten, Windows prüfen, Neustart, Energiesparen) und **Zur erweiterten Ansicht wechseln**.

---

# 4 PC sichern und wiederherstellen

Das **Backup-Center** bündelt alles, was Sie zum Sichern und Wiederherstellen brauchen – mit Windows-Bordmitteln und ohne Zusatzsoftware. Sie finden es in der Navigation unter **Backup-Center** (Suchwörter z. B. „backup“, „sicherung“, „abbild“, „treiber“).

| Bereich | Wofür |
|---|---|
| Übersicht | letzte Sicherungen, Warnungen, Verlauf, Empfehlungen |
| Datei-Backup | Backup-Profile mit Versionen anlegen, bearbeiten, ausführen |
| Wiederherstellen | Dateien/Ordner aus einer früheren Version zurückholen |
| System-Abbild | komplettes Windows als Abbild sichern; Abbild durchsuchen; Anleitung zum Zurückspielen |
| Treiber sichern | alle installierten Treiber in einen Ordner exportieren |
| Rettungs-USB-Stick | Windows-Wiederherstellungslaufwerk erstellen |

**So sind Sie geschützt**
1. **System-Abbild:** komplette Windows-Installation zurückspielen (nach Plattendefekt, Schadsoftware, missglücktem Update).
2. **Datei-Backup mit Versionen:** einzelne Dateien und Ordner von einem früheren Tag zurückholen.
3. **Rettungs-USB-Stick:** startet einen PC, der nicht mehr hochfährt – zum Zurückspielen des Abbilds.

**Empfehlung – Regel 3-2-1:** drei Kopien Ihrer Daten, auf zwei verschiedenen Datenträgern, eine davon außer Haus. Ziehen Sie die Backup-Platte nach der Sicherung ab – so kann ein Verschlüsselungstrojaner sie nicht erreichen.

**Empfehlung – BitLocker To Go:** Verschlüsseln Sie die Backup-Platte (Explorer → Rechtsklick auf das Laufwerk → „BitLocker aktivieren“, in Windows Pro/Enterprise). Bewahren Sie den Wiederherstellungsschlüssel getrennt von der Platte auf.

## 4.1 Übersicht

Zeigt je Profil die letzte erfolgreiche Sicherung, den Zeitplan und ob die Zielplatte angeschlossen ist, außerdem das letzte System-Abbild und die letzte Treibersicherung. Rote **Warnungen** erscheinen, wenn die letzte erfolgreiche Sicherung eines Profils älter ist als die Warnschwelle (Standard 7 Tage). Dieselbe Warnung zeigt auch die [Infobereich-Überwachung](#64-überwachung-im-hintergrund-infobereich) (Regel *Backup überfällig*). Der **Verlauf** listet die letzten Sicherungen aller Arten mit Ergebnis, Ziel und Dauer.

## 4.2 Datei-Backup mit Versionen

**Zweck:** Persönliche Dateien regelmäßig auf eine externe Festplatte sichern – mit mehreren Versionen, sodass Sie auch den Stand von letzter Woche zurückholen können. Läuft **ohne** Administratorrechte.

**So funktioniert es:** Jede Sicherung ist ein vollständiger Ordner, den Sie auch ohne ULTIMATE X im Explorer öffnen können:

```
E:\UltimateX-Backup\<Profil>\2026-10-14_190000\C\Users\<Name>\Documents\…
```

Unveränderte Dateien werden nicht erneut kopiert, sondern per *Hardlink* aus der vorherigen Version übernommen. Jede Version ist dadurch vollständig, braucht aber nur Platz für die Änderungen. Voraussetzung: Die Zielplatte ist mit **NTFS** formatiert. Bei exFAT/FAT32 wird jede Version komplett kopiert (funktioniert, braucht aber mehr Platz).

### Profil anlegen – Schritt für Schritt
1. Bereich **Datei-Backup** öffnen, **+ Neues Profil** wählen.
2. **Name** vergeben (z. B. „Meine Dateien“).
3. **Ziel** wählen (**Wählen …**): am besten die Wurzel einer externen Festplatte (z. B. `E:\`).
4. **Vorlagen** anhaken: Desktop, Dokumente, Bilder, Videos, Musik, Downloads, OneDrive (falls eingerichtet), Browser-Lesezeichen (nur die Lesezeichen-Dateien von Edge/Chrome und Firefox), Quellcode (`%USERPROFILE%\source\repos`, falls vorhanden). Vorlagen werden bei jedem Lauf neu aufgelöst – ein neues Browserprofil wird z. B. automatisch erfasst.
5. Weitere Ordner per **Ordner hinzufügen …** oder per **Drag & Drop** aus dem Explorer hinzufügen.
6. **Ausschlüsse** prüfen – ein Muster pro Zeile:
   - `node_modules`, `bin` – schließt jeden Ordner/jede Datei mit diesem Namen aus (samt Inhalt),
   - `*.tmp`, `~$*` – Platzhalter `*` (beliebig) und `?` (ein Zeichen),
   - `Cache\*` – Muster mit `\` gelten relativ zum Quellordner; `**\logs\*.log` erfasst `logs` in jeder Tiefe,
   - **+ Vorlage „Programmierung“** fügt `bin, obj, node_modules, .vs, packages, *.tmp, Thumbs.db, ~$*` hinzu. Der Ordner `.git` bleibt bewusst erhalten.
7. **Zeitplan** wählen: **Manuell**, **Täglich** (Uhrzeit), **Wöchentlich** (Tag + Uhrzeit) oder **Beim Anstecken der Zielplatte**.
8. **Aufbewahrung (Versionen)** festlegen (Standard 10).
9. Optional **Änderungen zusätzlich per Hash erkennen (langsamer)** – liest jede Datei, erkennt aber auch Änderungen ohne neues Änderungsdatum.
10. **Profil speichern**, dann **Jetzt sichern**.

**Während der Sicherung** sehen Sie Phase, Fortschritt und Dateizahl; **Abbrechen** ist jederzeit möglich. Eine abgebrochene Version bleibt als „unvollständig“ stehen und wird nie als Basis für Hardlinks genutzt.

**Nach der Sicherung** prüft ULTIMATE X automatisch, ob alle Dateien mit richtiger Größe vorhanden sind, und vergleicht eine Stichprobe von SHA-256-Prüfsummen. Übersprungene Dateien stehen mit Grund unter **ÜBERSPRUNGENE DATEIEN**.

### Hinweise
- **Geöffnete Dateien** (z. B. Outlook-PST, Datenbanken eines laufenden Browsers) können ohne Administratorrechte nicht gelesen werden – Schattenkopien (VSS) brauchen Adminrechte. Diese Dateien werden übersprungen und aufgeführt. Tipp: Outlook und Browser vor der Sicherung schließen.
- **Nur online verfügbare OneDrive-Dateien** werden nicht heruntergeladen und nicht gesichert. Wer alles sichern will, wählt in OneDrive „Immer auf diesem Gerät beibehalten“.
- Verknüpfungen (Junctions/symbolische Links) werden nicht verfolgt; leere Ordner werden nicht gesichert.
- Die Zielplatte wird über die Datei `UltimateX-Backup\target.json` erkannt – auch unter einem anderen Laufwerksbuchstaben. Bitte diese Datei nicht löschen. Steckt unter dem gewohnten Buchstaben eine *andere* Backup-Platte, sichert ULTIMATE X dort aus Sicherheitsgründen nicht.
- Liegen Quelle und Ziel auf demselben Laufwerk, warnt ULTIMATE X: Das schützt nicht vor einem Plattendefekt.

### Aufbewahrung
Nach jeder *erfolgreichen und geprüften* Sicherung werden die ältesten Versionen über der eingestellten Anzahl **endgültig** gelöscht (nicht in den Papierkorb – sonst würde kein Platz frei). Gelöscht werden ausschließlich Versionsordner dieses Profils unterhalb von `UltimateX-Backup`, die ein ULTIMATE-X-Manifest enthalten. Ihre Originaldateien und andere Ordner werden nie angefasst. Wegen der Hardlinks gehen keine Daten verloren, die neuere Versionen noch brauchen.

> **Wichtig – nichts in Versionsordnern ändern:** Unveränderte Dateien sind per Hardlink zwischen Versionen geteilt. Wer eine Datei *in* einem Versionsordner bearbeitet, ändert sie in allen Versionen gleichzeitig. Zum Arbeiten Dateien immer erst herauskopieren oder **Wiederherstellen** nutzen.

### Zeitpläne – ehrlich gesagt
Zeitpläne laufen nur, solange ULTIMATE X geöffnet ist (minimiert oder im Infobereich reicht). Ist die App geschlossen oder der PC aus, wird eine verpasste tägliche/wöchentliche Sicherung beim nächsten Start nachgeholt, sobald die Zielplatte angeschlossen ist. „Beim Anstecken“ prüft jede Minute, ob die Zielplatte angeschlossen wurde, und sichert höchstens einmal pro 20 Stunden. ULTIMATE X legt **keine** Windows-Aufgabe an.

### Profile exportieren/importieren
Die Profile liegen in `%LOCALAPPDATA%\UltimateX\backup-profiles.json`. Mit **Export …** speichern Sie sie in eine Datei, mit **Import …** übernehmen Sie sie (z. B. auf einem neuen PC). Importierte Profile beginnen ohne Verlauf; die Zielplatte wird beim ersten Lauf neu zugeordnet. **Profil entfernen** löscht nur das Profil, nie die Sicherungen.

**Einstellungen** (Bereich Datei-Backup): **SHA-256 je Datei im Manifest speichern** (Standard an) und **Warnen, wenn letzte Sicherung älter als (Tage)**.

## 4.3 Dateien und Ordner wiederherstellen

1. Bereich **Wiederherstellen** öffnen, **Profil** und **Version** (Datum) wählen.
2. Ohne Suchbegriff sehen Sie die gesicherten Quellen (z. B. Dokumente). Mit einem Suchbegriff finden Sie Dateien und Ordner, deren Name ihn enthält.
3. Eine Datei oder einen Ordner in der Liste markieren.
4. **Wiederherstellen nach** wählen:
   - **Originalort** – an den ursprünglichen Speicherort,
   - **Anderer Ordner** – der Eintrag landet mit seinem Namen im gewählten Ordner (empfohlen, wenn Sie erst vergleichen möchten).
5. **Wenn die Datei am Ziel schon existiert:**
   - *Vorhandene Dateien behalten (überspringen)* – Standard, nichts wird überschrieben,
   - *Beide behalten (Sicherung mit neuem Namen)* – die gesicherte Datei wird als „Name (wiederhergestellt Datum Uhrzeit).ext“ abgelegt,
   - *Ersetzen (vorhandene Datei in den Papierkorb)* – die vorhandene Datei wandert in den Papierkorb, dann wird die Sicherung zurückgeschrieben.
6. **Wiederherstellen …** – ULTIMATE X zeigt vorher, wie viele Dateien betroffen sind und wie viele schon existieren. Erst nach Ihrer Bestätigung wird geschrieben.

Alternativ: **Version im Explorer öffnen** und Dateien einfach herauskopieren.

**Grenzen:** In geschützte Systemordner (z. B. `C:\Windows`, `C:\Program Files`) wird nie wiederhergestellt. Fehlt das ursprüngliche Laufwerk, wählen Sie „Anderer Ordner“.

## 4.4 System-Abbild erstellen

**Zweck:** Eine komplette Kopie von Windows inklusive Programmen, Einstellungen und Dateien auf dem Systemlaufwerk sowie der Start-Partitionen (EFI, Wiederherstellung). Damit lässt sich der PC nach einem Plattendefekt, Schadsoftware oder einem missglückten Update vollständig zurücksetzen. Technik: Windows-Sicherung (`wbadmin`), erfordert Administratorrechte (UAC).

1. Externe Festplatte anschließen, Bereich **System-Abbild** öffnen, ggf. **Aktualisieren**.
2. Ziellaufwerk wählen. ULTIMATE X prüft vorab: nicht das Systemlaufwerk, lokal angeschlossen, **NTFS**, freier Platz mindestens belegter Platz des Systemlaufwerks + 10 %. Passt etwas nicht, steht der Grund direkt darunter.
3. **Abbild erstellen …**, bestätigen und die UAC-Abfrage zulassen.
4. Fortschritt und Ausgabe von wbadmin werden angezeigt (Fortschritt je Volume: EFI, System, Wiederherstellung). Dauer: 20 Minuten bis mehrere Stunden.

**Hinweise**
- **Anzeige beenden** stoppt nur die Anzeige – wbadmin läuft mit Administratorrechten weiter. Das Ergebnis sehen Sie später unter **Abbilder anzeigen (wbadmin)**.
- Windows behält auf einer Platte je PC in der Regel nur das **neueste** Abbild zuverlässig. Für mehrere Stände mehrere Platten im Wechsel nutzen.
- Das Abbild liegt in `<Laufwerk>:\WindowsImageBackup\<PC-Name>\`. Den Ordner nicht umbenennen oder verschieben, sonst findet die Windows-Wiederherstellung ihn nicht.

### Abbild durchsuchen (einzelne Dateien herausholen)
1. **VHDX-Dateien suchen** – listet die Abbild-Dateien. Meldet ULTIMATE X „nur Administratoren“, zuerst **Abbild-Ordner im Explorer** öffnen und die Zugriffsabfrage von Windows bestätigen, dann erneut suchen.
2. **Durchsuchen …** – Windows bindet die VHDX als Laufwerk ein.
   **Achtung:** Windows bindet die Datei *beschreibbar* ein. Nur Dateien herauskopieren, nichts ändern oder löschen – sonst kann das Abbild unbrauchbar werden.
3. Danach im Explorer: Rechtsklick auf das eingebundene Laufwerk → **Auswerfen**.

## 4.5 System-Abbild zurückspielen – Schritt für Schritt

> **Achtung:** Das Systemlaufwerk wird vollständig mit dem Stand des Abbilds überschrieben. Dateien, die nach dem Abbild entstanden sind, vorher mit dem Datei-Backup sichern und danach wiederherstellen.

1. Externe Festplatte mit dem Abbild anschließen.
2. **Windows startet noch:** in ULTIMATE X im Bereich System-Abbild **Erweiterter Start …** klicken (öffnet *Einstellungen → System → Wiederherstellung*) und dort bei „Erweiterter Start“ **Jetzt neu starten** wählen.
   **Windows startet nicht mehr:** vom **Rettungs-USB-Stick** (siehe 4.7) oder einem Windows-Installationsmedium starten und dort „Computerreparaturoptionen“ wählen.
3. Im blauen Menü: **Problembehandlung → Erweiterte Optionen → Systemimage-Wiederherstellung** (ggf. zuerst „Weitere Wiederherstellungsoptionen anzeigen“).
4. Konto wählen und Kennwort eingeben.
5. Windows schlägt das neueste Abbild vor; ein älteres wählen Sie über „Systemabbild auswählen“.
6. Den Assistenten fertigstellen und warten, bis die Wiederherstellung abgeschlossen ist.
7. Windows startet im gesicherten Zustand. Anschließend Windows Update ausführen und ggf. neuere Dateien aus dem Datei-Backup wiederherstellen (siehe 4.3).

## 4.6 Treiber sichern

**Zweck:** Alle installierten Treiberpakete (Driver Store) in einen Ordner kopieren – praktisch nach einer Neuinstallation von Windows oder für ältere Geräte ohne Hersteller-Download.

1. Bereich **Treiber sichern** öffnen, Laufwerk wählen (mindestens 1 GB frei).
2. **Treiber sichern …** und die UAC-Abfrage bestätigen.
3. Die Treiber landen in `<Laufwerk>:\UltimateX-Backup\Treiber\<JJJJMMTT-HHMMSS>\`, je Paket ein Unterordner. Die Liste **GESICHERTE TREIBER** zeigt Paket und INF; frühere Sicherungen stehen unter **VORHANDENE SICHERUNGEN AUF DIESEM LAUFWERK** und lassen sich **Öffnen**.

**Zurückspielen** – ULTIMATE X macht das bewusst nie automatisch:
1. Startmenü → „Terminal (Administrator)“ öffnen.
2. Eingeben (Ordner anpassen):
   `pnputil /add-driver "E:\UltimateX-Backup\Treiber\20261014-153000\*.inf" /subdirs /install`
3. Neu starten. Für einzelne Geräte alternativ im Geräte-Manager: Gerät → Treiber aktualisieren → „Auf meinem Computer suchen“ → Ordner wählen, „Unterordner einbeziehen“.

**Hinweis:** Nur Treiber zurückspielen, die zu diesem PC passen. Grafiktreiber besser frisch vom Hersteller installieren.

## 4.7 Rettungs-USB-Stick

**Zweck:** Ein Wiederherstellungslaufwerk startet einen PC, der nicht mehr hochfährt, in die Windows-Wiederherstellung – von dort lässt sich das System-Abbild zurückspielen.

1. USB-Stick mit mindestens 16 GB bereithalten (mit Option „Systemdateien sichern“: 32 GB). **Alle Daten auf dem Stick werden gelöscht.**
2. **Windows-Assistent starten …** und die UAC-Abfrage bestätigen. Der Windows-Assistent „Wiederherstellungslaufwerk“ führt durch die Erstellung.
3. Stick beschriften und sicher aufbewahren. Nach großen Windows-Funktionsupdates neu erstellen.

**Vom Stick starten:** Beim Einschalten die Boot-Menü-Taste drücken (oft F12, F8, F11 oder Esc) und den Stick wählen.

## 4.8 Grenzen auf einen Blick

- Datei-Backup ohne Schattenkopien: geöffnete Dateien werden übersprungen (im Bericht aufgeführt).
- Zeitpläne nur, solange ULTIMATE X läuft.
- Netzwerkfreigaben (`\\Server\…`) sind weder als Quelle noch als Ziel vorgesehen.
- System-Abbild über Windows-Sicherung (wbadmin): je Platte und PC zuverlässig nur das neueste Abbild; Ziel muss NTFS sein.
- Ein gestartetes System-Abbild kann aus ULTIMATE X heraus nicht abgebrochen werden (nur die Anzeige).
- Keine Cloud-Sicherung, keine Verschlüsselung durch ULTIMATE X selbst – dafür BitLocker To Go verwenden.

---

# 5 Spiele: Can My PC Run It und Online-Anforderungen

## 5.1 Überblick

Die Seite **Can My PC Run It** vergleicht die veröffentlichten Systemanforderungen eines Spiels mit Ihrem PC (Spalten **MEIN PC**, **MINIMUM**, **RECOMMENDED**, **Status**), gibt eine Bewertung von **EXCELLENT** über VERY GOOD, GOOD, PLAYABLE, LIMITED bis **NOT RECOMMENDED** und schätzt die Bildrate (**FPS ESTIMATION (ESTIMATED)**) je Auflösung, Qualitätsstufe (Low/Medium/High/Ultra), Raytracing und Upscaler.

Die eingebaute Datenbank enthält die Anforderungen von 42 bekannten Spielen. Seit Version 1.3 kann ULTIMATE X für alle anderen Spiele die Anforderungen (Minimum und Empfohlen) aus dem öffentlichen **Steam-Shop** laden – ohne Steam-Konto und ohne Anmeldung. Die geladenen Daten werden lokal gespeichert und stehen danach auch offline zur Verfügung.

## 5.2 Ein Spiel prüfen

1. **Can My PC Run It** öffnen. Oben steht **MEIN PC** mit Ihrer Hardware.
2. Den Spielnamen in das Suchfeld eingeben (z. B. „Hogwarts Legacy“).
3. Ist das Spiel bereits vorhanden (eingebaut oder früher gespeichert), wird es ausgewählt und sofort bewertet – es wird nichts abgerufen.
4. Bewertung, Vergleichstabelle und FPS-Schätzung lesen.

## 5.3 Ein Spiel online suchen (Steam)

1. Spielnamen eingeben.
2. **Online suchen** anklicken oder **Enter** drücken.
3. ULTIMATE X sucht das Spiel im Steam-Shop, wählt den passendsten Treffer, lädt die Anforderungen, speichert sie und bewertet Ihren PC sofort.
4. Unter dem Spielnamen steht die **Quelle**: „eingebaute Datenbank …“ oder „Steam (AppID …, abgerufen am TT.MM.JJJJ)“.

## 5.4 Fehlende Anforderungen für installierte Spiele laden

Unter **INSTALLIERTE SPIELE** listet ULTIMATE X Spiele aus Steam, Epic, Xbox, GOG, EA und Ubisoft mit Launcher, Größe, **PC Suitability**, **Erwartete Performance**, **Möglicher Engpass** und **Empfehlung**.

1. **Fehlende Anforderungen online laden** anklicken.
2. Die Rückfrage bestätigen. Es werden nur Spiele **ohne** Daten abgefragt – Steam-Spiele über ihre Steam-AppID, Spiele anderer Launcher über ihren Namen.
3. Der Fortschritt wird angezeigt („Lade 3/12: …“). **Abbrechen** beendet den Vorgang jederzeit.
4. Danach wird die Liste automatisch neu bewertet.

Gelesen werden nur lokale Bibliotheksdateien (Namen, Installationsort) – keine Kontodaten oder Passwörter.

## 5.5 Gespeichertes Spiel entfernen

Ein online gespeichertes Spiel auswählen und **Gespeichertes Spiel entfernen** anklicken (mit Rückfrage). Eingebaute Spiele können nicht entfernt werden. Die Daten lassen sich jederzeit erneut laden.

## 5.6 Weitere Funktionen

- **Anforderungen im Web suchen** öffnet eine Websuche im Standardbrowser.
- **FIND GAMES FOR MY PC** bietet Suchanfragen aus Ihrer Hardware an; ein Klick öffnet die Suche im Standardbrowser. ULTIMATE X ruft dabei selbst nichts ab.

## 5.7 Hinweise

- **Datenschutz:** Gesendet wird nur der Spielname (Suche) bzw. die Steam-AppID an `store.steampowered.com` – keine Hardware-, Konto- oder Gerätedaten. Ein Online-Abruf findet nur auf Knopfdruck statt.
- **Speicherort:** `%LOCALAPPDATA%\UltimateX\games-user.json`. Eine beschädigte Datei wird nicht geladen; es wird eine Sicherungskopie (`games-user.json.broken`) angelegt.
- **Nichts geraten:** Übernommen werden nur ausdrücklich genannte Werte (CPU, GPU, RAM, VRAM, Speicher, DirectX, SSD). Unbekannte Angaben erscheinen als **N/A**. Allgemeine Angaben wie „Quad-Core-Prozessor“ oder „DirectX-11-Grafikkarte“ können nicht verglichen werden – die Bewertung lautet dann ggf. N/A.
- **SSD:** Beim Minimum gilt eine SSD nur als Pflicht, wenn Steam sie ausdrücklich verlangt („SSD required“); bei der Empfehlung genügt die Erwähnung („SSD preferred“).
- **Raytracing/Upscaling:** Steam nennt dazu keine Angaben. Für online geladene Spiele zeigt die FPS-Schätzung mit Raytracing oder DLSS/FSR/XeSS daher „N/A – Keine Angabe zu Raytracing/Upscaling in den Online-Daten“.
- **Alle FPS-Werte sind Schätzungen (ESTIMATED)** auf Basis veröffentlichter Anforderungen und geschätzter Leistungsklassen – echte Werte hängen von Spielszene, Patch und Treiber ab. Unbekannte oder mobile Grafikkarten ergeben N/A.

## 5.8 Grenzen

- Nur Spiele, die im Steam-Shop gelistet sind (z. B. Valorant oder Fortnite sind dort nicht verfügbar).
- Steam begrenzt die Anzahl der Abfragen. Bei mehreren Spielen wartet ULTIMATE X 1,5 Sekunden zwischen den Abrufen; meldet Steam „zu viele Anfragen“ (HTTP 429), wird der Vorgang beendet – bitte einige Minuten später erneut versuchen.
- Ist der Spielname mehrdeutig, wird kein Spiel geraten; die gefundenen Namen werden angezeigt, damit Sie die Suche präzisieren können.
- Herstellerangaben können sich mit Updates ändern; die Daten tragen deshalb das Abrufdatum.

---

# 6 In-Game-Overlay

## 6.1 Overlay einrichten

**Zweck:** Leistungswerte direkt über dem Spiel – FPS, Frametime, 1 % Low, CPU-/GPU-Auslastung, Temperaturen, Takt, GPU-Leistung, RAM, VRAM, Ping und Uhrzeit. Das Overlay ist **durchklickbar**: Maus und Tastatur bleiben beim Spiel; es erfasst keine Eingaben.

1. Seite **Overlay** öffnen. Oben steht, ob das Overlay ein- oder ausgeblendet ist; **Overlay ein/aus** schaltet um.
2. Unter **ANGEZEIGTE WERTE** die gewünschten Zeilen anhaken.
3. Unter **DARSTELLUNG & POSITION** wählen:
   - **Monitor**,
   - **Ecke** (Oben links, Oben rechts, Unten links, Unten rechts),
   - **Schriftgröße**, **Deckkraft**, **Hintergrund** (Transparenz),
   - **Ping-Ziel**: *Router (Standardgateway, lokal)* oder *Internet (1.1.1.1, Cloudflare)*,
   - **Tastenkürzel** (Standard **Strg+Umschalt+O**),
   - optional **Overlay beim Programmstart einblenden**.
4. **Übernehmen** klicken. Die **VORSCHAU (LIVE)** zeigt, wie das Overlay aussieht.
5. Im Spiel das Overlay mit dem Tastenkürzel ein- und ausblenden.

**Profile:** Unter **PROFILE** speichern Sie die aktuelle Darstellung unter einem Namen (**Aktuelle Darstellung speichern als** → **Speichern**), z. B. „Minimal“ nur mit FPS oder „Diagnose“ mit allen Werten. **Profil laden** stellt sie wieder her, **Profil löschen** entfernt sie. Ein Profil enthält Werte, Ecke, Schriftgröße und Transparenz.

**Hinweise**
- Es werden nur Werte angezeigt, für die eine Messquelle vorhanden ist. Fehlt eine Quelle, entfällt die Zeile. CPU-/GPU-Takt und GPU-Leistung liefern nur Sensorquellen wie HWiNFO oder LibreHardwareMonitor (siehe *Sensorquellen*).
- **Ping:** Gepingt wird nur, solange das Overlay sichtbar und die Zeile „Ping“ aktiv ist (alle 3 Sekunden).
- **Tastenkürzel:** mindestens **Strg**, **Alt** oder **Win** plus eine Taste (A–Z, 0–9, F1–F24, Einfg, Entf, Pos1, Ende, BildAuf, BildAb, Pause). Beispiele: Strg+Umschalt+O, Alt+F10, Strg+Alt+P. Ist die Kombination schon belegt, erscheint ein Hinweis.
- Die Vorschau auf der Seite misst keine FPS von ULTIMATE X selbst – FPS beziehen sich immer auf das Programm im Vordergrund.
- Einstellungen: `%LOCALAPPDATA%\UltimateX\overlay.json`.

**Grenzen**
- Spiele im **exklusiven Vollbildmodus** zeigen kein fremdes Fenster darüber. Stellen Sie im Spiel „Randloses Fenster“ bzw. „Vollbild (Fenster)“ ein.
- Manche Spiele mit Anti-Cheat oder eigenem Overlay können Fenster anderer Programme überdecken.

## 6.2 FPS-Messung (ohne Eingriff ins Spiel)

ULTIMATE X misst FPS, Frametime und 1 % Low des Programms im Vordergrund, **ohne Code in das Spiel zu laden** (Anti-Cheat-neutral). Dazu liest es die Bildausgabe-Ereignisse von Windows (Ereignisablaufverfolgung/ETW, Anbieter „Microsoft-Windows-DXGI“ und „Microsoft-Windows-D3D9“) – dasselbe Verfahren wie Intels PresentMon.

| Wert | Bedeutung |
|---|---|
| **FPS** | Bilder der letzten Sekunde |
| **Frametime** | mittlere Zeit zwischen zwei Bildern in der letzten Sekunde |
| **1 % Low** | Bildrate der langsamsten 1 % aller Bilder der letzten 10 Sekunden (zeigt Ruckler) |

**Berechtigung:** Windows erlaubt diese Messung nur Administratoren oder Mitgliedern der Gruppe **„Leistungsprotokollbenutzer“** (engl. *Performance Log Users*). Fehlt die Berechtigung, zeigt das Overlay **FPS: N/A** und die Seite *Overlay* einen Hinweis mit dem Knopf **Anleitung anzeigen**. ULTIMATE X ändert Ihr Konto nicht selbst.

So fügen Sie Ihr Konto der Gruppe hinzu (einmalig, Administrator nötig):
1. `lusrmgr.msc` öffnen (Windows Pro/Enterprise) → *Gruppen* → *Leistungsprotokollbenutzer* → *Hinzufügen* → Benutzernamen eintragen. Alternativ in einer Eingabeaufforderung als Administrator:
   `net localgroup "Leistungsprotokollbenutzer" IhrBenutzername /add` (englisches Windows: `"Performance Log Users"`).
2. Abmelden und wieder anmelden.
3. ULTIMATE X neu starten.

**Grenzen**
- Gemessen wird immer das Programm im Vordergrund. Wechseln Sie das Fenster, beginnt die Statistik neu.
- OpenGL- und manche Vulkan-Spiele melden keine Direct3D-Bildausgabe – dann bleibt die FPS-Zeile ausgeblendet.
- Die Messung läuft nur, solange sie gebraucht wird (Overlay mit FPS-Zeile sichtbar, Aufzeichnung aktiv oder Seite *Overlay* geöffnet).

## 6.3 Spiel-Sitzung aufzeichnen

1. Seite **Overlay** → **Aufzeichnung starten**.
2. Spielen.
3. **Aufzeichnung beenden**. Die neue Sitzung erscheint in der Liste (**Liste aktualisieren**); ein Klick zeigt FPS-Verlauf, Temperaturverlauf (CPU/GPU) und eine Zusammenfassung (Dauer, Ø FPS, Minimum, Ø 1 % Low, Höchsttemperaturen, Spiel).

**Hinweise:** Jede Sekunde werden FPS, Frametime, 1 % Low, Auslastung und Temperaturen geschrieben – als CSV-Datei unter `Dokumente\ULTIMATE X\Sessions` (Spalten mit „;“ getrennt, Zahlen mit Punkt; z. B. in Excel zu öffnen). Leere Felder bedeuten: kein Messwert. Sitzungsdateien werden nie automatisch gelöscht.

## 6.4 Überwachung im Hintergrund (Infobereich)

**Zweck:** ULTIMATE X behält Ihren PC im Blick, auch wenn das Fenster minimiert oder geschlossen ist, und meldet Probleme per Windows-Benachrichtigung.

| Regel | Standard |
|---|---|
| CPU-Temperatur über Grenzwert | 90 °C, länger als 30 Sekunden ununterbrochen |
| GPU-Temperatur über Grenzwert | 85 °C, länger als 30 Sekunden ununterbrochen |
| Laufwerk belegt | ab 90 % Warnung, ab 95 % kritisch |
| SMART-/Laufwerkswarnung | aus der letzten SMART-Abfrage und dem Windows-Laufwerksstatus |
| Neuer Bluescreen | seit der letzten Prüfung |
| Virenschutz ausgeschaltet | Defender-Echtzeitschutz aus und kein anderer aktiver Virenschutz |
| Backup überfällig | Datei-Backup-Profil älter als die Warnschwelle des Backup-Centers |

**Schritte**
1. Seite **Overlay** → Abschnitt **ÜBERWACHUNG IM HINTERGRUND (INFOBEREICH)**.
2. **Symbol im Infobereich und Überwachung aktiv** einschalten (Standard an).
3. Regeln an-/abwählen, Grenzwerte, **Prüfintervall** (Standard 5 Minuten) und **Wiederholungssperre** (Standard 30 Minuten je Regel) einstellen → **Übernehmen**. **Jetzt prüfen** prüft sofort.
4. Optional **Beim Schließen in den Infobereich minimieren** – dann läuft ULTIMATE X nach dem Schließen des Fensters im Infobereich weiter.

**Symbol-Menü** (Rechtsklick auf das ULTIMATE-X-Symbol): **Öffnen** · **Overlay ein/aus** · **Überwachung pausieren** · **Beenden**. Doppelklick öffnet das Fenster.

**Hinweise**
- Die Überwachung liest nur – sie ändert nichts und greift nicht auf das Internet zu.
- Ist „Benachrichtigungen“ in den Einstellungen aus, gibt es keine Meldungen; die Liste **LETZTE MELDUNGEN** auf der Seite zeigt sie trotzdem.
- Dieselbe Meldung wiederholt sich frühestens nach der Wiederholungssperre. Gleichartige Windows-Benachrichtigungen erscheinen höchstens einmal pro Stunde; weitere als Sprechblase am Symbol.
- Einstellungen: `%LOCALAPPDATA%\UltimateX\background-monitor.json`.

**Grenzen**
- Temperaturregeln brauchen eine Temperaturquelle (siehe *Sensorquellen*). Ohne Quelle wird nichts gemeldet.
- Die SMART-Regel nutzt die zuletzt gespeicherte SMART-Abfrage; für aktuelle Werte die Abfrage auf der Seite *Storage* erneut starten.

---

# 7 Benchmarks und Performance Score

## 7.1 Benchmarks ausführen

**Zweck:** Leistung von Prozessor, Grafikkarte, Arbeitsspeicher, Datenträger und Netzwerk messen, bewerten und mit früheren Läufen vergleichen. Alle eigenen Tests laufen lokal, ohne Zusatzsoftware und ohne Internet (Ausnahme: optionaler Speedtest). Ergebnisse landen im **Verlauf** (lokale Datenbank).

| Karte | Was gemessen wird | Optionen |
|---|---|---|
| **CPU testen** | Ganzzahl, Gleitkomma, Krypto, Kompression, Dekompression, AES, Rendering – auf einem und allen Kernen | **Dauerlast (2 min, Throttling-Erkennung)** |
| **GPU testen** | Direct3D-11-Compute: Rechenleistung (GFLOPS), Speicherbandbreite | **Stabilitätstest (60 s)** |
| **RAM testen** | Lesen, Schreiben, Kopieren, Latenz | – |
| **Datenträger testen** | sequentiell und 4K am Windows-Cache vorbei, IOPS, Zugriffszeit | Quick (256 MB), Normal (1 GB), Extended (4 GB – nur wenn nötig) |
| **Netzwerk testen** | Ping, Jitter, Paketverlust, DNS, Gateway, Verbindung, WLAN-Signal | **inkl. Speedtest (Download/Upload)** |

**Schritte**
1. Andere Programme möglichst schließen.
2. Entweder **START COMPLETE BENCHMARK** (alles nacheinander: CPU → GPU → RAM → SSD/HDD → Network → Cooling → Stability; ca. 5–10 Minuten; optional **inkl. Internet-Speedtest**) oder einzelne Tests über die Karten starten. **Abbrechen** ist möglich.
3. Ergebnis ablesen: Punktzahl je Karte, Vergleich zum vorherigen Lauf, **ULTIMATE X PERFORMANCE SCORE** oben, **BOTTLENECK ANALYZER** (CPU-/GPU-/RAM-/VRAM-/Storage-/PCIe-Limit, Throttling, Energieprofil, Hintergrundlast).

## 7.2 ULTIMATE X PERFORMANCE SCORE – so ist die Skala zu lesen

- 0–100 Punkte je Komponente (CPU, GPU, RAM, SSD, Netzwerk, Kühlung) und ein gewichteter Gesamtwert:

| Komponente | CPU | GPU | RAM | SSD | Netzwerk | Kühlung |
|---|---|---|---|---|---|---|
| Gewicht | 25 % | 30 % | 15 % | 15 % | 5 % | 10 % |

- **100 Punkte ≈ aktuelle Spitzenklasse für Endanwender (Stand 2026).** Die Skala ist logarithmisch: doppelte Leistung bringt immer gleich viele Punkte. Wer schneller als die Referenz ist, bleibt bei 100.
- **Einstufungen:**

| Einstufung | Gesamtwert |
|---|---|
| ENTRY LEVEL | unter 35 |
| STANDARD | 35–54 |
| GAMING | 55–71 |
| HIGH-END GAMING | 72–86 |
| ENTHUSIAST | ab 87 |

  Eine Einstufung gibt es nur, wenn CPU **und** GPU gemessen wurden. Fällt ein Teil aus, zählt er nicht in die Wertung.
- **Beispiele (geschätzt):** Core i9-13900K (Spitzenklasse 2022) etwa 84, RTX 4080 etwa 81, DDR5-6000 mit 106 ns Latenz etwa 67, NVMe-SSD (PCIe 4.0, ca. 7 000 MB/s) etwa 90. Eine RTX 5090 liegt bei 100.
- Die Referenzwerte sind Schätzungen (**ESTIMATED**) für die Messmethoden von ULTIMATE X – sie gelten nicht für fremde Benchmarks.
- **Seit Version 1.3 neu kalibriert:** In 1.2 stand die CPU wegen falscher Referenzwerte immer bei 100/100. Die gespeicherten Messwerte älterer Läufe bleiben gültig – nur ihre Bewertung ändert sich. Ihr Gesamtwert kann nach dem Update daher niedriger ausfallen, ohne dass Ihr PC langsamer geworden ist.

## 7.3 Datenträger: welches Laufwerk zählt?

- Für die SSD-Wertung im Performance Score und für die Bottleneck-Analyse zählt **immer das Windows-Systemlaufwerk** (meist `C:\`) – nicht der zuletzt getestete Datenträger.
- Sie können trotzdem jedes lokale Laufwerk testen (z. B. `D:\`). Die Datenträger-Karte zeigt dann den letzten Test und darunter deutlich, welcher Lauf des Systemlaufwerks in den Score eingeht. Der Prozentvergleich bezieht sich nur auf frühere Tests desselben Laufwerks.
- Der Complete Benchmark testet immer das Systemlaufwerk.
- Wurde das Systemlaufwerk noch nie getestet, ist die SSD-Wertung „N/A“.

## 7.4 Expected vs Actual Performance

Der Bereich **EXPECTED VS ACTUAL PERFORMANCE** vergleicht gemessene Werte (*Measured*) mit typischen Werten (*Expected*):

| Komponente | Erwartungswert |
|---|---|
| GPU | Herstellerangabe (TFLOPS) × typische Auslastung – nur für Grafikkarten aus der lokalen Datenbank |
| RAM | theoretische Bandbreite aus Kanälen und Takt, davon ca. 75 % als erreichbar |
| Datenträger (Systemlaufwerk) | typischer Wert der Schnittstelle: NVMe PCIe 3.0 ≈ 3 000 MB/s, PCIe 4.0 ≈ 6 500 MB/s, PCIe 5.0 ≈ 10 000 MB/s, SATA-SSD ≈ 500 MB/s, Festplatte ≈ 150 MB/s. Bei NVMe nur, wenn die PCIe-Generation eindeutig ermittelt werden kann (z. B. eine einzige NVMe-SSD), sonst „N/A“ |
| CPU | keine geprüften Referenzwerte – nur Vergleich mit eigenen früheren Läufen |

Alle Erwartungswerte sind Schätzungen (**ESTIMATED**).

Liegt ein Wert mehr als 10 % unter der Erwartung, nennt ULTIMATE X **mögliche Ursachen – passend zur Komponente**:
- **GPU:** Temperatur, Power Limit, PCIe-Konfiguration, Treiber, Hintergrundprogramme, CPU-Limit, Energieplan
- **RAM:** XMP/EXPO aus, Single Channel, Hintergrundprogramme, Energieplan
- **Datenträger:** falsches Laufwerk getestet, SATA statt NVMe, Laufwerk fast voll, Laufwerkstemperatur, Hintergrundprogramme
- **CPU:** Temperatur, Power Limit, Energieplan, Hintergrundprogramme

Wird nichts Konkretes erkannt, erscheint die allgemeine Prüfliste der jeweiligen Komponente.

## 7.5 Hinweise und Grenzen

- Werte, die kein Sensor liefert (z. B. Leistungsaufnahme), erscheinen nicht bzw. als N/A – es wird nichts geschätzt. Ohne HWiNFO/LibreHardwareMonitor sind Kühlungs-Score und Throttling-Erkennung eingeschränkt.
- Ein 3D-Spieletest (FPS, 1 % Low) ist nicht enthalten. Für echte FPS im Spiel nutzen Sie das [Overlay](#6-in-game-overlay).
- Die Testdatei des Datenträger-Tests wird immer gelöscht. Der Modus „Extended“ schreibt 4 GB – auf SSDs nur bei Bedarf verwenden.

## 7.6 Geekbench (Primate Labs)

**Zweck:** Wer zusätzlich mit dem bekannten Geekbench vergleichen möchte, kann ein bereits installiertes Geekbench aus ULTIMATE X starten und die Ergebnisse im Benchmark-Verlauf festhalten.

**Wichtig**
- Geekbench ist ein Programm von Primate Labs. **ULTIMATE X liefert Geekbench nicht mit, lädt nichts herunter und lädt nichts hoch.**
- Die kostenlose Geekbench-Version lädt ihre Ergebnisse **selbst** in den Geekbench Browser (Internet) hoch – das macht Geekbench, nicht ULTIMATE X.
- Geekbench-Werte haben eine eigene Skala und fließen **nicht** in den ULTIMATE X PERFORMANCE SCORE ein.

**Schritte**
1. Seite **Benchmarks** → Karte **GEEKBENCH**.
2. Ist Geekbench installiert, steht dort der gefundene Pfad. **Geekbench starten** öffnet das Programm (ohne Administratorrechte, eigenes Fenster). Ist es nicht installiert, öffnet **Offizielle Download-Seite öffnen** die Seite www.geekbench.com/download/ im Browser – Download und Installation führen Sie selbst durch.
3. Test in Geekbench durchführen.
4. In ULTIMATE X unter **Ergebnis eintragen**: **Version** (Geekbench 6 oder 5), **Single-Core**- und **Multi-Core**-Wert, optional **GPU (optional)** mit Schnittstelle (OpenCL oder Vulkan).
5. **Ergebnis speichern.** Die Karte zeigt den letzten Multi-Core-Wert, Datum, alle eingetragenen Werte und die Veränderung zum vorherigen Ergebnis.

**Hinweise und Grenzen**
- Gesucht wird ausschließlich in `C:\Program Files\Geekbench 6\` und `C:\Program Files\Geekbench 5\` (höchste Version zuerst). Andere Installationsorte werden bewusst nicht unterstützt.
- Verglichen wird nur mit Ergebnissen derselben Geekbench-Hauptversion (5 und 6 haben unterschiedliche Skalen).
- Die Werte tragen Sie selbst ein; sie werden nicht automatisch ausgelesen. Tausendertrennzeichen sind erlaubt (z. B. „22.456“, „22,456“ oder „22 456“). Ist der Multi-Core-Wert kleiner als der Single-Core-Wert, wird die Eingabe als Tippfehler abgelehnt. Ein GPU-Wert wird nur zusammen mit der Schnittstelle (OpenCL oder Vulkan) angenommen.

---

# 8 Zeitgesteuertes Herunterfahren

**Zweck:** Den PC zu einem bestimmten Zeitpunkt herunterfahren, neu starten, sperren, abmelden oder in den Energiesparmodus versetzen – z. B. nach einem langen Download oder einer nächtlichen Datensicherung.

## 8.1 Aktion planen

1. Seite **Tools & Power** öffnen, Bereich **Zeitgesteuert**.
2. **Aktion** wählen: Herunterfahren, Neustart, Sperren, Abmelden oder Energiesparen.
3. Zeitpunkt festlegen:
   - **In …** – Anzahl und Einheit (Minuten oder Stunden), z. B. „90 Minuten“,
   - **Um …** – Uhrzeit im Format `HH:mm` (z. B. `22:30`) und **heute** oder **morgen**.
4. **Planen** klicken und die Rückfrage bestätigen.
5. Oben erscheint „… geplant um 22:30 Uhr (heute)“ mit der Restzeit (`noch hh:mm:ss`).

## 8.2 Abbrechen oder verschieben

- Im Bereich **Zeitgesteuert**: **Abbrechen**, **+15 min** oder **+1 h**.
- **Geplantes Herunterfahren abbrechen** (oben im Power-Bereich) bricht eine geplante Abschaltung ebenfalls ab.
- **Warnungen:** 5 Minuten und 1 Minute vor dem Zeitpunkt holt ULTIMATE X sein Fenster nach vorn und fragt, ob die Aktion abgebrochen oder um 15 Minuten verschoben werden soll. Sichern Sie spätestens dann Ihre offene Arbeit.

## 8.3 Wer führt die Aktion aus?

| Aktion | ausgeführt von | Wenn ULTIMATE X beendet wird |
|---|---|---|
| Herunterfahren, Neustart | Windows (geplant mit `shutdown /t`) | Planung läuft weiter; zum Zeitpunkt schließt Windows alle Programme **zwangsweise** – ungespeicherte Arbeit geht verloren |
| Sperren, Abmelden, Energiesparen | ULTIMATE X selbst | funktioniert nur, solange ULTIMATE X läuft (minimiert genügt); war die App zum Zeitpunkt geschlossen, wird die Aktion **nicht nachgeholt** – beim nächsten Start erscheint ein Hinweis |

Die Planung wird gespeichert (`%LOCALAPPDATA%\UltimateX\power-schedule.json`) und nach einem Neustart von ULTIMATE X wieder angezeigt.

## 8.4 Grenzen

- Höchstens **7 Tage** im Voraus; es kann immer nur **eine** Aktion geplant sein (eine neue Planung ersetzt die alte).
- Zeitumstellung: Eine Uhrzeit, die es nicht gibt (z. B. 02:30 beim Beginn der Sommerzeit), wird auf die erste gültige Uhrzeit danach (03:00) gelegt; eine doppelt vorhandene Uhrzeit (Ende der Sommerzeit) gilt beim ersten Auftreten.
- Eine Uhrzeit „heute“, die schon vorbei ist, wird abgelehnt (nicht verschoben).
- Wurde ein Herunterfahren außerhalb von ULTIMATE X abgebrochen (z. B. mit `shutdown /a`), zeigt ULTIMATE X die Planung bis zum Zeitpunkt weiter an.
- Hat ein anderes Programm bereits ein Herunterfahren geplant, meldet Windows einen Fehler – zuerst dieses abbrechen.

---

# 9 Fehlersuche

Wenn der PC abstürzt, langsam wird oder sich seltsam verhält, gehen Sie am besten in dieser Reihenfolge vor:

1. **Zeitleiste** – Was hat sich geändert, seit das Problem auftritt?
2. **Absturzanalyse** – Was sagen Bluescreens, Hardwarefehler und Programmabstürze?
3. **Windows Health** – Sind Systemdateien, Datenträger und Speicher in Ordnung?
4. **Protokolle** – Windows Logs und das Protokoll von ULTIMATE X für Details.

## 9.1 Zeitleiste – Was ist passiert?

**Zweck:** „Seit gestern stürzt der PC ab – was hat sich geändert?“ Die Zeitleiste zeigt Änderungen und Probleme der letzten 7, 30 oder 90 Tage nach Tagen geordnet.

**Erfasste Ereignisse**
- Windows-Updates (installiert / fehlgeschlagen)
- Programminstallationen und -deinstallationen (Windows Installer sowie Installationsdatum aus der Programmliste – dort nur Datum, Uhrzeit „––:––“)
- Treiberinstallationen
- Bluescreens und unerwartete Neustarts
- Programmabstürze
- Start und Herunterfahren von Windows
- System Snapshots sowie Autostart- und Desktop-Layout-Sicherungen von ULTIMATE X

**Schritte**
1. Seite **Zeitleiste** öffnen und den **Zeitraum** wählen (Standard: 30 Tage).
2. Mit den Kästchen nach **Art** filtern; **Nur Probleme & Änderungen** zeigt nur Abstürze, Fehlschläge, Treiber und Updates; **Alle Arten** setzt den Filter zurück.
3. Im **Suchfeld** nach Programm-, Treiber- oder Update-Namen suchen.
4. Ein Ereignis anklicken – rechts erscheinen die **DETAILS** (Zeitpunkt, Quelle, Fehlercodes).

**Ursachen-Hinweis:** Tritt innerhalb von **48 Stunden** nach einer Treiber- oder Update-Installation der erste Absturz auf (und gab es in den 7 Tagen davor keinen gleichartigen), wird er mit ⚑ markiert und oben unter **URSACHEN-HINWEIS** genannt. Das ist ein **möglicher Zusammenhang, kein Beweis** – prüfen Sie z. B., ob das Zurücksetzen des Treibers oder das Deinstallieren des Updates hilft.

**Grenzen**
- Die Ereignisprotokolle von Windows sind größenbegrenzt; ältere Ereignisse können fehlen.
- Starts aus Schnellstart/Ruhezustand erscheinen nicht als „Start“.
- Programme, die ohne Windows Installer installiert wurden, erscheinen nur mit Datum (laut Programmliste), sofern der Hersteller es einträgt.
- Läufe des Backup-Centers erscheinen nicht in der Zeitleiste – sie stehen im Verlauf des Backup-Centers.
- Alles wird lokal gelesen, ohne Adminrechte und ohne Upload.

## 9.2 Absturzanalyse

**Zweck:** Bluescreens, Hardwarefehler und Programmabstürze verständlich erklärt.

**Funktionen**
- **Zeitraum (Tage)** wählen.
- **Bluescreens** mit Klartext-Erklärung, Parametern, Quelle und Empfehlung.
- **Absturzabbilder (Minidumps):** **Absturzabbilder lesen (Admin)** liest den Fehlercode aus den Abbildern; mit installiertem Microsoft-Debugger (*Debugging Tools for Windows*) ermittelt **Ursache ermitteln** zusätzlich den verursachenden Treiber (**Originalausgabe des Debuggers** einblendbar).
- **Hardwarefehler (WHEA)**, Grafiktreiber-Resets.
- **Programmabstürze und hängende Programme** mit Programm, Art, Anzahl, letztem Auftreten, Modul und Code.
- Knöpfe **Zuverlässigkeitsverlauf** (Windows) und **RAM testen**.

**Schritte**
1. Seite öffnen, Zeitraum wählen.
2. Bluescreens und Empfehlungen lesen.
3. Für Details **Absturzabbilder lesen (Admin)** (UAC) und ggf. **Ursache ermitteln**.
4. Deutet die Empfehlung auf Speicherfehler hin, **RAM testen** (siehe Memory Test).

**Hinweise:** Absturzabbilder enthalten Speicherinhalte. Sie verlassen nie den PC und werden nicht kopiert. Der Debugger lädt Symbole ausschließlich vom Microsoft-Symbolserver (HTTPS). **Grenzen:** Die Ursachenanalyse benötigt die Debugging Tools for Windows (Windows SDK).

## 9.3 Windows Health prüfen und reparieren

**Funktionen**
- **WINDOWS HEALTH SCORE** (100 minus nachvollziehbare Abzüge).
- **Systemdateien und Komponentenspeicher:** **Systemdateien prüfen (SFC)**, **Quick Check**, **Deep Scan**, **Windows Repair** – **Reparatur-Workflow (DISM → SFC)**.
- **Datenträger (CHKDSK):** Laufwerk wählen, **Prüfen (nur lesend)** oder **Reparieren …**.
- **Arbeitsspeicher:** **Windows Memory Diagnostic …**.
- **Updates & Neustart:** **Windows Update öffnen**.
- **Wiederherstellung:** **Wiederherstellungspunkt erstellen**, **Systemwiederherstellung öffnen**.
- **Stabilität (30 Tage)**, **Wichtige Windows-Dienste**, **Häufigste Probleme (Ereignisprotokolle, 7 Tage)**.

**Schritte – Windows reparieren**
1. Zuerst **Quick Check** bzw. **Systemdateien prüfen (SFC)**.
2. Meldet die Prüfung Fehler: **Windows Repair** starten (DISM prüft und repariert den Komponentenspeicher, danach SFC die Systemdateien). UAC bestätigen.
3. Die Live-Ausgabe verfolgen; **Technische Ausgabe anzeigen** blendet die Originalausgabe ein. Am Ende steht eine verständliche Zusammenfassung.
4. Bei Datenträgerproblemen **Prüfen (nur lesend)**, bei Fehlern **Reparieren …**. Das Systemlaufwerk kann nur beim nächsten Start repariert werden – Windows prüft und repariert es dann automatisch vor der Anmeldung.

**Hinweise**
- Alle Werkzeuge laufen über eine feste Befehlsliste und den signierten Admin-Helper; Windows fragt jeweils per UAC.
- **Anzeige abbrechen** stoppt nur die Anzeige, nicht den laufenden Befehl.
- Vor *Windows Repair*, Datenträgerreparatur und Treiber-Updates wird automatisch ein Wiederherstellungspunkt angefordert (abschaltbar in den Einstellungen). Windows erstellt höchstens einen Punkt pro 24 Stunden.
- Windows Update wird von ULTIMATE X nie deaktiviert.

## 9.4 Protokolle

| Protokoll | Wo | Inhalt |
|---|---|---|
| **Windows Logs** (Seite) | Windows-Ereignisprotokolle System, Anwendung, Setup | Fehler (und auf Wunsch Warnungen) mit Quelle und Event-ID |
| **Logs** (Seite) | `%LOCALAPPDATA%\UltimateX\logs\app-*.jsonl` und `security-*.jsonl` | Aktionen und Sicherheitsereignisse von ULTIMATE X, 30 Tage aufbewahrt |
| **Letzte Admin-Befehle** (Technician Mode) | aktuelle Sitzung | Exit Codes und Originalausgabe der Admin-Befehle |
| CBS-Protokoll von Windows | `%windir%\Logs\CBS\CBS.log` | Details zu SFC/DISM |

Auf der Seite **Logs** zeigt **Nur Security-Ereignisse** sicherheitsrelevante Einträge; **Exportieren (TXT/JSON) …** speichert das Protokoll, z. B. für eine Fehlermeldung. Im **Admin / Service Mode → Export** exportieren Sie Systembericht und Logs gemeinsam.

---

# 10 Häufige Fragen und Problembehandlung

## 10.1 Installation und Start

| Problem | Lösung |
|---|---|
| MSI meldet „Eine neuere Version ist bereits installiert“ | Downgrades sind gesperrt. Ältere Version zuerst über *Einstellungen → Apps* deinstallieren. |
| App startet nicht, Meldung „Integritätsprüfung“ | Programmdateien wurden verändert (ungültige Signatur). MSI erneut installieren. |
| „Admin-Helper nicht gefunden“ | Neu installieren; `UltimateX.AdminHelper.exe` muss neben `UltimateX.exe` liegen. |
| „Sichere Verbindung zum Admin-Helper konnte nicht hergestellt werden“ | Andere Sicherheitssoftware blockiert Named Pipes oder die App wurde aus einem ungewöhnlichen Ordner gestartet. App aus dem Installationsordner starten. |
| „Ausgabe des Admin-Helpers aus Sicherheitsgründen verworfen“ | Die Prüfung der Gegenstelle oder des Protokolls ist fehlgeschlagen (siehe `security-*.jsonl`). Der Befehl selbst wurde ausgeführt; das Ergebnis lässt sich in Windows prüfen (z. B. `%windir%\Logs\CBS\CBS.log`). |
| Einstellungen zurücksetzen | `%LOCALAPPDATA%\UltimateX\settings.json` löschen. |
| Windows-Benachrichtigungen erscheinen nicht | Benachrichtigungen erscheinen nur mit der Startmenü-Verknüpfung aus dem MSI und wenn „Windows-Benachrichtigungen“ in den Einstellungen an ist; sonst erscheint ein Hinweis in der App. |
| Windows Hello erscheint nicht | Hello in den Windows-Einstellungen einrichten; ohne Hello wird ausdrücklich bestätigt. |

## 10.2 Sensoren und Temperaturen

| Problem | Lösung |
|---|---|
| „HWiNFO Sensor Source: NOT AVAILABLE“ | HWiNFO ist nicht gestartet – optional. Für echte CPU-Kerntemperaturen HWiNFO starten. |
| HWiNFO-Status „INACTIVE“ | HWiNFO läuft, aber *Shared Memory Support* ist aus: HWiNFO → Einstellungen → „Shared Memory Support“ aktivieren, Sensorfenster offen lassen (in der kostenlosen Version begrenzt HWiNFO die Laufzeit). |
| LibreHardwareMonitor „INACTIVE“ | LibreHardwareMonitor als Administrator starten, sonst ist die WMI-Schnittstelle leer. |
| Keine GPU-Temperatur | Aktuellen Grafiktreiber (WDDM 2.4+) installieren; manche iGPUs melden keine Temperatur. |
| CPU-Temperatur fehlt ohne Zusatzprogramm | Gewollt: Windows liefert CPU-Kerntemperaturen nur über Kernel-Treiber; ULTIMATE X lädt keinen. ACPI-Zonen erscheinen als „Mainboard“, nicht als CPU. |
| Min/Ø/Max wirken falsch | Statistik läuft seit Programmstart; **Min/Ø/Max zurücksetzen** auf der Seite *Sensorquellen*. |

## 10.3 Netzwerk und Ping

| Problem | Lösung |
|---|---|
| Gerät „NICHT ERREICHBAR“, obwohl es läuft | Viele Geräte/Firewalls beantworten keinen Ping. Mit **SERVICES** prüfen, ob Dienste erreichbar sind. |
| Hostname wird nicht aufgelöst | Lokale Namen (`*.local`, `fritz.box`) benötigen den Router als DNS bzw. mDNS/LLMNR. IP-Adresse verwenden. |
| Live-Ping stoppt beim Seitenwechsel | Gewollt – Dauerping läuft nur, solange die Seite geöffnet ist. |
| WLAN zeigt keine SSID | Einstellungen → Datenschutz → Standort → „Apps Zugriff auf Ihren Standort erlauben“. |
| Traceroute zeigt viele „*“ | Router beantworten Ping oft absichtlich nicht – kein Fehler. Nach 5 stummen Hops endet die Verfolgung. |
| DNS-Umstellung nur teilweise übernommen | Eine der UAC-Abfragen wurde abgelehnt. Anbieter erneut wählen oder **Automatisch (DHCP)**. |

## 10.4 Komponententests

| Problem | Lösung |
|---|---|
| Kamera „Zugriff blockiert“ | Einstellungen → Datenschutz → Kamera → „Desktop-Apps Zugriff auf die Kamera erlauben“. |
| Mikrofon „kein Signal“ | Stummschaltung, Standard-Aufnahmegerät und Datenschutzfreigabe für Mikrofon prüfen. |
| Systemzeit „NOT AVAILABLE“ | UDP-Port 123 (NTP) wird blockiert oder kein Internet. |
| WinRE „NOT AVAILABLE“ | `ReAgent.xml` ist ohne Adminrechte nicht lesbar – Status mit `reagentc /info` in einer Admin-Konsole prüfen. |
| SMART „noch nicht gelesen“ | Seite *Hardware* → **SMART-Details lesen (Adminrechte)** bzw. *Storage* → **SMART lesen (UAC)**. |
| Schreibtest „nicht möglich“ | Keine Schreibrechte auf dem Laufwerk (z. B. Wechseldatenträger) oder zu wenig Platz. |
| Speicherdiagnose-Ergebnis fehlt | Das Ergebnis erscheint nach dem Neustart im Ereignisprotokoll und danach im Memory Test. |

## 10.5 PC-Bericht

| Problem | Lösung |
|---|---|
| Datenschutzmodus lässt sich nicht ausschalten | Abschalten erfordert Windows Hello bzw. die Bestätigung; abgebrochen = bleibt an. |
| Abschnitt fehlt | Der Abschnitt ist abgewählt oder es liegen noch keine Daten vor (z. B. kein Komponententest). |
| PDF statt Drucker | Im Druckdialog „Microsoft Print to PDF“ wählen oder direkt **EXPORT PC REPORT (PDF)**. |

## 10.6 Reparatur und Updates

| Problem | Lösung |
|---|---|
| SFC „konnte den Vorgang nicht ausführen“ | Neustart (ausstehende Updates), dann erneut. |
| DISM RestoreHealth Fehler 0x800f081f | Internetverbindung/Windows Update prüfen. |
| winget fehlt | „App-Installer“ aus dem Microsoft Store installieren. |
| Desktop Organizer meldet „automatisch anordnen“ | Rechtsklick auf den Desktop → Ansicht → „Symbole automatisch anordnen“ deaktivieren. |

## 10.7 Neu in Version 1.3

| Frage / Problem | Antwort / Lösung |
|---|---|
| **speedtest.ch zeigt „WebView2-Runtime ist nicht installiert“** | Die Microsoft Edge WebView2-Runtime fehlt (bei Windows 10 möglich). **WebView2-Runtime bei Microsoft herunterladen** öffnet die offizielle Seite; Runtime installieren und ULTIMATE X neu starten. Der eigene Speedtest (Cloudflare) funktioniert auch ohne WebView2. |
| **Overlay zeigt „FPS: N/A“** | Ihrem Konto fehlt die Berechtigung für die FPS-Messung (ETW). Konto der Gruppe „Leistungsprotokollbenutzer“ hinzufügen, ab- und wieder anmelden, ULTIMATE X neu starten – Anleitung in [6.2](#62-fps-messung-ohne-eingriff-ins-spiel) bzw. über **Anleitung anzeigen**. |
| **FPS-Zeile fehlt ganz** | Das Spiel nutzt OpenGL oder Vulkan ohne Direct3D-Bildausgabe – dafür gibt es keine Messung. |
| **Overlay ist im Spiel nicht zu sehen** | Das Spiel läuft im exklusiven Vollbildmodus. Im Spiel „Randloses Fenster“ bzw. „Vollbild (Fenster)“ wählen. Prüfen Sie außerdem, ob das Overlay eingeblendet ist (Tastenkürzel, Standard Strg+Umschalt+O). |
| **Tastenkürzel reagiert nicht** | Die Kombination ist von einem anderen Programm belegt (Hinweis auf der Seite *Overlay*). Andere Kombination wählen und **Übernehmen**. |
| **Geplante Datensicherung ist nicht gelaufen** | Zeitpläne laufen nur, solange ULTIMATE X geöffnet ist (minimiert oder im Infobereich genügt). Tipp: **Beim Schließen in den Infobereich minimieren** aktivieren und in den Einstellungen **Mit Windows starten** + **Minimiert starten** einschalten. Verpasste tägliche/wöchentliche Sicherungen werden beim nächsten Start nachgeholt, sobald die Zielplatte angeschlossen ist. |
| **Backup-Center sichert nicht auf die gewohnte Platte** | Unter dem Laufwerksbuchstaben steckt eine andere Backup-Platte (erkannt über `UltimateX-Backup\target.json`). Richtige Platte anschließen. |
| **System-Abbild: wbadmin meldet einen Fehler** | Ziel prüfen: externe, lokal angeschlossene **NTFS**-Platte, nicht das Systemlaufwerk, freier Platz ≥ belegter Platz des Systemlaufwerks + 10 %. UAC muss bestätigt werden. Die Original-Ausgabe von wbadmin steht im Bereich System-Abbild. |
| **System-Abbild: ältere Abbilder fehlen** | Windows behält je Platte und PC in der Regel nur das neueste Abbild zuverlässig. Mehrere Platten im Wechsel nutzen. |
| **„VHDX-Dateien suchen“ meldet „nur Administratoren“** | **Abbild-Ordner im Explorer** öffnen, die Zugriffsabfrage von Windows bestätigen, dann erneut suchen. |
| **Gesicherte Datei wurde in allen Versionen geändert** | Dateien in Versionsordnern sind per Hardlink geteilt. Nie direkt in Versionsordnern arbeiten – erst herauskopieren oder **Wiederherstellen** nutzen. |
| **Geöffnete Dateien wurden übersprungen** | Ohne Adminrechte keine Schattenkopien. Outlook/Browser vor der Sicherung schließen. |
| **Steam-Suche meldet „zu viele Anfragen“** | Steam begrenzt die Abfragen (HTTP 429). Einige Minuten warten und erneut versuchen. |
| **Spiel wird online nicht gefunden** | Nur Spiele im Steam-Shop sind abrufbar; bei mehrdeutigen Namen den Namen präzisieren. |
| **Performance Score ist nach dem Update gesunken** | Die Skala wurde in 1.3 neu kalibriert (in 1.2 stand die CPU immer bei 100). Ihr PC ist nicht langsamer geworden. |
| **SSD-Wertung „N/A“** | Das Systemlaufwerk wurde noch nie getestet. **Datenträger testen** mit dem Systemlaufwerk oder **START COMPLETE BENCHMARK** ausführen. |
| **Geplantes Sperren/Energiesparen wurde nicht ausgeführt** | Diese Aktionen führt ULTIMATE X selbst aus und nur, solange die App läuft. Sie werden nicht nachgeholt. |
| **Planung „Herunterfahren“ schlägt fehl** | Ein anderes Programm hat bereits ein Herunterfahren geplant. Zuerst **Geplantes Herunterfahren abbrechen**. |
| **Infobereich meldet keine Temperaturwarnung** | Ohne Temperaturquelle (HWiNFO/LibreHardwareMonitor bzw. Grafiktreiber) gibt es keine Temperaturregel. |
| **SMB1 deaktiviert – NAS nicht mehr erreichbar** | Sehr alte NAS-Geräte/Drucker können nur SMB1. Firmware des Geräts aktualisieren; SMB1 nur bewusst und vorübergehend wieder aktivieren (in den Windows-Features). |
| **Speicherplatz wird nach dem Aufräumen nicht frei** | Gelöschte Dateien liegen im Papierkorb. Papierkorb leeren. |

---

# 11 Anhang

## A Tastenkürzel

| Kürzel | Wirkung | Wo |
|---|---|---|
| **Strg+K** | Suchfeld aktivieren (globale Suche) | Hauptfenster |
| **Enter** (im Suchfeld) | Seite suchen und öffnen | Hauptfenster |
| **F5** | aktuelle Seite neu laden | Hauptfenster |
| **Strg+Umschalt+O** (Standard, änderbar) | In-Game-Overlay ein-/ausblenden | systemweit, solange ULTIMATE X läuft |
| **Enter** (im Spielsuchfeld) | Spiel online suchen | Can My PC Run It |
| Doppelklick auf das Infobereich-Symbol | Fenster öffnen | Taskleiste |

## B Dateiablageorte

**Unter `%LOCALAPPDATA%\UltimateX\`** (nur Ihr Benutzerkonto, keine Adminrechte nötig):

| Datei / Ordner | Inhalt |
|---|---|
| `settings.json` | Einstellungen (bei Beschädigung: Sicherungskopie + Standardwerte) |
| `logs\app-*.jsonl`, `logs\security-*.jsonl` | Aktions- und Sicherheitsprotokoll (30 Tage) |
| `history.db` | Verläufe (Speedtest, Health Score, Benchmarks, Netzwerkgeräte) |
| `backups\` | Sicherungen vor Änderungen (z. B. Autostart, Desktop-Layout) |
| `desktop-rules.json` | Regeln des Desktop Organizers |
| `smart-cache.json` | zuletzt gelesene SMART-Werte |
| `ComponentTests\history.json` | Verlauf des Komponententests (max. 20 Läufe) |
| `snapshots\` | System Snapshots |
| `install-log.jsonl` | Installationsprotokoll von New PC Setup |
| `setup\office\` | Office Deployment Tool |
| `clamav\` | ClamAV-Konfiguration, Signaturen, Quarantäne, Scanverlauf |
| `oui.csv` | Herstellerliste (IEEE) des Netzwerk-Scanners |
| `updates\` | heruntergeladenes App-Update (nur nach Prüfung ausgeführt) |
| `games-user.json` | online geladene Spiele-Anforderungen (v1.3) |
| `backup-profiles.json`, `backup-history.json` | Backup-Profile und -Verlauf (v1.3) |
| `privacy-backup.json` | ursprüngliche Datenschutzwerte für „Rückgängig“ (v1.3) |
| `power-schedule.json` | geplante Energieaktion (v1.3) |
| `uptime-log.json` | eigene Laufzeit-Mitschrift für PC Lifetime (v1.3) |
| `overlay.json` | Overlay-Einstellungen und -Profile (v1.3) |
| `background-monitor.json` | Einstellungen der Infobereich-Überwachung (v1.3) |
| `WebView2\` | Browserdaten des eingebetteten speedtest.ch (v1.3) |
| `bench\` | Arbeitsordner für Datenträger-Testdateien (werden immer gelöscht) |

**Weitere Orte**

| Ort | Inhalt |
|---|---|
| `Dokumente\ULTIMATE X\` | exportierte PDF-Berichte |
| `Dokumente\ULTIMATE X\Sessions\` | aufgezeichnete Spielsitzungen (CSV, v1.3) |
| `<Ziel>\UltimateX-Backup\<Profil>\<Datum_Uhrzeit>\` | Datei-Backup-Versionen (v1.3) |
| `<Ziel>\UltimateX-Backup\target.json` | Kennung der Backup-Platte (nicht löschen) |
| `<Laufwerk>:\UltimateX-Backup\Treiber\<Zeitstempel>\` | Treiber-Sicherungen (v1.3) |
| `<Laufwerk>:\WindowsImageBackup\<PC-Name>\` | System-Abbild (von Windows angelegt) |
| `C:\Program Files\ULTIMATE X Windows Control\` | Programmdateien |

Den Datenordner öffnen Sie über **Einstellungen → Über → Datenordner öffnen**.

## C Lizenz

- **Quellcode:** MIT License – Copyright (c) 2026 Dennis Karahusic. Siehe Datei `LICENSE`.
- **Installation und Nutzung:** Der Installer zeigt die Endbenutzer-Lizenzvereinbarung mit Haftungsausschluss und Haftungsbeschränkung (Schweiz) an: [EULA.md](EULA.md). ULTIMATE X ist kostenlose, nicht-kommerzielle Open-Source-Software. Die Rechte am Quellcode richten sich nach der MIT-Lizenz.
- **Fremdsoftware**, die Sie auf Wunsch installieren (z. B. ClamAV, Microsoft 365 Apps, Firefox, Geekbench), unterliegt den Lizenzen der jeweiligen Hersteller. Verwendete Bibliotheken: [DEPENDENCIES.md](DEPENDENCIES.md).
