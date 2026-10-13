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
