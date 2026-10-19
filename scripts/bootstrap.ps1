# Dot-source this file. No global installation, registry changes or remote script execution.
function Resolve-PasteDockDotNet {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$Root, [switch]$AllowDownload)
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $minimum = [version]((Get-Content -LiteralPath (Join-Path $Root 'global.json') -Raw | ConvertFrom-Json).sdk.version)
    $tools = Join-Path $Root '.tools'
    $candidates = @()
    $global = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    if ($global) { $candidates += $global.Source }
    if (Test-Path -LiteralPath $tools) {
        $candidates += @(Get-ChildItem -LiteralPath $tools -Directory -Filter 'dotnet-10.*' |
            Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'dotnet.exe' })
    }
    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate)) { continue }
        try {
            $versionText = ((& $candidate --version 2>$null) | Out-String).Trim()
            if ($LASTEXITCODE -eq 0 -and $versionText -match '^10\.0\.\d+$' -and [version]$versionText -ge $minimum) {
                if ($candidate.StartsWith($tools, [StringComparison]::OrdinalIgnoreCase)) {
                    $env:DOTNET_ROOT = Split-Path -Parent $candidate
                    $env:PATH = "$($env:DOTNET_ROOT);$env:PATH"
                }
                return $candidate
            }
        } catch { }
    }
    if (-not $AllowDownload) { throw "The .NET $minimum SDK or a later stable .NET 10 SDK is required. Run build.cmd, or use -Bootstrap for a verified local SDK download." }
    if ($env:OS -ne 'Windows_NT' -or -not [Environment]::Is64BitOperatingSystem) { throw 'This bootstrap requires 64-bit Windows.' }
    $architecture = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Write-Host 'Reading official Microsoft .NET 10 release metadata. No SDK is installed globally.'
    $metadataUri = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
    $metadata = Invoke-RestMethod -Uri $metadataUri -TimeoutSec 60
    $version = [string]$metadata.'latest-sdk'
    if ($version -notmatch '^10\.0\.\d+$' -or [version]$version -lt $minimum) { throw 'The feed did not provide a supported stable SDK version.' }
    $release = @($metadata.releases | Where-Object { $_.sdk.version -eq $version }) | Select-Object -First 1
    if (-not $release) { throw 'The stable SDK release was not found in the feed.' }
    $file = @($release.sdk.files | Where-Object { $_.rid -eq "win-$architecture" -and $_.name -like '*.zip' }) | Select-Object -First 1
    if (-not $file) { throw 'The feed did not contain the requested SDK archive.' }
    $uri = [uri]$file.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com' -or
        $uri.AbsolutePath -ne "/dotnet/Sdk/$version/dotnet-sdk-$version-win-$architecture.zip" -or $uri.Query) {
        throw 'The SDK archive URL did not match the approved Microsoft origin and path.'
    }
    if ([string]$file.hash -notmatch '^[a-fA-F0-9]{128}$') { throw 'The SDK SHA-512 value is invalid.' }
    New-Item -ItemType Directory -Force -Path $tools | Out-Null
    $staging = Join-Path $tools ('sdk-staging-' + [guid]::NewGuid().ToString('N'))
    $destination = Join-Path $tools "dotnet-$version-$architecture"
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        $archive = Join-Path $staging 'sdk.zip'
        Write-Host "Downloading .NET SDK $version ($architecture) into this project's .tools directory."
        Invoke-WebRequest -Uri $uri.AbsoluteUri -OutFile $archive -UseBasicParsing -TimeoutSec 600
        $actualHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA512).Hash
        if (-not $actualHash.Equals([string]$file.hash, [StringComparison]::OrdinalIgnoreCase)) { throw 'SDK checksum mismatch. The archive will not be extracted or executed.' }
        $expanded = Join-Path $staging 'expanded'
        Expand-Archive -LiteralPath $archive -DestinationPath $expanded
        if (-not (Test-Path -LiteralPath (Join-Path $expanded 'dotnet.exe'))) { throw 'The SDK archive has no dotnet.exe.' }
        if (Test-Path -LiteralPath $destination) { throw "An unusable SDK directory already exists: $destination. Rename it and retry; it has not been overwritten." }
        Move-Item -LiteralPath $expanded -Destination $destination
        $exe = Join-Path $destination 'dotnet.exe'
        $env:DOTNET_ROOT = $destination
        $env:PATH = "$destination;$env:PATH"
        $reported = ((& $exe --version) | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or [version]$reported -lt $minimum) { throw 'The downloaded SDK did not start successfully.' }
        Write-Host 'SDK archive verified with the SHA-512 value from Microsoft release metadata.'
        return $exe
    } finally {
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
