# Troubleshooting – ULTIMATE X Windows Control

## Installation & Start

| Problem | Lösung |
|---|---|
| MSI meldet „Eine neuere Version ist bereits installiert“ | Downgrades sind gesperrt. Ältere Version zuerst über *Einstellungen → Apps* deinstallieren. |
| App startet nicht, Meldung „Integritätsprüfung“ | Programmdateien wurden verändert (ungültige Signatur). MSI erneut installieren. |
| „Admin-Helper nicht gefunden“ | Neu installieren; `UltimateX.AdminHelper.exe` muss neben `UltimateX.exe` liegen. |
| „Sichere Verbindung zum Admin-Helper konnte nicht hergestellt werden“ | Andere Sicherheitssoftware blockiert Named Pipes oder die App wurde aus einem ungewöhnlichen Ordner gestartet. App aus dem Installationsordner starten. |
| „Ausgabe des Admin-Helpers aus Sicherheitsgründen verworfen“ | Die Prüfung der Gegenstelle oder des Protokolls ist fehlgeschlagen (siehe `security-*.jsonl`). Der Befehl selbst wurde ausgeführt; das Ergebnis kann in Windows (z. B. `%windir%\Logs\CBS\CBS.log`) geprüft werden. |
| Einstellungen zurücksetzen | `%LOCALAPPDATA%\UltimateX\settings.json` löschen. |
| Logs | `%LOCALAPPDATA%\UltimateX\logs` bzw. Seite „Logs“. |

## Sensoren & Temperaturen

| Problem | Lösung |
|---|---|
| „HWiNFO Sensor Source: NOT AVAILABLE“ | HWiNFO ist nicht gestartet – optional. Für echte CPU-Kerntemperaturen HWiNFO starten. |
| HWiNFO-Status „INACTIVE“ | HWiNFO läuft, aber *Shared Memory Support* ist aus: HWiNFO → Einstellungen → „Shared Memory Support“ aktivieren, Sensorfenster offen lassen (in der kostenlosen Version gilt eine Zeitbegrenzung von HWiNFO). |
| LibreHardwareMonitor „INACTIVE“ | LibreHardwareMonitor als Administrator starten, sonst ist die WMI-Schnittstelle leer. |
| Keine GPU-Temperatur | Aktuellen Grafiktreiber (WDDM 2.4+) installieren; manche iGPUs melden keine Temperatur. |
| CPU-Temperatur fehlt ohne Zusatzprogramm | Gewollt: Windows liefert CPU-Kerntemperaturen nur über Kernel-Treiber; ULTIMATE X lädt keinen. ACPI-Zonen werden als „Mainboard“ angezeigt, nicht als CPU. |
| Min/Ø/Max wirken falsch | Statistik läuft seit Programmstart; „Min/Ø/Max zurücksetzen“ auf der Seite *Sensorquellen*. |

## Netzwerk & Ping

| Problem | Lösung |
|---|---|
| Gerät „NICHT ERREICHBAR“, obwohl es läuft | Viele Geräte/Firewalls beantworten keinen Ping. Mit „SERVICES“ prüfen, ob Dienste erreichbar sind. |
| Hostname wird nicht aufgelöst | Lokale Namen (`*.local`, `fritz.box`) benötigen den Router als DNS bzw. mDNS/LLMNR. IP-Adresse verwenden. |
| Live-Ping stoppt beim Seitenwechsel | Gewollt – Dauerping läuft nur, solange die Seite geöffnet ist. |
| WLAN zeigt keine SSID | Einstellungen → Datenschutz → Standort → „Apps Zugriff auf Ihren Standort erlauben“. |

## Komponententests

| Problem | Lösung |
|---|---|
| Kamera „Zugriff blockiert“ | Einstellungen → Datenschutz → Kamera → „Desktop-Apps Zugriff auf die Kamera erlauben“. |
| Mikrofon „kein Signal“ | Stummschaltung, Standard-Aufnahmegerät und Datenschutzfreigabe für Mikrofon prüfen. |
| Systemzeit „NOT AVAILABLE“ | UDP-Port 123 (NTP) wird blockiert oder kein Internet. |
| WinRE „NOT AVAILABLE“ | `ReAgent.xml` ist ohne Adminrechte nicht lesbar – Status mit `reagentc /info` in einer Admin-Konsole prüfen. |
| SMART „noch nicht gelesen“ | Seite *Hardware* → „SMART lesen“ (UAC). |
| Schreibtest „nicht möglich“ | Keine Schreibrechte auf dem Laufwerk (z. B. Wechseldatenträger) oder zu wenig Platz. |
| Speicherdiagnose-Ergebnis fehlt | Ergebnis erscheint nach dem Neustart im Ereignisprotokoll und danach im RAM-Test. |

## PC-Bericht (PDF/Druck)

| Problem | Lösung |
|---|---|
| Datenschutzmodus lässt sich nicht ausschalten | Abschalten erfordert Windows Hello bzw. die Bestätigung; abgebrochen = bleibt an. |
| Abschnitt fehlt | In der Vorschau ist der Abschnitt deaktiviert oder es liegen noch keine Daten vor (z. B. kein Komponententest). |
| PDF statt Drucker | Im Druckdialog „Microsoft Print to PDF“ wählen oder direkt „EXPORT PC REPORT (PDF)“. |

## Reparatur & Updates

| Problem | Lösung |
|---|---|
| SFC „konnte den Vorgang nicht ausführen“ | Neustart (ausstehende Updates), dann erneut. |
| DISM RestoreHealth 0x800f081f | Internetverbindung/Windows Update prüfen. |
| winget fehlt | „App-Installer“ aus dem Microsoft Store installieren. |
| Windows Hello erscheint nicht | Hello in den Windows-Einstellungen einrichten; ohne Hello wird explizit bestätigt. |
| Desktop Organizer meldet „automatisch anordnen“ | Rechtsklick auf Desktop → Ansicht → „Symbole automatisch anordnen“ deaktivieren. |

## Neu in v1.3

Ausführlich mit Schritt-für-Schritt-Anleitungen: [HANDBUCH.md, Kapitel 10](HANDBUCH.md#10-häufige-fragen-und-problembehandlung) · English: [MANUAL_EN.md, chapter 10](MANUAL_EN.md#10-faq-and-problem-solving).

| Problem | Lösung |
|---|---|
| speedtest.ch: „WebView2-Runtime ist nicht installiert“ | Knopf „WebView2-Runtime bei Microsoft herunterladen“, Runtime installieren, App neu starten. Der eigene Speedtest (Cloudflare) funktioniert ohne WebView2. |
| Overlay zeigt „FPS: N/A“ | Konto zur Gruppe „Leistungsprotokollbenutzer“ (*Performance Log Users*) hinzufügen, ab- und anmelden, App neu starten. |
| FPS-Zeile fehlt | Spiel nutzt OpenGL/Vulkan ohne Direct3D-Bildausgabe – keine Messung möglich. |
| Overlay im Spiel unsichtbar | Exklusiver Vollbildmodus: im Spiel „Randloses Fenster“ bzw. „Vollbild (Fenster)“ wählen; Tastenkürzel prüfen (Standard Strg+Umschalt+O). |
| Geplantes Backup lief nicht | Zeitpläne nur, solange ULTIMATE X läuft – „Beim Schließen in den Infobereich minimieren“ und „Mit Windows starten“/„Minimiert starten“ aktivieren. |
| Backup auf gewohnte Platte verweigert | Unter dem Buchstaben steckt eine andere Backup-Platte (`UltimateX-Backup\target.json`). Richtige Platte anschließen. |
| System-Abbild schlägt fehl (wbadmin) | Ziel: externe, lokale NTFS-Platte, nicht das Systemlaufwerk, freier Platz ≥ belegter Platz + 10 %; UAC bestätigen. |
| „VHDX-Dateien suchen“: nur Administratoren | „Abbild-Ordner im Explorer“ öffnen, Windows-Zugriffsabfrage bestätigen, erneut suchen. |
| Steam: „zu viele Anfragen“ (HTTP 429) | Einige Minuten warten, erneut versuchen. |
| Performance Score nach Update niedriger | Neue Kalibrierung in 1.3 (in 1.2 stand die CPU immer bei 100). |
| Geplantes Sperren/Energiesparen nicht ausgeführt | Nur bei laufender App, wird nicht nachgeholt. |
| Geplantes Herunterfahren schlägt fehl | Ein anderes Programm hat bereits ein Herunterfahren geplant – zuerst „Geplantes Herunterfahren abbrechen“. |
| DNS-Umstellung nur teilweise | Eine UAC-Abfrage wurde abgelehnt – Anbieter erneut wählen oder „Automatisch (DHCP)“. |
