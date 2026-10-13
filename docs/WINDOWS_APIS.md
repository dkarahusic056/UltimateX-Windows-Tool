# Verwendete Windows-APIs (§96, §112)

Grundsatz: offizielle, dokumentierte APIs; Shell-Werkzeuge nur über die Allowlist.

| Bereich | API | Rechte | Hinweise |
|---|---|---|---|
| CPU, GPU, RAM-Module, Board, BIOS, OS | WMI `Win32_Processor`, `Win32_VideoController`, `Win32_PhysicalMemory(Array)`, `Win32_BaseBoard`, `Win32_BIOS`, `Win32_OperatingSystem` | Benutzer | VRAM > 4 GB aus Registry `HardwareInformation.qwMemorySize` (WMI-Feld ist 32-Bit) |
| Arbeitsspeicher live | `GlobalMemoryStatusEx` | Benutzer | |
| CPU-Last | `GetSystemTimes` | Benutzer | kein Performance-Counter nötig |
| Datenträgeraktivität | PDH `PhysicalDisk\% Idle Time` | Benutzer | fällt bei Fehlern auf „nicht verfügbar“ zurück |
| Netzwerkdurchsatz/Adapter | `System.Net.NetworkInformation` | Benutzer | |
| Datenträger-Gesundheit | WMI `MSFT_PhysicalDisk` (Storage) | Benutzer | SMART-Details (`MSFT_StorageReliabilityCounter`) erfordern Admin → Phase 2 |
| Laufwerke | `DriveInfo` | Benutzer | |
| Firmware | `GetFirmwareType` | Benutzer | |
| Secure Boot | Registry `...\SecureBoot\State` | Benutzer | nur lesend |
| TPM | `Tbsi_GetDeviceInfo` (tbs.dll) | Benutzer | keine Schlüsseloperationen |
| BitLocker | WMI `Win32_EncryptableVolume` (Admin) bzw. Shell-Property `System.Volume.BitLockerProtection` | Benutzer/Admin | Recovery-Keys werden nie gelesen |
| Defender | WMI `MSFT_MpComputerStatus` | Benutzer | nur lesend |
| Drittanbieter-AV | WMI `root\SecurityCenter2\AntiVirusProduct` | Benutzer | |
| Firewall | Registry (Policy vor lokal) | Benutzer | Änderungen nur über Windows-Einstellungen |
| UAC-Status | Registry `Policies\System` | Benutzer | |
| Signaturen | `WinVerifyTrust` (WINTRUST_ACTION_GENERIC_VERIFY_V2), Revocation nur aus Cache | Benutzer | Katalogsignaturen werden in Phase 1 nicht ausgewertet |
| Treiber | WMI `Win32_PnPSignedDriver` | Benutzer | |
| Programme | Registry `Uninstall` (64/32 Bit, HKCU) | Benutzer | |
| Autostart | Registry `Run` + `Explorer\StartupApproved` (wie Task-Manager) | Benutzer (HKCU) / Admin (HKLM, Phase 2) | |
| Event Logs | `EventLogReader` (XPath) | Benutzer | System/Application |
| Dienste | WMI `Win32_Service` | Benutzer | nur lesend |
| Neustartstatus | Registry CBS/WU/PendingFileRename | Benutzer | |
| Papierkorb | `SHQueryRecycleBin`, `SHEmptyRecycleBin` | Benutzer | |
| Power | `LockWorkStation`, `SetSuspendState`, `ExitWindowsEx(EWX_LOGOFF)`, `shutdown.exe` (Allowlist) | Benutzer | |
| Authentifizierung | WinRT `UserConsentVerifier` (Windows Hello) | Benutzer | keine eigene Passwortabfrage |
| Reparatur | `sfc.exe`, `dism.exe`, `chkdsk.exe`, `fsutil.exe dirty set`, `mdsched.exe` | Admin (UAC) | ausschließlich über `CommandCatalog` + AdminHelper |
| Titelleiste | `DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE)` | Benutzer | |

## Weitere APIs

| Bereich | API | Rechte | Hinweise |
|---|---|---|---|
| GPU-Last / VRAM | PDH-Kategorien `GPU Engine` (Utilization Percentage), `GPU Adapter Memory` (Dedicated Usage) | Benutzer | gleiche Quelle wie Task-Manager |
| GPU-Temperatur/Lüfter (NVIDIA) | NVML (`nvml.dll` aus System32, signaturgeprüft) | Benutzer | nur lesend |
| SMART | WMI `MSFT_StorageReliabilityCounter` | Admin (Helper) | Temperatur, Betriebsstunden, Wear, Fehlerzähler |
| WLAN | Native Wi-Fi API `WlanQueryInterface`, `WlanGetNetworkBssList` | Benutzer (+ Standortfreigabe ab Win 11 24H2) | |
| Monitore | `EnumDisplayDevices`, `EnumDisplaySettings`, `GetDpiForMonitor`, `QueryDisplayConfig`, `DisplayConfigGetDeviceInfo` | Benutzer | HDR über ADVANCED_COLOR_INFO |
| Desktop Organizer | `IShellWindows::FindWindowSW(SWC_DESKTOP)` → `IShellBrowser` → `IFolderView::SelectAndPositionItems` | Benutzer | nur Positionen |
| Treiber/Windows-Updates | Windows Update Agent API (`Microsoft.Update.Session`, Searcher/Downloader/Installer) | Suche: Benutzer, Installation: Admin (Helper) | |
| Software-Updates | `winget upgrade` (Allowlist) | Benutzer | Installer fordern UAC selbst an |
| Wiederherstellungspunkte | WMI `root\default:SystemRestore.CreateRestorePoint` | Admin (Helper) | |
| HKLM-Autostart | Registry `StartupApproved` (HKLM) | Admin (Helper) | |
| Geräte/Fehlercodes | WMI `Win32_PnPEntity` | Benutzer | Technician Mode |
| Akku | WMI `Win32_Battery`, `root\wmi:BatteryStaticData/BatteryFullChargedCapacity` | Benutzer | |
| Prozesse | `Process`, `QueryFullProcessImageName` | Benutzer | |
| Benachrichtigungen | WinRT `ToastNotificationManager` + AppUserModelID | Benutzer | Shortcut-AUMID aus dem MSI |
| Verlaufsdaten | SQLite (`Microsoft.Data.Sqlite`) | Benutzer | |
