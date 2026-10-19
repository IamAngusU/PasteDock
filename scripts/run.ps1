[CmdletBinding()]
param([switch]$Bootstrap)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'bootstrap.ps1')
Push-Location $root
try {
    if ($env:OS -ne 'Windows_NT') { throw 'PasteDock Desktop requires Windows.' }
    $dotnet = Resolve-PasteDockDotNet -Root $root -AllowDownload:$Bootstrap
    & $dotnet run --project src/PasteDock.Desktop/PasteDock.Desktop.csproj -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'The development run failed. Inspect the compiler or startup output above.' }
} finally { Pop-Location }
