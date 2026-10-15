# ULTIMATE X Windows Control 1.3.1 – Release Notes

Wartungs- und Erweiterungsversion zu [1.3.0](RELEASE_NOTES_1.3.0.md). Installiert sich als Update über 1.3.0 (Einstellungen und Daten bleiben erhalten).

## New PC Setup
- **NVIDIA-Grafiktreiber automatisch:** Karte wird erkannt, der passende neueste Game-Ready-Treiber (WHQL) direkt bei NVIDIA gesucht und – falls neuer – vorgemerkt; Download nur von `download.nvidia.com`, Start erst nach Signaturprüfung „NVIDIA Corporation“ (UAC, NVIDIA-Installer mit eigener Oberfläche). AMD: Link zur AMD-Treibererkennung; Intel: Intel Driver & Support Assistant.
- **Neue Programme:** OneDrive, WinRAR, HWiNFO, Intel DSA, CPU-Z, GPU-Z, CrystalDiskInfo/-Mark, Logitech G HUB, Corsair iCUE, GOG GALAXY, EA app, Ubisoft Connect, Battle.net, Notepad++, Everything, ShareX, PowerShell 7, Thunderbird, Spotify, GIMP, Paint.NET, Audacity, HandBrake, KeePassXC, Malwarebytes, TeamViewer. Chrome gehört zum Profil BASIC.
- **Microsoft Office** steht jetzt direkt über der Programmliste.

## Overlay
- Flackern behoben: Zeilen werden nicht mehr jede Sekunde neu aufgebaut, die Breite springt nicht mehr mit wechselnden Zahlen, keine Schatteneffekte mehr (wurden in Software gerendert).

## Sicherheit
- Neuer Befehl `NvidiaDriverInstall` in der `CommandCatalog`-Allowlist: fester Pfad `%LOCALAPPDATA%\UltimateX\setup\nvidia\nvidia-driver.exe`, keine Parameter; gestartet nur von der App per UAC nach Authenticode-Prüfung („NVIDIA Corporation“) bei gesperrter Datei – der Admin-Helper lehnt ihn ab (wie das Office Deployment Tool).
- Download nur über HTTPS von `*.download.nvidia.com` (auch nach Weiterleitungen), höchstens 2 GB; die Treibersuche fragt nur `www.nvidia.com` und `gfwsl.geforce.com` ab.
- Neue Programme sind an die offiziellen Herstellerdomains gebunden (geprüft mit `winget show`), z. B. WinRAR → `rarlab.com`, OneDrive → `oneclient.sfx.ms`, HWiNFO → `hwinfo.com`.

## Tests
- 830+ Unit-Tests, neu `V13DriverTests` (Versionsumrechnung, Produktsuche, Treiberabfrage, Download-Prüfung, Allowlist).
