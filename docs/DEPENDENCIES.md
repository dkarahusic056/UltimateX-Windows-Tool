# Abhängigkeiten & SBOM (§92, §93)

Alle Versionen sind zentral in `Directory.Packages.props` fixiert. In CI wird mit Lock-Files (`packages.lock.json`, beim ersten lokalen Restore erzeugt und einchecken) im *Locked Mode* wiederhergestellt.

## Laufzeit

| Paket | Version | Lizenz | Herausgeber | Zweck |
|---|---|---|---|---|
| CommunityToolkit.Mvvm | 8.2.2 | MIT | .NET Foundation / Microsoft | MVVM (ObservableProperty, RelayCommand) |
| System.Management | 8.0.0 | MIT | Microsoft | WMI |
| System.Diagnostics.EventLog | 8.0.0 | MIT | Microsoft | Ereignisprotokolle |
| System.Diagnostics.PerformanceCounter | 8.0.0 | MIT | Microsoft | Datenträgeraktivität, GPU-Last/VRAM |
| Microsoft.Data.Sqlite.Core | 8.0.10 | MIT | Microsoft / .NET Foundation | Verlaufsdaten (Speedtest, Health, Benchmarks, Netzwerkgeräte) |
| SQLitePCLRaw.bundle_winsqlite3 | 2.1.6 | Apache-2.0 | Eric Sink | Nutzt die in Windows enthaltene `winsqlite3.dll` (Patches über Windows Update). Bewusst **nicht** das gebündelte `e_sqlite3` – betroffen von GHSA-2m69-gcr7-jv3q (CVE-2025-6965), ohne gepatchte Version. |
| .NET 8 Runtime + Windows Desktop Runtime | 8.0.x | MIT | Microsoft | self-contained ausgeliefert |
| Microsoft.Windows.SDK.NET (CsWinRT-Projektion) | über TFM `net8.0-windows10.0.19041.0` | MIT | Microsoft | Windows Hello (`UserConsentVerifier`); v1.1: Bluetooth-Funkstatus (`Windows.Devices.Radios`), Kameraliste/-zugriffstest (`DeviceInformation`, `MediaCapture`) |
| Microsoft.Diagnostics.Tracing.TraceEvent (v1.3) | 3.1.13 | MIT | Microsoft / .NET Foundation | FPS-Messung für das In-Game-Overlay über Windows-ETW (Anbieter Microsoft-Windows-DXGI/-D3D9, Prinzip PresentMon) – liest nur Ereignisse, keine Injection in Spiele, kein Treiber |
| Microsoft.Web.WebView2 (v1.3) | 1.0.2903.40 | BSD-3-Clause | Microsoft | eingebettete Webansicht für speedtest.ch; nutzt die in Windows vorhandene Microsoft Edge WebView2-Runtime (wird nicht mitgeliefert), Navigation nur zu `*.speedtest.ch` (`EmbeddedBrowserPolicy`) |

**v1.3: zwei neue Pakete** (TraceEvent, WebView2 – siehe Tabelle). Das Infobereich-Symbol nutzt `System.Windows.Forms.NotifyIcon` aus der Windows Desktop Runtime (kein zusätzliches Paket). Die WebView2-Runtime ist eine Windows-Komponente von Microsoft; fehlt sie, verweist die App auf die offizielle Downloadseite.

**v1.1: keine neuen NuGet-Pakete.** Der PDF-Bericht wird mit einem eigenen, kleinen PDF-Writer erzeugt (Standardschriften Helvetica, keine eingebetteten Fremdbibliotheken oder nativen Binärdateien). Audio-Test und Mikrofonpegel nutzen `winmm.dll` (Windows).

## Build/Test (nicht ausgeliefert)

| Paket | Version | Lizenz |
|---|---|---|
| WixToolset.Sdk / UI.wixext / Util.wixext | 5.0.2 | MS-RL |
| xunit | 2.9.0 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |

## Externe Dienste (nur auf Benutzeraktion)

| Dienst | Zweck | Daten | Austauschbar |
|---|---|---|---|
| `https://api.ipify.org` / `api6.ipify.org` | Öffentliche IP | sieht nur die eigene IP | `NetworkService.PublicIpV4Url` |
| `https://speed.cloudflare.com` | Speedtest | Test-Payload, keine Nutzerdaten | `SpeedtestService.BaseUrl` – vor kommerziellem Vertrieb Nutzungsbedingungen prüfen oder eigenen Server betreiben |
| `https://www.microsoft.com` (HEAD) | Netzwerkdiagnose | – | |
| `1.1.1.1` (ICMP) | Ping/Paketverlust | – | |
| `1.1.1.1`, `8.8.8.8` (ICMP, TCP 443) | Komponententest „Internet“ (nur auf Benutzeraktion) | – | |
| `www.microsoft.com`, `www.cloudflare.com` (DNS) | Komponententest „DNS“ | – | |
| `time.windows.com`, `pool.ntp.org` (UDP 123, SNTP) | Komponententest „Systemzeit“ – nur lesend, die Uhrzeit wird nie gesetzt | – | `SntpClient.Servers` |
| Windows Update (Microsoft) | Treiber-/Windows-Updates | Standard-WU-Kommunikation von Windows | per Einstellung „Beim Start nach Updates suchen“ |
| winget-Quelle (Microsoft) | Software-Updates | Paketliste | wie oben |
| Update-Manifest des Herausgebers | App-Updates | – | Einstellungen → Update-Quelle (leer = aus) |
| winget-Quelle / Microsoft Store (v1.2) | New PC Setup: Paketmanifest (`winget show`) und Installation | Paket-ID; Download direkt vom Hersteller | nur Pakete aus `SoftwareCatalog`; Installer-URL muss zur hinterlegten Herstellerdomain passen |
| Herstellerdomains laut Katalog (v1.2), z. B. `download.mozilla.org`, `adobe.com`, `7-zip.org`, `videolan.org`, `github.com/<offizielles Repo>/` | Download der Installer durch winget | – | `SoftwareCatalog.All` |
| `https://officecdn.microsoft.com/pr/wsus/setup.exe` (v1.2) | Office Deployment Tool für Microsoft 365 Apps | – | `OfficeDeploymentService.OdtUrl`; Office-Setup lädt danach selbst von Microsoft-CDNs |
| `https://api.github.com/repos/Cisco-Talos/clamav` (v1.2) | Projektprüfung ClamAV (letzte Version, archiviert, Aktivität) | anonyme API-Anfrage, keine Nutzerdaten | `ClamAvService.ProjectApi` |
| `database.clamav.net` (v1.2, freshclam) | ClamAV-Signatur-Updates | Standard-freshclam-Anfrage | `freshclam.conf` unter `%LOCALAPPDATA%\UltimateX\clamav` |
| `https://www.google.com/search?q=…` (v1.2) | Websuche nach Spielanforderungen/Software – öffnet nur den Standardbrowser, ULTIMATE X ruft selbst nichts ab | Suchbegriff aus Hardwarenamen (keine Seriennummern/Adressen) | `SearchQueries.SearchBase` |
| `speed.cloudflare.com`, `1.1.1.1` (v1.2, Netzwerk-Benchmark) | wie Speedtest/Ping oben, Speedtest optional | – | |
| `https://standards-oui.ieee.org/oui/oui.csv` (seit v1.1, hier ergänzt) | Herstellerliste des Netzwerk-Scanners („Herstellerliste laden (IEEE)“) | – | `NetworkScannerService` |
| `https://msdl.microsoft.com/download/symbols` (Absturzanalyse, ergänzt) | Symbole für den Microsoft-Debugger (kd.exe) bei „Ursache ermitteln“ | Symbol-Anfragen des Debuggers; das Absturzabbild verlässt den PC nicht | nur wenn die Debugging Tools installiert sind |
| `https://store.steampowered.com/api/storesearch/` und `/api/appdetails` (v1.3) | Can My PC Run It: Spiele-Anforderungen laden („Online suchen“, „Fehlende Anforderungen online laden“) – ohne Steam-Konto | nur Spielname (Suche) bzw. Steam-AppID; feste Parameter `l=english`, `cc=CH`, `filters=basic`; keine Hardware-/Konto-/Gerätedaten; 1,5 s Pause zwischen Abrufen, Abbruch bei HTTP 429 | `SteamRequirements.StoreHost`; Ergebnis lokal in `games-user.json` |
| `https://www.speedtest.ch/` (v1.3) | eingebetteter Speedtest (WebView2) – wird erst geladen, wenn „speedtest.ch (eingebettet)“ gewählt ist | was die Webseite selbst abfragt (eigener Profilordner `%LOCALAPPDATA%\UltimateX\WebView2`); ULTIMATE X liest die Seite nicht aus | `EmbeddedBrowserPolicy.SpeedtestChHome`; es gelten die Bedingungen von speedtest.ch |
| `https://www.nvidia.com/Download/API/lookupValueSearch.aspx`, `https://gfwsl.geforce.com/…/AjaxDriverService.php`, `https://*.download.nvidia.com/` (v1.3) | New PC Setup: passenden NVIDIA-Treiber suchen (beim Öffnen der Seite bzw. „Erneut suchen“) und auf Wunsch laden | nur Produktserie/Produkt-ID der Karte, Betriebssystem (Windows 10/11 64-Bit), WHQL/DCH; keine persönlichen Daten | `NvidiaDriver` (Core), `NvidiaDriverService`; Paket nach `%LOCALAPPDATA%\UltimateX\setup\nvidia`, Signatur „NVIDIA Corporation“ |
| `https://www.geekbench.com/download/` (v1.3) | nur Link im Standardbrowser („Offizielle Download-Seite öffnen“) – ULTIMATE X lädt nichts herunter | – | |
| `https://developer.microsoft.com/microsoft-edge/webview2/` (v1.3) | nur Link im Standardbrowser, wenn die WebView2-Runtime fehlt | – | `SpeedtestChBrowser.RuntimeDownloadUrl` |
| Vom Benutzer genannte Ziele (v1.3, Netzwerk-Werkzeuge) | Traceroute (ICMP, max. 30 Hops) und Port-Prüfung (TCP-Verbindungsaufbau, ein Ziel, max. 20 Ports, keine Daten) | – | Ziel per `InputValidator` (IP oder Hostname) |
| `1.1.1.1` (v1.3, Overlay-Ping) | Ping alle 3 s, nur wenn als Ping-Ziel „Internet“ gewählt und das Overlay sichtbar ist (Standard: Router, lokal) | – | |
| UPnP-SSDP-Multicast im lokalen Netz (v1.3, Sicherheits-Check) | Prüfung „UPnP am Router“ (Suche nach InternetGatewayDevice) | verlässt das lokale Netz nicht | |
| Wake-on-LAN-Broadcast im lokalen Subnetz (v1.3) | Magic Packet an eine MAC-Adresse | nur eigenes Subnetz, kein Internet | |

### Mitgelieferte Daten (keine Online-Abfrage)

| Datei | Inhalt | Herkunft |
|---|---|---|
| `UltimateX.Core/Analysis/games.json` | veröffentlichte Mindest-/Empfehlungsanforderungen von 42 Spielen | Herstellerangaben (Fakten), Stand 2026; FPS werden daraus nur geschätzt |
| `HardwareDatabase.cs` | FP32-Herstellerwerte und grobe Leistungsindizes von ~130 GPUs, CPU-Klassen aus Modellnamen | Herstellerdatenblätter; Indizes sind Schätzungen (ESTIMATED) |
| `PerformanceScore.cs` (v1.3 neu kalibriert) | Referenzwerte der Score-Skala (100 ≈ Spitzenklasse 2026) | Schätzungen (ESTIMATED) für die eigenen Messmethoden |
| `SecurityAudit.cs` (v1.3) | Support-Tabelle der Windows-Versionen (Stand Oktober 2026) | Microsoft-Lebenszyklusangaben |
| `NetworkTools.cs` (v1.3) | feste DNS-Anbieter Cloudflare (1.1.1.1), Quad9 (9.9.9.9), Google (8.8.8.8) | öffentliche Anbieterangaben |
| `PrivacyCatalog.cs` (v1.3) | feste Tabelle der sechs Datenschutz-Einstellungen (Registry-Werte) | Windows-Dokumentation |

### Fremdsoftware, die auf Wunsch installiert wird (nicht Teil des MSI)

ULTIMATE X liefert keine dieser Programme mit, sondern startet auf ausdrücklichen Wunsch die Installation aus der offiziellen Quelle. **Geekbench** (Primate Labs, v1.3) wird weder mitgeliefert noch heruntergeladen: ULTIMATE X startet nur ein bereits installiertes Geekbench aus `Program Files\Geekbench 6|5` ohne Parameter. Es gelten die Lizenzen der Hersteller, z. B. ClamAV (GPL-2.0, Cisco), Microsoft 365 Apps (Microsoft-Lizenz, eigene Aktivierung), Firefox (MPL-2.0), 7-Zip (LGPL), VLC (GPL-2.0). Keine neuen NuGet-Abhängigkeiten in v1.2.

## SBOM

`build.ps1` erzeugt `artifacts/dependencies.json` (`dotnet list package --include-transitive --format json`). Für ein CycloneDX-SBOM kann zusätzlich das .NET-Tool `CycloneDX` im CI ausgeführt werden (`dotnet CycloneDX UltimateX.sln -o artifacts`).

## Bewusst NICHT verwendet

- **LibreHardwareMonitor / OpenHardwareMonitor** – benötigen den WinRing0-Kernel-Treiber (CVE-2020-14979, Defender-Blockade).
- **Inoffizielle Driver-Updater-Datenbanken** – keine vertrauenswürdige Quelle.
- **DI-Container, Chart-Bibliotheken** – nicht nötig; weniger Angriffsfläche.
- **PDF-Bibliotheken (z. B. QuestPDF/SkiaSharp)** – v1.1 nutzt einen eigenen PDF-Writer: keine Lizenzbedingungen, keine nativen Abhängigkeiten.
- **Overlay-/FPS-Hooks (DLL-Injection, Present-Hooks, z. B. RTSS-Ansatz)** – v1.3 misst FPS ausschließlich über ETW; keine Injection in Spielprozesse (Anti-Cheat, Stabilität).
- **Backup-Bibliotheken/VSS-Treiber** – v1.3 nutzt Windows-Bordmittel (wbadmin, pnputil, RecoveryDrive) und eine eigene Datei-Backup-Engine ohne Schattenkopien.
