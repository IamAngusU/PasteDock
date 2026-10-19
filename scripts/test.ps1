[CmdletBinding()]
param([ValidateSet('Debug', 'Release')][string]$Configuration = 'Release', [switch]$Bootstrap)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'bootstrap.ps1')
Push-Location $root
try {
    $dotnet = Resolve-PasteDockDotNet -Root $root -AllowDownload:$Bootstrap
    $runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $reports = Join-Path $root "artifacts\reports\test-$runId"
    New-Item -ItemType Directory -Force -Path $reports | Out-Null
    & $dotnet run --project tests/PasteDock.Core.Tests -c $Configuration -- --json (Join-Path $reports 'core-tests.json')
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    if ($env:OS -eq 'Windows_NT') {
        & $dotnet build PasteDock.slnx -c $Configuration
        if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
        & $dotnet run --no-build --project src/PasteDock.Desktop -c $Configuration -- --diagnostics --report (Join-Path $reports 'native-dev.json')
        if ($LASTEXITCODE -ne 0) { throw 'Native smoke checks failed.' }
        & $dotnet run --no-build --project src/PasteDock.Desktop -c $Configuration -- --ui-smoke --report (Join-Path $reports 'ui-dev.json')
        if ($LASTEXITCODE -ne 0) { throw 'WPF smoke checks failed.' }
    }
    Write-Host "Reports: $reports"
} finally { Pop-Location }
