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

    if (-not (Test-Path -LiteralPath $Path)) { return }

    $raw = [System.IO.File]::ReadAllText($Path)
    if ([string]::IsNullOrWhiteSpace($raw)) { return }

    try { $parsed = $raw | ConvertFrom-Json }
    catch { throw "Existing pluginmaster.json is invalid JSON: $($_.Exception.Message)" }

    if ($parsed -is [System.Array]) {
        foreach ($entry in $parsed) {
            if ($null -ne $entry) { Write-Output $entry }
        }
    }
    elseif ($null -ne $parsed) {
        Write-Output $parsed
    }
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
$entries = @(Read-ManifestEntries -Path $Manifest)

$legacy = $entries | Where-Object {
    $internalName = Get-EntryString $_ 'InternalName'
    $name = Get-EntryString $_ 'Name'
    $internalName -eq 'DAH-LocalSpotifySupport' -or
    $name -eq 'DAH-LocalSpotifySupport' -or
    $name -eq '[Discontinued] DAH-LocalSpotifySupport'
} | Select-Object -First 1

if (-not $legacy) {
    Write-Warning 'Legacy manifest entry was not present locally. Reconstructing the known discontinued DAH entry.'
    $legacy = [pscustomobject][ordered]@{
        Author = 'Dash + AI'
        InternalName = 'DAH-LocalSpotifySupport'
        AssemblyVersion = '0.1.0.0'
        ApplicableVersion = 'any'
        RepoUrl = $repoUrl
        Tags = @('discord','spotify','local-files','activity','honorific')
        DalamudApiLevel = 15
        LoadRequiredState = 0
        LoadSync = $false
        CanUnloadAsync = $false
        LoadPriority = 0
        AcceptsFeedback = $false
        DownloadLinkInstall = "$rawBase/plugins/DAH-LocalSpotifySupport/latest.zip"
        IsTestingExclusive = $false
        DownloadLinkTesting = "$rawBase/plugins/DAH-LocalSpotifySupport/latest.zip"
        DownloadLinkUpdate = "$rawBase/plugins/DAH-LocalSpotifySupport/latest.zip"
        Name = '[Discontinued] DAH-LocalSpotifySupport'
        Description = 'Discontinued legacy DiscordActivityHonorific fork. Replaced by the standalone SpotifyTrackHonorific plugin.'
        Punchline = 'Discontinued - replaced by SpotifyTrackHonorific.'
        Changelog = 'This legacy DAH-based fork is discontinued. Use SpotifyTrackHonorific instead.'
        IsHide = $false
    }
}
else {
    Set-JsonProperty $legacy 'Name' '[Discontinued] DAH-LocalSpotifySupport'
    Set-JsonProperty $legacy 'Description' 'Discontinued legacy DiscordActivityHonorific fork. Replaced by the standalone SpotifyTrackHonorific plugin.'
    Set-JsonProperty $legacy 'Punchline' 'Discontinued - replaced by SpotifyTrackHonorific.'
    Set-JsonProperty $legacy 'Changelog' 'This legacy DAH-based fork is discontinued. Use SpotifyTrackHonorific instead.'
    Set-JsonProperty $legacy 'IsHide' $false
}

$otherEntries = @($entries | Where-Object {
    $internalName = Get-EntryString $_ 'InternalName'
    $name = Get-EntryString $_ 'Name'
    $isActive = $internalName -eq 'SpotifyTrackHonorific' -or $name -eq 'SpotifyTrackHonorific'
    $isLegacy = $internalName -eq 'DAH-LocalSpotifySupport' -or
                $name -eq 'DAH-LocalSpotifySupport' -or
                $name -eq '[Discontinued] DAH-LocalSpotifySupport'
    -not $isActive -and -not $isLegacy
})

$newEntry = [pscustomobject][ordered]@{
    Author = 'Dash + AI'
    Name = 'SpotifyTrackHonorific'
    InternalName = 'SpotifyTrackHonorific'
    AssemblyVersion = $fullVersion
    Description = 'Shows your current Spotify track as an Honorific title, including local files, formatting, cleanup, styling, supporter effects, reliability handling, content filtering, saved profiles, and portable settings.'
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
    Changelog = 'SpotifyTrackHonorific 1.0.5 adds saved profiles, enhanced live preview, portable settings export/import, and faster pause/resume detection.'
    AcceptsFeedback = $false
    DownloadLinkInstall = $download
    IsHide = $false
    IsTestingExclusive = $false
    DownloadLinkTesting = $download
    DownloadLinkUpdate = $download
}

$finalEntries = @($newEntry) + @($legacy) + @($otherEntries)
$manifestJson = ConvertTo-Json -InputObject $finalEntries -Depth 15
if (-not $manifestJson.TrimStart().StartsWith('[')) { $manifestJson = "[`r`n$manifestJson`r`n]" }
[System.IO.File]::WriteAllText($Manifest, $manifestJson + [Environment]::NewLine, $utf8NoBom)

$validatedEntries = @(Read-ManifestEntries -Path $Manifest)
$activeCheck = $validatedEntries | Where-Object { (Get-EntryString $_ 'InternalName') -eq 'SpotifyTrackHonorific' } | Select-Object -First 1
$legacyCheck = $validatedEntries | Where-Object { (Get-EntryString $_ 'InternalName') -eq 'DAH-LocalSpotifySupport' } | Select-Object -First 1
if (-not $activeCheck) { throw 'Generated manifest does not contain SpotifyTrackHonorific.' }
if (-not $legacyCheck) { throw 'Generated manifest does not contain the discontinued DAH-LocalSpotifySupport entry.' }

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
Write-Host '  git add -A'
Write-Host ('  git commit -m "Release SpotifyTrackHonorific v' + $displayVersion + '"')
Write-Host '  git push origin main'
