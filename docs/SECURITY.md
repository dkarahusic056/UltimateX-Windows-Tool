# Security – ULTIMATE X Windows Control

Dieses Dokument fasst das Sicherheitskonzept zusammen. Einzelrisiken mit Schutzmaßnahmen und Rest-Risiko stehen in [THREAT_MODEL.md](THREAT_MODEL.md).

## Grundsätze

| Grundsatz | Umsetzung |
|---|---|
| **Least Privilege** | `UltimateX.exe` läuft immer als Standardbenutzer (`asInvoker`). Adminrechte gibt es nur für einzelne Befehle über `UltimateX.AdminHelper.exe` und den Windows-UAC-Dialog. |
| **Keine freie Shell** | Es gibt keinen Codepfad `Execute(string command)`. Erhöhte Befehle sind ein Enum (`AllowedCommand`) mit festem Programmpfad in `System32` und festen Argumenten; der Helper prüft alles erneut. Ping, DNS und Netzwerktests laufen über .NET-APIs, nie über `cmd`/`powershell`. |
| **Abgesicherte IPC** | App ↔ Helper: Named Pipe mit restriktiver ACL, Peer-Prüfung beider Seiten (PID bzw. Programmpfad), Impersonation-Level *Identification*, Sitzungs-Nonce, Sequenznummern, Größen- und Zeitlimits, Schema-Prüfung. Über die Pipe fließt nur Ausgabe – keine Befehle. |
| **Eingabevalidierung** | Allowlist statt Blocklist: Hostnamen/IPs (`InputValidator`), Laufwerksbuchstaben, GUIDs, winget-IDs, Dump-Namen. Pfade über `PathGuard` (Kanonisierung, keine UNC/Device-Pfade, keine Reparse-Points, geschützte Systemordner). |
| **Integrität** | Start: Authenticode-Prüfung der eigenen EXE/DLLs. Signierte App startet den Helper nur, wenn er mit demselben Zertifikat gültig signiert ist. `nvml.dll` nur aus `System32` mit NVIDIA-Signatur. |
| **Sichere Updates** | Nur HTTPS ohne Redirects, Manifest- und Versionsprüfung, SHA-256, Authenticode + gleicher Herausgeber-Thumbprint, erneute Prüfung direkt vor dem Start; jeder Fehler blockiert das Update. Unsignierte Builds aktualisieren sich nie selbst. |
| **Keine Änderungen an Schutzfunktionen** | Defender, Firewall, UAC, Tamper Protection, Secure Boot, TPM und BitLocker werden nur **gelesen**. Es werden nie Defender-Ausnahmen angelegt. |
| **Fail closed** | Signatur-/Hash-Fehler, unbekannter Admin-Befehl, ungültiger Pfad, manipulierter Helper, IPC-Protokollverletzung → Abbruch, Sicherheitsereignis im Security-Log. |
| **Privatsphäre** | Keine Telemetrie. Sensible Werte standardmäßig maskiert, PC-Bericht mit Datenschutzmodus (Standard: an). Kamera/Mikrofon/Tastatur/Maus nur in ausdrücklich gestarteten Tests, ohne Aufzeichnung. |
| **Keine Geheimnisse in Logs** | Keine Passwörter, Tokens, WLAN-Schlüssel, privaten Schlüssel oder Dateiinhalte in Logs. Sicherheitsrelevante Aktionen landen in `security-*.jsonl`. |
| **Keine Kernel-Treiber** | ULTIMATE X installiert und lädt keinen Treiber (WinRing0 ist verwundbar, CVE-2020-14979). Sensorwerte kommen aus Windows-APIs oder lesend aus HWiNFO/LibreHardwareMonitor. |
| **Keine destruktiven Tests** | Komponententests sind standardmäßig nicht-destruktiv; der Datenträger-Schreibtest läuft nur nach Bestätigung mit einer eigenen Temp-Datei. |

## Sensible Aktionen

Ablauf zentral in `SensitiveActionGuard`: **Authentifizieren** (Windows Hello, Security Session 0–5 min) → **Erklären** → **Bestätigen** → **Ausführen** (ggf. UAC) → **Protokollieren**. Die Session endet sofort beim Sperren, Benutzerwechsel oder über den LOCK-Button.

## Sicherheitsprüfungen im Build

- `build.ps1`: Release-Build (Nullable-Warnungen = Fehler), xUnit-Tests inkl. Security-Tests, Schwachstellenprüfung aller NuGet-Pakete (`dotnet list package --vulnerable --include-transitive`, blockierend), Publish, MSI, SHA-256, SBOM.
- Security-Tests: `PathGuardTests`, `CommandCatalogTests`, `InputValidatorTests`, `MaskerTests`, `HelperPipeProtocolTests`, `LivePingTests`, `PdfReportTests` (Maskierung), `SecurityTests`.

## Schwachstellen melden

Bitte Sicherheitsprobleme nicht öffentlich melden, sondern direkt an den Herausgeber (Kontakt im Release). Angaben: Version, Windows-Build, Schritte zur Reproduktion, erwartetes/tatsächliches Verhalten.

## Grenzen

Keine Software ist „100 % unhackbar“. Ein Angreifer mit Administrator- oder Kernelrechten kann jede Anwendung manipulieren. ULTIMATE X reduziert die eigene Angriffsfläche, ersetzt aber keinen Virenschutz und erkennt kein bereits kompromittiertes System zuverlässig.
