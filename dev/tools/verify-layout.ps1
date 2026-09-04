$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ToolsDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ToolsDir '..\..')).Path

Write-Host '[1/4] Checking required runtime paths...'
$required = @(
    'README.md',
    'pluginmaster.json',
    'LICENSE_NOTICE.md',
    'SpotifyTrackHonorific',
    'SpotifyTrackHonorific\images\icon.png',
    'plugins\SpotifyTrackHonorific\latest.zip',
    'plugins\DAH-LocalSpotifySupport\latest.zip',
    'legacy\DAH-LocalSpotifySupport\source',
    'legacy\DAH-LocalSpotifySupport\overlay',
    'dev\tools\publish.ps1',
    'dev\tools\publish-legacy-dah.ps1',
    'dev\tools\verify-release.ps1'
)
foreach ($rel in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Root $rel))) {
        throw "Required organized path is missing: $rel"
    }
}

Write-Host '[2/4] Checking old root clutter is gone...'
$forbidden = @(
    'publish.ps1',
    'publish-legacy-dah.ps1',
    'VERIFY_V1.0.4.ps1',
    'REPO_UPDATE_STEPS.md',
    'V1.0.4_RELEASE_NOTES.txt',
    'NO_LICENSE_CHANGE.txt',
    'source',
    'overlay',
    'SpotifyTrackHonorific\RELEASE_CHECKLIST.md',
    'SpotifyTrackHonorific\RELEASE_WORKFLOW.md',
    'SpotifyTrackHonorific\ICON_SETUP.txt',
    'SpotifyTrackHonorific\TEST_FIRST.txt'
)
foreach ($rel in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $Root $rel)) {
        throw "Old developer/legacy path still exists: $rel"
    }
}

Write-Host '[3/4] Checking runtime URLs stayed stable...'
$manifest = Get-Content -LiteralPath (Join-Path $Root 'pluginmaster.json') -Raw | ConvertFrom-Json
$active = $manifest | Where-Object { $_.InternalName -eq 'SpotifyTrackHonorific' } | Select-Object -First 1
if (-not $active) { throw 'SpotifyTrackHonorific missing from pluginmaster.json.' }
$rawBase = 'https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main'
if ([string]$active.DownloadLinkInstall -ne "$rawBase/plugins/SpotifyTrackHonorific/latest.zip") {
    throw 'SpotifyTrackHonorific download URL changed during cleanup.'
}
if ([string]$active.IconUrl -ne "$rawBase/SpotifyTrackHonorific/images/icon.png") {
    throw 'SpotifyTrackHonorific icon URL changed during cleanup.'
}

Write-Host '[4/4] Repository layout OK.' -ForegroundColor Green
Write-Host 'Developer files are grouped under dev/ and legacy material under legacy/.'
Write-Host 'Runtime download and icon paths are unchanged.'
