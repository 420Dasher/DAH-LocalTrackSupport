param(
    [Parameter(Mandatory=$true)][string]$GitHubUser,
    [string]$RepoName = 'DAH-LocalTrackSupport',
    [string]$Version = '0.1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ToolsDir '..\..')).Path
$Work = Join-Path $Root 'dev\.work\legacy-dah'
$RepoZip = Join-Path $Work 'upstream.zip'
$Extract = Join-Path $Work 'extract'
$Repo = Join-Path $Extract 'DiscordActivityHonorific-master'
$LegacyRoot = Join-Path $Root 'legacy\DAH-LocalSpotifySupport'
$Overlay = Join-Path $LegacyRoot 'overlay\DiscordActivityHonorific'
$SourceSnapshot = Join-Path $LegacyRoot 'source'
$PluginDir = Join-Path $Root 'plugins\DAH-LocalSpotifySupport'
$LatestZip = Join-Path $PluginDir 'latest.zip'
$Manifest = Join-Path $Root 'pluginmaster.json'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK was not found.' }
$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') { throw '.NET 10 SDK was not found. Dalamud API 15 builds require .NET 10.' }
if (-not (Test-Path $Overlay)) { throw "Spotify-local overlay was not found at: $Overlay" }

if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
New-Item -ItemType Directory -Path $Work, $Extract, $PluginDir -Force | Out-Null

Write-Host '[1/6] Downloading current upstream source...'
Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/anya-hichu/DiscordActivityHonorific/archive/refs/heads/master.zip' -OutFile $RepoZip
Expand-Archive -Path $RepoZip -DestinationPath $Extract -Force
if (-not (Test-Path $Repo)) { throw 'Unexpected upstream archive structure.' }

Write-Host '[2/6] Applying DAH-LocalSpotifySupport overlay...'
Copy-Item -Path (Join-Path $Overlay '*') -Destination (Join-Path $Repo 'DiscordActivityHonorific') -Recurse -Force

$Project = Join-Path $Repo 'DiscordActivityHonorific\DiscordActivityHonorific.csproj'
[xml]$ProjectXml = Get-Content $Project
$propertyGroup = $ProjectXml.Project.PropertyGroup | Select-Object -First 1
$repoPage = "https://github.com/$GitHubUser/$RepoName"
if ($null -ne $propertyGroup.RepoUrl) { $propertyGroup.RepoUrl = $repoPage }
if ($null -ne $propertyGroup.PackageProjectUrl) { $propertyGroup.PackageProjectUrl = $repoPage }
$ProjectXml.Save($Project)

$staleManifest = Join-Path $Repo 'DiscordActivityHonorific\DiscordActivityHonorific.json'
if (Test-Path $staleManifest) { Remove-Item $staleManifest -Force }

Write-Host '[3/6] Saving complete corresponding source snapshot...'
if (Test-Path $SourceSnapshot) { Remove-Item $SourceSnapshot -Recurse -Force }
Copy-Item -Path $Repo -Destination $SourceSnapshot -Recurse -Force

Write-Host "[4/6] Building DAH-LocalSpotifySupport $Version..."
Push-Location $Repo
try {
    & dotnet restore $Project --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw 'dotnet restore failed.' }
    & dotnet build $Project -c Release --no-restore -p:Version=$Version -p:AssemblyVersion=$Version -p:FileVersion=$Version
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
}
finally { Pop-Location }

Write-Host '[5/6] Collecting Dalamud plugin package...'
$releaseZip = Get-ChildItem -Path (Join-Path $Repo 'DiscordActivityHonorific\bin') -Filter 'latest.zip' -Recurse -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $releaseZip) {
    $releaseZip = Get-ChildItem -Path (Join-Path $Repo 'DiscordActivityHonorific\bin') -Filter '*.zip' -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
}
if (-not $releaseZip) { throw 'Build succeeded but no plugin ZIP was found under bin/.' }
Copy-Item $releaseZip.FullName $LatestZip -Force

Write-Host '[6/6] Updating legacy entry in multi-plugin manifest...'
$rawBase = "https://raw.githubusercontent.com/$GitHubUser/$RepoName/main"
$repoUrl = "https://github.com/$GitHubUser/$RepoName"
$download = "$rawBase/plugins/DAH-LocalSpotifySupport/latest.zip"

$entry = [pscustomobject][ordered]@{
    Author = 'Dash + AI'
    Name = '[Discontinued] DAH-LocalSpotifySupport'
    InternalName = 'DAH-LocalSpotifySupport'
    AssemblyVersion = $Version
    Description = 'Discontinued legacy DiscordActivityHonorific fork with Spotify local-file fallback. Replaced by SpotifyTrackHonorific.'
    ApplicableVersion = 'any'
    RepoUrl = $repoUrl
    Tags = @('discord','spotify','local-files','activity','honorific')
    DalamudApiLevel = 15
    LoadRequiredState = 0
    LoadSync = $false
    CanUnloadAsync = $false
    LoadPriority = 0
    Punchline = 'Discontinued - replaced by SpotifyTrackHonorific.'
    Changelog = 'Legacy DAH-based fork is discontinued. Use SpotifyTrackHonorific instead.'
    AcceptsFeedback = $false
    DownloadLinkInstall = $download
    IsHide = $false
    IsTestingExclusive = $false
    DownloadLinkTesting = $download
    DownloadLinkUpdate = $download
}

$entries = @()
if (Test-Path $Manifest) {
    $manifestRaw = [System.IO.File]::ReadAllText($Manifest)
    if (-not [string]::IsNullOrWhiteSpace($manifestRaw)) {
        try { $entries = @($manifestRaw | ConvertFrom-Json) }
        catch { throw "Existing pluginmaster.json is invalid JSON: $($_.Exception.Message)" }
    }
}

$preserved = @($entries | Where-Object {
    $_.InternalName -ne 'DAH-LocalSpotifySupport' -and
    $_.Name -ne 'DAH-LocalSpotifySupport' -and
    $_.Name -ne '[Discontinued] DAH-LocalSpotifySupport'
})
$finalEntries = @($preserved) + @($entry)
$manifestJson = ConvertTo-Json -InputObject $finalEntries -Depth 15
if (-not $manifestJson.TrimStart().StartsWith('[')) { $manifestJson = "[`r`n$manifestJson`r`n]" }

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($Manifest, $manifestJson + [Environment]::NewLine, $utf8NoBom)
$manifestCheck = [System.IO.File]::ReadAllText($Manifest)
if (-not $manifestCheck.TrimStart().StartsWith('[')) { throw 'Generated pluginmaster.json is not a JSON array.' }
try { $null = $manifestCheck | ConvertFrom-Json }
catch { throw "Generated pluginmaster.json is invalid JSON: $($_.Exception.Message)" }

Write-Host ''
Write-Host 'LEGACY PUBLISH PACKAGE READY' -ForegroundColor Yellow
Write-Host 'DAH-LocalSpotifySupport remains DISCONTINUED.'
Write-Host "Plugin ZIP: $LatestZip"
Write-Host "Manifest:   $Manifest"
Write-Host "Source:     $SourceSnapshot"
Write-Host "Repo URL:   $rawBase/pluginmaster.json"
Write-Host ''
Write-Host 'Legacy working files were kept under dev/.work and are ignored by Git.'
