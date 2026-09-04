$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ToolsDir '..\..')).Path

function Get-EntryString {
    param(
        $Entry,
        [Parameter(Mandatory=$true)][string]$Name
    )
    if ($null -eq $Entry) { return $null }
    $property = $Entry.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return [string]$property.Value
}

function Read-ManifestEntries {
    param([Parameter(Mandatory=$true)][string]$Path)

    $raw = [System.IO.File]::ReadAllText($Path)
    try { $parsed = $raw | ConvertFrom-Json }
    catch { throw "pluginmaster.json is invalid JSON: $($_.Exception.Message)" }

    if ($parsed -is [System.Array]) {
        foreach ($entry in $parsed) {
            if ($null -ne $entry) { Write-Output $entry }
        }
    }
    elseif ($null -ne $parsed) {
        Write-Output $parsed
    }
}

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

Write-Host '[1/6] Reading expected version from source...'
[xml]$projectXml = Get-Content -LiteralPath $Project
$propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1
$expectedVersion = [string]$propertyGroup.Version
if ([string]::IsNullOrWhiteSpace($expectedVersion)) {
    throw 'Could not read <Version> from SpotifyTrackHonorific.csproj.'
}
$expectedDisplayVersion = $expectedVersion -replace '\.0$', ''

$pluginText = [System.IO.File]::ReadAllText($PluginCs)
if ($pluginText -notmatch ('DisplayVersion\s*=\s*"' + [regex]::Escape($expectedDisplayVersion) + '"')) {
    throw "Plugin.cs does not contain DisplayVersion = `"$expectedDisplayVersion`""
}

Write-Host '[2/6] Checking repository manifest...'
$repo = @(Read-ManifestEntries -Path $Manifest)

$active = $repo | Where-Object {
    (Get-EntryString $_ 'InternalName') -eq 'SpotifyTrackHonorific'
} | Select-Object -First 1
if (-not $active) { throw 'SpotifyTrackHonorific is missing from pluginmaster.json' }

if ((Get-EntryString $active 'AssemblyVersion') -ne $expectedVersion) {
    throw "pluginmaster AssemblyVersion is $(Get-EntryString $active 'AssemblyVersion'), expected $expectedVersion"
}

$legacy = $repo | Where-Object {
    (Get-EntryString $_ 'InternalName') -eq 'DAH-LocalSpotifySupport'
} | Select-Object -First 1
if (-not $legacy) { throw 'Legacy DAH-LocalSpotifySupport entry is missing from pluginmaster.json' }

$legacyName = Get-EntryString $legacy 'Name'
if (-not $legacyName.StartsWith('[Discontinued]')) {
    throw 'Legacy plugin entry is present but is not marked discontinued'
}

$expectedRawBase = 'https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main'
$expectedDownload = "$expectedRawBase/plugins/SpotifyTrackHonorific/latest.zip"
$expectedIcon = "$expectedRawBase/SpotifyTrackHonorific/images/icon.png"
if ((Get-EntryString $active 'DownloadLinkInstall') -ne $expectedDownload) {
    throw "Active DownloadLinkInstall changed unexpectedly: $(Get-EntryString $active 'DownloadLinkInstall')"
}
if ((Get-EntryString $active 'IconUrl') -ne $expectedIcon) {
    throw "Active IconUrl changed unexpectedly: $(Get-EntryString $active 'IconUrl')"
}

Write-Host '[3/6] Checking compiled package hash...'
$actualHash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashLine = [System.IO.File]::ReadAllText($HashFile).Trim()
$expectedHashLine = "$actualHash  latest.zip"
if ($hashLine.ToLowerInvariant() -ne $expectedHashLine) {
    throw "latest.zip.sha256.txt does not match latest.zip. Expected: $expectedHashLine"
}

Write-Host '[4/6] Checking embedded Dalamud manifest...'
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('sth-release-verify-' + [guid]::NewGuid().ToString('N'))
try {
    Expand-Archive -LiteralPath $Zip -DestinationPath $temp -Force
    $embeddedPath = Join-Path $temp 'SpotifyTrackHonorific.json'
    if (-not (Test-Path -LiteralPath $embeddedPath)) { throw 'Compiled ZIP is missing SpotifyTrackHonorific.json' }
    $embedded = Get-Content -LiteralPath $embeddedPath -Raw | ConvertFrom-Json
    if ([string]$embedded.InternalName -ne 'SpotifyTrackHonorific') {
        throw "Embedded InternalName is $($embedded.InternalName)"
    }
    if ([string]$embedded.AssemblyVersion -ne $expectedVersion) {
        throw "Embedded AssemblyVersion is $($embedded.AssemblyVersion), expected $expectedVersion"
    }
    if ([int]$embedded.DalamudApiLevel -ne 15) {
        throw "Embedded DalamudApiLevel is $($embedded.DalamudApiLevel), expected 15"
    }
}
finally {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
}

Write-Host '[5/6] Checking source/package identity...'
if ((Get-EntryString $active 'InternalName') -ne 'SpotifyTrackHonorific') { throw 'Active manifest InternalName changed unexpectedly.' }
if ((Get-EntryString $active 'Author') -ne 'Dash + AI') { throw 'Active manifest Author changed unexpectedly.' }

Write-Host '[6/6] Release consistency OK.' -ForegroundColor Green
Write-Host "SpotifyTrackHonorific $expectedDisplayVersion is ready to commit/push."
Write-Host "latest.zip SHA-256: $actualHash"
