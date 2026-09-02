$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Project = Join-Path $Root 'SpotifyTrackHonorific.csproj'
$BuiltPlugin = Join-Path $Root 'built-plugin'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK was not found.'
}

$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') {
    throw '.NET 10 SDK was not found. Dalamud API 15 requires .NET 10.'
}

Write-Host '[0/3] Cleaning previous build artifacts...'
foreach ($dirName in @('bin', 'obj', 'built-plugin')) {
    $dir = Join-Path $Root $dirName
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}

Write-Host '[1/3] Restoring...'
& dotnet restore $Project --force-evaluate
if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }

Write-Host '[2/3] Building Release...'
& dotnet build $Project -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }

if (Test-Path $BuiltPlugin) { Remove-Item $BuiltPlugin -Recurse -Force }
New-Item -ItemType Directory -Path $BuiltPlugin -Force | Out-Null

Write-Host '[3/3] Preparing dev-plugin folder...'
$releaseZip = Get-ChildItem -Path (Join-Path $Root 'bin') -Filter 'latest.zip' -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1

if (-not $releaseZip) {
    $releaseZip = Get-ChildItem -Path (Join-Path $Root 'bin') -Filter '*.zip' -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}

if ($releaseZip) {
    Expand-Archive -Path $releaseZip.FullName -DestinationPath $BuiltPlugin -Force
}
else {
    $dll = Get-ChildItem -Path (Join-Path $Root 'bin') -Filter 'SpotifyTrackHonorific.dll' -Recurse -File |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $dll) { throw 'Build succeeded but SpotifyTrackHonorific.dll could not be found.' }

    Copy-Item $dll.FullName (Join-Path $BuiltPlugin $dll.Name) -Force
    $manifest = Get-ChildItem -Path $dll.Directory.FullName -Filter 'SpotifyTrackHonorific.json' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($manifest) { Copy-Item $manifest.FullName (Join-Path $BuiltPlugin $manifest.Name) -Force }
}

Write-Host ''
Write-Host 'DEV BUILD READY' -ForegroundColor Green
Write-Host "Dev plugin folder: $BuiltPlugin"
Write-Host 'Point Dalamud Dev Plugin Locations at that folder and reload the plugin.'
