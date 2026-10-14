<#
.SYNOPSIS
  Baut ULTIMATE X Windows Control: Tests → Publish (self-contained, x64) → Signierung → MSI → Prüfsummen.

.EXAMPLE
  .\build.ps1                                   # Entwicklungs-Build (unsigniert)
  .\build.ps1 -Version 1.0.0 -CertThumbprint ABC123...   # Release-Build mit Authenticode-Signatur

.NOTES
  Voraussetzungen: .NET 8 SDK, Windows 10/11. Für die Signierung: signtool.exe (Windows SDK)
  und ein Code-Signing-Zertifikat im Zertifikatsspeicher (empfohlen: EV/HSM-gestützt).
#>
[CmdletBinding()]
param(
    [string]$Version = "1.1.0",
    [string]$Configuration = "Release",
    [string]$CertThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root      = $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish   = Join-Path $artifacts "publish\"
$app       = Join-Path $root "src\UltimateX.App\UltimateX.App.csproj"
$installer = Join-Path $root "installer\UltimateX.Installer.wixproj"

function Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }

if (Test-Path $artifacts) {
    try { Remove-Item $artifacts -Recurse -Force -ErrorAction Stop }
    catch {
        # Dateien sind gesperrt (z. B. App läuft aus artifacts\publish oder Setup ist offen) → eigener Ausgabeordner.
        $artifacts = Join-Path $root ("artifacts-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
        $publish   = Join-Path $artifacts "publish\"
        Write-Host "Hinweis: 'artifacts' ist gesperrt, Ausgabe nach $artifacts" -ForegroundColor Yellow
    }
}
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$release = Join-Path $artifacts "release"
New-Item -ItemType Directory -Path $release -Force | Out-Null
Start-Transcript -Path (Join-Path $release "build-log.txt") -Force | Out-Null

Step "Restore"
dotnet restore (Join-Path $root "UltimateX.sln") | Out-Host
if ($LASTEXITCODE) { throw "Restore fehlgeschlagen" }

Step "Build ($Configuration)"
dotnet build (Join-Path $root "UltimateX.sln") -c $Configuration --no-restore -p:Version=$Version -p:Platform=x64 | Out-Host
if ($LASTEXITCODE) { throw "Build fehlgeschlagen" }

if (-not $SkipTests) {
    Step "Tests"
    dotnet test (Join-Path $root "tests\UltimateX.Core.Tests\UltimateX.Core.Tests.csproj") -c $Configuration --no-build -p:Platform=x64 --logger "trx;LogFileName=tests.trx" --results-directory (Join-Path $artifacts "test-results") | Out-Host
    if ($LASTEXITCODE) { throw "Tests fehlgeschlagen – Release blockiert" }
}

Step "Bekannte Schwachstellen in Abhängigkeiten prüfen"
# JSON-Ausgabe: unabhängig von der Sprache der .NET-CLI (deutsche Ausgabe wurde früher nicht erkannt).
$vulnJson = dotnet list (Join-Path $root "UltimateX.sln") package --vulnerable --include-transitive --format json 2>&1 | Out-String
if ($LASTEXITCODE) { Write-Host $vulnJson; throw "Schwachstellen-Prüfung konnte nicht ausgeführt werden" }
$found = @()
foreach ($proj in @(($vulnJson | ConvertFrom-Json).projects)) {
    if (-not ($proj.PSObject.Properties.Name -contains 'frameworks')) { continue }   # Projekt ohne Funde
    foreach ($fw in @($proj.frameworks)) {
        foreach ($kind in 'topLevelPackages', 'transitivePackages') {
            if (-not ($fw.PSObject.Properties.Name -contains $kind)) { continue }
            foreach ($pkg in @($fw.$kind)) {
                if ($pkg.PSObject.Properties.Name -contains 'vulnerabilities') { $found += $pkg }
            }
        }
    }
}
if ($found.Count -gt 0) {
    $found | ForEach-Object { Write-Host ("  {0} {1}: {2}" -f $_.id, $_.resolvedVersion, (($_.vulnerabilities | ForEach-Object { $_.advisoryurl }) -join ", ")) -ForegroundColor Red }
    throw "Verwundbare Pakete gefunden – Release blockiert"
}
Write-Host "Keine bekannten Schwachstellen in Abhängigkeiten."

Step "Publish (self-contained, win-x64)"
dotnet publish $app -c $Configuration -r win-x64 --self-contained true -o $publish -p:Version=$Version -p:DebugType=none | Out-Host
if ($LASTEXITCODE) { throw "Publish fehlgeschlagen" }
if (-not (Test-Path (Join-Path $publish "UltimateX.AdminHelper.exe"))) { throw "Admin-Helper fehlt im Publish-Ordner" }

function Sign-Files([string[]]$files) {
    if (-not $CertThumbprint) { return }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\signtool.exe" -ErrorAction SilentlyContinue |
                Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe nicht gefunden (Windows SDK installieren)" }
    & $signtool.FullName sign /sha1 $CertThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 /d "ULTIMATE X Windows Control" $files
    if ($LASTEXITCODE) { throw "Signierung fehlgeschlagen" }
    & $signtool.FullName verify /pa /all $files
    if ($LASTEXITCODE) { throw "Signaturprüfung fehlgeschlagen" }
}

if ($CertThumbprint) {
    Step "Authenticode-Signierung (EXE/DLL)"
    $own = Get-ChildItem $publish -Include "UltimateX*.exe","UltimateX*.dll" -Recurse | ForEach-Object FullName
    Sign-Files $own
} else {
    Write-Warning "Kein Zertifikat angegeben: Entwicklungs-Build bleibt UNSIGNIERT (nicht für Verteilung geeignet)."
}

Step "MSI bauen (WiX)"
dotnet build $installer -c $Configuration -p:PublishDir=$publish -p:ProductVersion=$Version -o (Join-Path $artifacts "msi") | Out-Host
if ($LASTEXITCODE) { throw "MSI-Build fehlgeschlagen" }
$msi = Get-ChildItem (Join-Path $artifacts "msi") -Filter *.msi | Select-Object -First 1
# WiX legt die MSI als Hardlink auf obj\ ab – eigenständige Kopie erzeugen (Signatur/Weitergabe betrifft nur diese Datei).
$tmpMsi = "$($msi.FullName).tmp"
Copy-Item $msi.FullName $tmpMsi -Force
Remove-Item $msi.FullName -Force
Move-Item $tmpMsi $msi.FullName
$msi = Get-Item $msi.FullName
if ($CertThumbprint) { Sign-Files @($msi.FullName) }

Step "MSI prüfen (administrative Extraktion, Inhalt, Eigenschaften)"
$extract = Join-Path $artifacts "msi-check"
New-Item -ItemType Directory -Path $extract -Force | Out-Null
$wi = New-Object -ComObject WindowsInstaller.Installer
$db = $wi.GetType().InvokeMember("OpenDatabase", "InvokeMethod", $null, $wi, @($msi.FullName, 0))
function MsiQuery($sql) {
    $view = $db.GetType().InvokeMember("OpenView", "InvokeMethod", $null, $db, @($sql))
    $view.GetType().InvokeMember("Execute", "InvokeMethod", $null, $view, $null) | Out-Null
    $values = @()
    while ($rec = $view.GetType().InvokeMember("Fetch", "InvokeMethod", $null, $view, $null)) {
        $values += $rec.GetType().InvokeMember("StringData", "GetProperty", $null, $rec, 1)
    }
    $view.GetType().InvokeMember("Close", "InvokeMethod", $null, $view, $null) | Out-Null
    return ,$values
}
$r = MsiQuery "SELECT Value FROM Property WHERE Property='ProductVersion'"; $msiVersion = [string]$r[0]
$r = MsiQuery "SELECT Value FROM Property WHERE Property='ProductName'"; $msiName = [string]$r[0]
$r = MsiQuery "SELECT Value FROM Property WHERE Property='UpgradeCode'"; $upgrade = [string]$r[0]
# Dateinamen aus der File-Tabelle ("KURZ~1|LangerName" → LangerName)
$r = MsiQuery "SELECT FileName FROM File"
$fileNames = @(foreach ($n in $r) { ([string]$n -split '\|')[-1] })
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($db) | Out-Null
[System.Runtime.InteropServices.Marshal]::ReleaseComObject($wi) | Out-Null
if ($msiVersion -ne $Version) { throw "MSI-Prüfung: ProductVersion $msiVersion statt $Version" }
if (-not $upgrade) { throw "MSI-Prüfung: UpgradeCode fehlt (Upgrades wären nicht möglich)" }
function Require($name) { if ($fileNames -notcontains $name) { throw "MSI-Prüfung: $name fehlt" } }
Require "UltimateX.exe"; Require "UltimateX.AdminHelper.exe"; Require "UltimateX.Core.dll"; Require "UltimateX.Services.dll"
Require "Bedienungsanleitung.pdf"
Require "coreclr.dll"; Require "hostfxr.dll"; Require "PresentationFramework.dll"   # self-contained: keine separate .NET-Installation
$dev = $fileNames | Where-Object { $_ -like "*.pdb" -or $_ -like "*Tests*.dll" -or $_ -like "xunit*" -or $_ -like "Microsoft.TestPlatform*" }
if ($dev) { throw ("MSI-Prüfung: Entwicklungsdateien enthalten: " + ($dev -join ", ")) }
# Administrative Extraktion (ohne Installation) und Versionsprüfung der EXE
$p = Start-Process msiexec.exe -ArgumentList @("/a", "`"$($msi.FullName)`"", "/qn", "TARGETDIR=`"$extract`"") -Wait -PassThru
if ($p.ExitCode -eq 0) {
    $exe = Get-ChildItem $extract -Recurse -File -Filter "UltimateX.exe" | Select-Object -First 1
    if (-not $exe) { throw "MSI-Prüfung: UltimateX.exe nach Extraktion nicht gefunden" }
    $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe.FullName).ProductVersion
    if (-not $fileVersion.StartsWith($Version)) { throw "MSI-Prüfung: UltimateX.exe hat Version $fileVersion statt $Version" }
} else {
    Write-Warning "Administrative Extraktion nicht möglich (Exitcode $($p.ExitCode)) – Prüfung über die MSI-Tabellen."
}
$installed = $fileNames
Write-Host "MSI-Inhalt OK: $($installed.Count) Dateien, $msiName $msiVersion, UpgradeCode $upgrade, keine Entwicklungsdateien."
Remove-Item $extract -Recurse -Force -ErrorAction SilentlyContinue

Step "SBOM und Prüfsummen"
dotnet list (Join-Path $root "UltimateX.sln") package --include-transitive --format json > (Join-Path $artifacts "dependencies.json")
$hash = (Get-FileHash $msi.FullName -Algorithm SHA256).Hash
"$hash  $($msi.Name)" | Out-File (Join-Path $artifacts "SHA256SUMS.txt") -Encoding ascii

Step "Release-Ordner"
Copy-Item $msi.FullName $release -Force
Copy-Item (Join-Path $artifacts "SHA256SUMS.txt") $release -Force
Copy-Item (Join-Path $artifacts "dependencies.json") (Join-Path $release "SBOM-dependencies.json") -Force
$notes = Join-Path $root "docs\RELEASE_NOTES_$Version.md"
if (Test-Path $notes) { Copy-Item $notes (Join-Path $release "RELEASE_NOTES.md") -Force }
$trx = Get-ChildItem (Join-Path $artifacts "test-results") -Filter *.trx -ErrorAction SilentlyContinue | Select-Object -First 1
if ($trx) {
    Copy-Item $trx.FullName (Join-Path $release "test-results.trx") -Force
    [xml]$t = Get-Content $trx.FullName
    $c = $t.TestRun.ResultSummary.Counters
    $securityClasses = "PathGuardTests|CommandCatalogTests|InputValidatorTests|MaskerTests|SecurityTests|HelperPipeProtocolTests|LivePingTests|PdfReportTests"
    $sec = @($t.TestRun.Results.UnitTestResult | Where-Object { $_.testName -match "($securityClasses)\." })
    $secPassed = @($sec | Where-Object outcome -eq "Passed").Count
    @(
        "ULTIMATE X Windows Control $Version – Testergebnisse",
        "Gesamt: $($c.total) · bestanden: $($c.passed) · fehlgeschlagen: $($c.failed)",
        "Security-Tests: $secPassed / $($sec.Count) bestanden",
        "Erstellt: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    ) | Out-File (Join-Path $release "TEST_RESULTS.txt") -Encoding utf8
}

Write-Host "`nFertig:" -ForegroundColor Green
Write-Host "  MSI:     $($msi.FullName)"
Write-Host "  Release: $release"
Write-Host "  SHA-256: $hash"
Stop-Transcript | Out-Null
