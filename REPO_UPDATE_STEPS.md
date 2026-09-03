# SpotifyTrackHonorific v1.0.1 repo update

This kit upgrades the existing `420Dasher/DAH-LocalTrackSupport` repository to SpotifyTrackHonorific v1.0.1 and publishes the plugin icon in the same commit.

## What it changes

- Replaces `SpotifyTrackHonorific/` with the tested v1.0.1 source.
- Adds `SpotifyTrackHonorific/images/icon.png` (512x512).
- Adds the same icon URL to the generated `pluginmaster.json` entry.
- Keeps the legacy DAH plugin entry and every other repository entry.
- Builds and replaces `plugins/SpotifyTrackHonorific/latest.zip`.

## Publish

Copy the **contents** of this kit into the root of your existing Git repository, preserving `.git/`, `source/`, `overlay/`, and `plugins/DAH-LocalSpotifySupport/`.

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\publish.ps1
```

Review:

```powershell
git status
git diff
```

Confirm the manifest contains v1.0.1 and IconUrl:

```powershell
$repo = Get-Content .\pluginmaster.json -Raw | ConvertFrom-Json
$repo | Where-Object InternalName -eq 'SpotifyTrackHonorific' | Format-List Name,AssemblyVersion,IconUrl,DownloadLinkInstall
```

Then commit everything created/changed by this release:

```powershell
git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json README.md publish.ps1 publish-legacy-dah.ps1 REPO_UPDATE_STEPS.md NO_LICENSE_CHANGE.txt
git commit -m "Release SpotifyTrackHonorific v1.0.1"
git push origin main
```

The icon and manifest can be pushed in the same commit. Once that commit is live, the raw IconUrl resolves normally for Dalamud.
