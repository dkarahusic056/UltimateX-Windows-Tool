<#
.SYNOPSIS
  Entwickler-Build: Restore + Build (+ optional Tests). Schreibt das komplette Protokoll nach
  artifacts\dev-build.log und nur die Fehler/Warnungen nach artifacts\dev-build-errors.txt.
#>
[CmdletBinding()]
param([switch]$Test, [string]$Configuration = "Debug")

$root = $PSScriptRoot
$out  = Join-Path $root "artifacts"
New-Item -ItemType Directory -Path $out -Force | Out-Null
$log  = Join-Path $out "dev-build.log"
$err  = Join-Path $out "dev-build-errors.txt"

"=== SDKs ===" | Out-File $log -Encoding utf8
dotnet --list-sdks 2>&1 | Out-File $log -Append -Encoding utf8

"=== BUILD ===" | Out-File $log -Append -Encoding utf8
dotnet build (Join-Path $root "UltimateX.sln") -c $Configuration -p:Platform=x64 -clp:NoSummary -nologo 2>&1 | Out-File $log -Append -Encoding utf8
$buildExit = $LASTEXITCODE

$testExit = 0
if ($Test -and $buildExit -eq 0) {
    "=== TEST ===" | Out-File $log -Append -Encoding utf8
    dotnet test (Join-Path $root "tests\UltimateX.Core.Tests\UltimateX.Core.Tests.csproj") -c $Configuration -p:Platform=x64 --no-build -nologo 2>&1 | Out-File $log -Append -Encoding utf8
    $testExit = $LASTEXITCODE
}

$lines = Get-Content $log | Where-Object { $_ -match ': error |: warning |Failed |Fehler|error NU|error MSB' } | Sort-Object -Unique
"BUILD_EXIT=$buildExit TEST_EXIT=$testExit" | Out-File $err -Encoding utf8
$lines | Out-File $err -Append -Encoding utf8
Write-Host "BUILD_EXIT=$buildExit TEST_EXIT=$testExit  ($($lines.Count) Meldungen) -> $err" -ForegroundColor Cyan
