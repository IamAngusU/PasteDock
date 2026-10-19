[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$FrameworkDependent,
    [switch]$SkipTests,
    [switch]$Bootstrap
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'bootstrap.ps1')

function Invoke-NativeCheck {
    param([string]$Executable, [string]$Mode, [string]$Report)
    $process = Start-Process -FilePath $Executable -ArgumentList @($Mode, '--report', ('"' + $Report + '"')) -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(120000)) {
        try { $process.Kill() } catch { }
        throw "$Mode exceeded its two-minute deadline. Packaging was stopped."
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Report)) {
        throw "$Mode failed. Inspect artifacts/reports. Packaging was stopped."
    }
    $result = Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json
    if ($result.status -ne 'passed') { throw "$Mode did not produce a passing report. Packaging was stopped." }
}

Push-Location $root
try {
    if ($env:OS -ne 'Windows_NT') { throw 'The desktop build script must run on Windows. Core tests can run on Linux.' }
    $dotnet = Resolve-PasteDockDotNet -Root $root -AllowDownload:$Bootstrap
    [xml]$properties = Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The project version is invalid.' }
    if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne "v$version") {
        throw 'The release tag does not match the project version.'
    }
    $flavor = if ($FrameworkDependent) { 'framework-dependent' } else { 'self-contained' }
    $buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
    $name = "PasteDock-$version-$Runtime-$flavor-$buildId"
    $publish = Join-Path $root "artifacts\$name"
    $reports = Join-Path $root "artifacts\reports\$buildId"
    New-Item -ItemType Directory -Force -Path $reports | Out-Null
    if (-not $SkipTests) {
        & $dotnet run --project tests/PasteDock.Core.Tests -c $Configuration -- --json (Join-Path $reports 'core-tests.json')
        if ($LASTEXITCODE -ne 0) { throw 'Core tests failed. Publishing was stopped.' }
    }
    # Every build gets a unique directory. Never delete a computed output tree.
    if (Test-Path -LiteralPath $publish) { throw 'The unique build directory already exists.' }
    $selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
    & $dotnet publish src/PasteDock.Desktop/PasteDock.Desktop.csproj -c $Configuration -r $Runtime --self-contained $selfContained `
        -p:PublishTrimmed=false -p:PublishSingleFile=false -p:ContinuousIntegrationBuild=true -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Windows compilation failed. No distributable was approved.' }
    Copy-Item README.md, README.de.md, START-HERE.md, CHANGELOG.md, LICENSE, SECURITY.md -Destination $publish
    Copy-Item docs -Destination $publish -Recurse
    $nativePassed = $false
    $uiSmokePassed = $false
    $processArchitecture = $env:PROCESSOR_ARCHITECTURE
    $canRun = ($Runtime -eq 'win-x64' -and $processArchitecture -eq 'AMD64') -or ($Runtime -eq 'win-arm64' -and $processArchitecture -eq 'ARM64')
    if (-not $SkipTests -and $canRun) {
        $exe = Join-Path $publish 'PasteDock.exe'
        Invoke-NativeCheck -Executable $exe -Mode '--diagnostics' -Report (Join-Path $reports "native-$Runtime.json")
        $nativePassed = $true
        Invoke-NativeCheck -Executable $exe -Mode '--ui-smoke' -Report (Join-Path $reports "ui-$Runtime.json")
        $uiSmokePassed = $true
    }
    Copy-Item -LiteralPath $reports -Destination (Join-Path $publish 'validation-reports') -Recurse
    $sourceCommit = $null
    $sourceDirty = $null
    if (Get-Command git -ErrorAction SilentlyContinue) {
        $revision = & git -C $root rev-parse --verify HEAD 2>$null
        if ($LASTEXITCODE -eq 0) {
            $sourceCommit = "$revision".Trim()
            $sourceDirty = -not [string]::IsNullOrWhiteSpace((& git -C $root status --porcelain | Out-String))
        }
    }
    $manifest = [ordered]@{
        product = 'PasteDock'; version = $version; buildId = $buildId; runtime = $Runtime; flavor = $flavor
        builtUtc = [DateTime]::UtcNow.ToString('o'); sdk = ((& $dotnet --version) | Out-String).Trim()
        coreTestsRun = (-not $SkipTests); nativeSmokeChecksPassed = $nativePassed
        uiSmokeChecksPassed = $uiSmokePassed; signed = $false; uiAcceptanceTested = $false
        sourceCommit = $sourceCommit; sourceDirty = $sourceDirty
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $publish 'build-manifest.json') -Encoding UTF8
    $archive = Join-Path $root "artifacts\$name.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    Compress-Archive -Path "$publish\*" -DestinationPath $archive -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($archive))" | Set-Content -LiteralPath "$archive.sha256" -Encoding ASCII
    $latest = [ordered]@{ buildId = $buildId; executable = (Join-Path $publish 'PasteDock.exe'); archive = $archive; reports = $reports }
    $latest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts\last-successful-build.json') -Encoding UTF8
    Write-Host "Executable: $(Join-Path $publish 'PasteDock.exe')"
    Write-Host "Created: $archive"
    Write-Host "Reports and real WPF smoke screenshots: $reports"
    Write-Host 'Unsigned developer build. Manual Windows acceptance is still required.'
} finally { Pop-Location }
