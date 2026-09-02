$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'SpotifyTrackHonorific.csproj'
$ReleaseDir = Join-Path $Root 'release'

Write-Host '[release] Building clean release...'
& (Join-Path $Root 'build-dev.ps1')

[xml]$projectXml = Get-Content -LiteralPath $Project
$version = [string]$projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read <Version> from SpotifyTrackHonorific.csproj.'
}
$version = $version -replace '\.0$', ''

if (Test-Path $ReleaseDir) { Remove-Item $ReleaseDir -Recurse -Force }
New-Item -ItemType Directory -Path $ReleaseDir -Force | Out-Null

$package = Get-ChildItem -Path (Join-Path $Root 'bin') -Filter 'latest.zip' -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $package) {
    $package = Get-ChildItem -Path (Join-Path $Root 'bin') -Filter '*.zip' -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if (-not $package) {
    throw 'Build succeeded but DalamudPackager output ZIP could not be found.'
}

$destination = Join-Path $ReleaseDir "SpotifyTrackHonorific-v$version.zip"
Copy-Item -LiteralPath $package.FullName -Destination $destination -Force

$hash = Get-FileHash -LiteralPath $destination -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($destination))"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText("$destination.sha256.txt", $hashLine + [Environment]::NewLine, $utf8NoBom)

$manifest = Get-ChildItem -Path (Join-Path $Root 'built-plugin') -Filter 'SpotifyTrackHonorific.json' -File -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($manifest) {
    Copy-Item -LiteralPath $manifest.FullName -Destination (Join-Path $ReleaseDir 'SpotifyTrackHonorific.json') -Force
}

Write-Host ''
Write-Host 'RELEASE PACKAGE READY' -ForegroundColor Green
Write-Host "Package: $destination"
Write-Host "SHA256:  $($hash.Hash.ToLowerInvariant())"
if ($manifest) {
    Write-Host "Manifest: $(Join-Path $ReleaseDir 'SpotifyTrackHonorific.json')"
}
