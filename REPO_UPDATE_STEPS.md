# v1.0.0 repository update steps

Target repository:

`https://github.com/420Dasher/DAH-LocalTrackSupport`

## 1. Copy this kit into the existing repo root

After extraction, the repository root should contain at least:

```text
SpotifyTrackHonorific/
publish.ps1
publish-legacy-dah.ps1
README.md
pluginmaster.json                 # existing file until publish.ps1 updates it
plugins/DAH-LocalSpotifySupport/  # existing legacy binary
source/                           # existing legacy corresponding source
overlay/                          # existing legacy overlay
LICENSE_NOTICE.md                 # existing legacy notice
```

Do **not** delete the legacy `source/`, `overlay/`, plugin ZIP, or license notice.

## 2. Build and update the multi-plugin manifest

Open PowerShell in the repo root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\publish.ps1
```

Expected results:

- `plugins/SpotifyTrackHonorific/latest.zip`
- `plugins/SpotifyTrackHonorific/latest.zip.sha256.txt`
- `pluginmaster.json` containing SpotifyTrackHonorific plus the legacy entry
- legacy entry displayed as `[Discontinued] DAH-LocalSpotifySupport`

## 3. Quick manifest check

```powershell
$repo = Get-Content .\pluginmaster.json -Raw | ConvertFrom-Json
$repo | Format-Table Name, InternalName, AssemblyVersion
```

You should see both SpotifyTrackHonorific and the discontinued DAH entry.

## 4. Test the raw repo locally before pushing if desired

After pushing, the custom-repository URL remains:

`https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/pluginmaster.json`

No Dalamud repository URL change is required for users already using that URL.

## 5. Commit and push

```powershell
git status
git diff
git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json README.md publish.ps1 publish-legacy-dah.ps1
git commit -m "Release SpotifyTrackHonorific v1.0.0"
git push origin main
```

## 6. Final install/update test

- Refresh `/xlplugins`.
- Confirm SpotifyTrackHonorific appears as v1.0.0.
- Confirm the old DAH fork is visibly marked discontinued.
- Install/update SpotifyTrackHonorific through the custom repo.
- Confirm `/sth status` reports v1.0.0 and existing v0.0.14 settings migrate unchanged.
