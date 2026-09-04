$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$ExpectedVersion = '1.0.4.0'
$ExpectedDisplayVersion = '1.0.4'
$ExpectedHash = '9d0f74af90ef91bb843a13c41f3ad456193081849f0c649de8a35d58c00a4136'

$Project = Join-Path $Root 'SpotifyTrackHonorific\SpotifyTrackHonorific.csproj'
$PluginCs = Join-Path $Root 'SpotifyTrackHonorific\Plugin.cs'
$Manifest = Join-Path $Root 'pluginmaster.json'
$Zip = Join-Path $Root 'plugins\SpotifyTrackHonorific\latest.zip'
$HashFile = Join-Path $Root 'plugins\SpotifyTrackHonorific\latest.zip.sha256.txt'

foreach ($path in @($Project, $PluginCs, $Manifest, $Zip, $HashFile)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required release file is missing: $path"
    }
}

Write-Host '[1/5] Checking source version...'
[xml]$projectXml = Get-Content -LiteralPath $Project
$propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1
$projectVersion = [string]$propertyGroup.Version
if ($projectVersion -ne $ExpectedVersion) {
    throw "csproj Version is $projectVersion, expected $ExpectedVersion"
}

$pluginText = [System.IO.File]::ReadAllText($PluginCs)
if ($pluginText -notmatch ('DisplayVersion\s*=\s*"' + [regex]::Escape($ExpectedDisplayVersion) + '"')) {
    throw "Plugin.cs does not contain DisplayVersion = `"$ExpectedDisplayVersion`""
}

Write-Host '[2/5] Checking repository manifest...'
$repo = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$active = $repo | Where-Object { $_.InternalName -eq 'SpotifyTrackHonorific' } | Select-Object -First 1
if (-not $active) { throw 'SpotifyTrackHonorific is missing from pluginmaster.json' }
if ([string]$active.AssemblyVersion -ne $ExpectedVersion) {
    throw "pluginmaster AssemblyVersion is $($active.AssemblyVersion), expected $ExpectedVersion"
}
$legacy = $repo | Where-Object { $_.InternalName -eq 'DAH-LocalSpotifySupport' } | Select-Object -First 1
if (-not $legacy) { throw 'Legacy DAH-LocalSpotifySupport entry is missing from pluginmaster.json' }
if (-not ([string]$legacy.Name).StartsWith('[Discontinued]')) {
    throw 'Legacy plugin entry is present but is not marked discontinued'
}

Write-Host '[3/5] Checking compiled package hash...'
$actualHash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $ExpectedHash) {
    throw "latest.zip SHA-256 is $actualHash, expected $ExpectedHash"
}
$hashLine = [System.IO.File]::ReadAllText($HashFile).Trim()
if ($hashLine -ne "$ExpectedHash  latest.zip") {
    throw 'latest.zip.sha256.txt does not match the expected hash line'
}

Write-Host '[4/5] Checking embedded Dalamud manifest...'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('sth-v104-verify-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $Zip -DestinationPath $temp -Force
    $embeddedPath = Join-Path $temp 'SpotifyTrackHonorific.json'
    if (-not (Test-Path -LiteralPath $embeddedPath)) { throw 'Compiled ZIP is missing SpotifyTrackHonorific.json' }
    $embedded = Get-Content -LiteralPath $embeddedPath -Raw | ConvertFrom-Json
    if ([string]$embedded.InternalName -ne 'SpotifyTrackHonorific') {
        throw "Embedded InternalName is $($embedded.InternalName)"
    }
    if ([string]$embedded.AssemblyVersion -ne $ExpectedVersion) {
        throw "Embedded AssemblyVersion is $($embedded.AssemblyVersion), expected $ExpectedVersion"
    }
    if ([int]$embedded.DalamudApiLevel -ne 15) {
        throw "Embedded DalamudApiLevel is $($embedded.DalamudApiLevel), expected 15"
    }
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}

Write-Host '[5/5] Release consistency OK.' -ForegroundColor Green
Write-Host "SpotifyTrackHonorific $ExpectedDisplayVersion is ready to commit/push."
Write-Host "latest.zip SHA-256: $actualHash"
