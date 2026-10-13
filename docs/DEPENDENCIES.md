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

## SBOM

`build.ps1` erzeugt `artifacts/dependencies.json` (`dotnet list package --include-transitive --format json`). Für ein CycloneDX-SBOM kann zusätzlich das .NET-Tool `CycloneDX` im CI ausgeführt werden (`dotnet CycloneDX UltimateX.sln -o artifacts`).

## Bewusst NICHT verwendet

- **LibreHardwareMonitor / OpenHardwareMonitor** – benötigen den WinRing0-Kernel-Treiber (CVE-2020-14979, Defender-Blockade).
- **Inoffizielle Driver-Updater-Datenbanken** – keine vertrauenswürdige Quelle.
- **DI-Container, Chart-Bibliotheken** – nicht nötig; weniger Angriffsfläche.
- **PDF-Bibliotheken (z. B. QuestPDF/SkiaSharp)** – v1.1 nutzt einen eigenen PDF-Writer: keine Lizenzbedingungen, keine nativen Abhängigkeiten.
