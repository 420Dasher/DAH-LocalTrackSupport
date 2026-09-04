param(
    [string]$GitHubUser = '420Dasher',
    [string]$RepoName = 'DAH-LocalTrackSupport'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ToolsDir '..\..')).Path
$SourceDir = Join-Path $Root 'SpotifyTrackHonorific'
$Project = Join-Path $SourceDir 'SpotifyTrackHonorific.csproj'
$BuildRelease = Join-Path $SourceDir 'build-release.ps1'
$PluginDir = Join-Path $Root 'plugins\SpotifyTrackHonorific'
$LatestZip = Join-Path $PluginDir 'latest.zip'
$LatestHash = Join-Path $PluginDir 'latest.zip.sha256.txt'
$Manifest = Join-Path $Root 'pluginmaster.json'
$Verifier = Join-Path $ToolsDir 'verify-release.ps1'

function Set-JsonProperty {
    param(
        [Parameter(Mandatory=$true)]$Object,
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter(Mandatory=$true)]$Value
    )
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

if (-not (Test-Path $Project)) { throw "SpotifyTrackHonorific source was not found at: $SourceDir" }
if (-not (Test-Path $BuildRelease)) { throw "build-release.ps1 was not found at: $BuildRelease" }
if (-not (Test-Path $Verifier)) { throw "verify-release.ps1 was not found at: $Verifier" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET SDK was not found.' }
$sdks = (& dotnet --list-sdks) -join "`n"
if ($sdks -notmatch '(?m)^10\.') { throw '.NET 10 SDK was not found. Dalamud API 15 builds require .NET 10.' }

[xml]$projectXml = Get-Content -LiteralPath $Project
$propertyGroup = $projectXml.Project.PropertyGroup | Select-Object -First 1
$fullVersion = [string]$propertyGroup.Version
if ([string]::IsNullOrWhiteSpace($fullVersion)) { throw 'Could not read <Version> from SpotifyTrackHonorific.csproj.' }
$displayVersion = $fullVersion -replace '\.0$', ''
$repoUrl = "https://github.com/$GitHubUser/$RepoName"
$rawBase = "https://raw.githubusercontent.com/$GitHubUser/$RepoName/main"
$download = "$rawBase/plugins/SpotifyTrackHonorific/latest.zip"

Write-Host "[1/5] Building SpotifyTrackHonorific $displayVersion..."
& $BuildRelease
if ($LASTEXITCODE -ne 0) { throw 'build-release.ps1 failed.' }

$releaseZip = Join-Path $SourceDir "release\SpotifyTrackHonorific-v$displayVersion.zip"
if (-not (Test-Path $releaseZip)) { throw "Expected release ZIP was not found: $releaseZip" }

Write-Host '[2/5] Copying repository package...'
New-Item -ItemType Directory -Path $PluginDir -Force | Out-Null
Copy-Item -LiteralPath $releaseZip -Destination $LatestZip -Force
$hash = Get-FileHash -LiteralPath $LatestZip -Algorithm SHA256
$hashLine = "$($hash.Hash.ToLowerInvariant())  latest.zip"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($LatestHash, $hashLine + [Environment]::NewLine, $utf8NoBom)

Write-Host '[3/5] Updating multi-plugin repository manifest...'
$entries = @()
if (Test-Path $Manifest) {
    $rawManifest = [System.IO.File]::ReadAllText($Manifest)
    if (-not [string]::IsNullOrWhiteSpace($rawManifest)) {
        try { $entries = @($rawManifest | ConvertFrom-Json) }
        catch { throw "Existing pluginmaster.json is invalid JSON: $($_.Exception.Message)" }
    }
}

$legacy = $entries | Where-Object {
    $_.InternalName -eq 'DAH-LocalSpotifySupport' -or
    $_.Name -eq 'DAH-LocalSpotifySupport' -or
    $_.Name -eq '[Discontinued] DAH-LocalSpotifySupport'
} | Select-Object -First 1

if ($legacy) {
    Set-JsonProperty $legacy 'Name' '[Discontinued] DAH-LocalSpotifySupport'
    Set-JsonProperty $legacy 'Description' 'Discontinued legacy DiscordActivityHonorific fork. Replaced by the standalone SpotifyTrackHonorific plugin.'
    Set-JsonProperty $legacy 'Punchline' 'Discontinued - replaced by SpotifyTrackHonorific.'
    Set-JsonProperty $legacy 'Changelog' 'This legacy DAH-based fork is discontinued. Use SpotifyTrackHonorific instead.'
    Set-JsonProperty $legacy 'IsHide' $false
}
else {
    Write-Warning 'DAH-LocalSpotifySupport was not found in the existing manifest, so no legacy entry was modified.'
}

$preserved = @($entries | Where-Object {
    $_.InternalName -ne 'SpotifyTrackHonorific' -and $_.Name -ne 'SpotifyTrackHonorific'
})

$newEntry = [pscustomobject][ordered]@{
    Author = 'Dash + AI'
    Name = 'SpotifyTrackHonorific'
    InternalName = 'SpotifyTrackHonorific'
    AssemblyVersion = $fullVersion
    Description = 'Shows your current Spotify track as an Honorific title, including local files, formatting, cleanup, styling, supporter effects, reliability handling, and content filtering.'
    ApplicableVersion = 'any'
    RepoUrl = $repoUrl
    Tags = @('spotify','local-files','honorific','music')
    IconUrl = "$rawBase/SpotifyTrackHonorific/images/icon.png"
    DalamudApiLevel = 15
    LoadRequiredState = 0
    LoadSync = $false
    CanUnloadAsync = $false
    LoadPriority = 0
    Punchline = 'Show your Spotify track as an Honorific title.'
    Changelog = "SpotifyTrackHonorific $displayVersion stable release by Dash + AI."
    AcceptsFeedback = $false
    DownloadLinkInstall = $download
    IsHide = $false
    IsTestingExclusive = $false
    DownloadLinkTesting = $download
    DownloadLinkUpdate = $download
}

$finalEntries = @($newEntry) + @($preserved)
$manifestJson = ConvertTo-Json -InputObject $finalEntries -Depth 15
if (-not $manifestJson.TrimStart().StartsWith('[')) { $manifestJson = "[`r`n$manifestJson`r`n]" }
[System.IO.File]::WriteAllText($Manifest, $manifestJson + [Environment]::NewLine, $utf8NoBom)

$manifestCheck = [System.IO.File]::ReadAllText($Manifest)
if (-not $manifestCheck.TrimStart().StartsWith('[')) { throw 'Generated pluginmaster.json is not a JSON array.' }
try { $validated = $manifestCheck | ConvertFrom-Json }
catch { throw "Generated pluginmaster.json is invalid JSON: $($_.Exception.Message)" }
if (-not ($validated | Where-Object { $_.InternalName -eq 'SpotifyTrackHonorific' })) {
    throw 'Generated manifest does not contain SpotifyTrackHonorific.'
}

Write-Host '[4/5] Verifying release package...'
& $Verifier

Write-Host '[5/5] Publish package prepared.'
Write-Host ''
Write-Host 'PUBLISH PACKAGE READY' -ForegroundColor Green
Write-Host "Plugin:      SpotifyTrackHonorific $displayVersion"
Write-Host "Plugin ZIP:  $LatestZip"
Write-Host "SHA256:      $($hash.Hash.ToLowerInvariant())"
Write-Host "Manifest:    $Manifest"
Write-Host "Repo URL:    $rawBase/pluginmaster.json"
Write-Host ''
Write-Host 'Review, then commit/push:'
Write-Host '  git status'
Write-Host '  git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json dev'
Write-Host ('  git commit -m "Release SpotifyTrackHonorific v' + $displayVersion + '"')
Write-Host '  git push origin main'
