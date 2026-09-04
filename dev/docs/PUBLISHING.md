# Publishing SpotifyTrackHonorific

## Normal active-plugin release

1. Update the source version in `SpotifyTrackHonorific/SpotifyTrackHonorific.csproj` and the matching display version in `Plugin.cs`.
2. Update `SpotifyTrackHonorific/CHANGELOG.md`.
3. Build/test the dev version locally in Dalamud.
4. From the repository root, run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\dev\tools\publish.ps1
```

The publisher:

- requires .NET 10;
- builds the active plugin using `SpotifyTrackHonorific/build-release.ps1`;
- copies the release ZIP to `plugins/SpotifyTrackHonorific/latest.zip`;
- writes `latest.zip.sha256.txt`;
- updates only the active SpotifyTrackHonorific entry in `pluginmaster.json`;
- preserves the discontinued legacy entry and all other manifest entries;
- runs the generic release verifier.

Review before committing:

```powershell
git status
git diff --stat
git diff -- SpotifyTrackHonorific pluginmaster.json dev
.\dev\tools\verify-release.ps1
```

Then:

```powershell
git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json dev

git status
git diff --cached --stat
git commit -m "Release SpotifyTrackHonorific vX.Y.Z"
git push origin main
```

Do not force-push.

## Final repo-install test

After GitHub shows the commit:

1. Disable/remove the Dev Plugin Location.
2. Refresh/reopen `/xlplugins`.
3. Confirm the new version is offered from the custom repository.
4. Install/update it.
5. Confirm settings migrate, icon loads, Spotify connects, title formatting works, and `/sth status` is healthy.

## Legacy publisher

Only use this if the discontinued DAH fork ever genuinely needs rebuilding:

```powershell
.\dev\tools\publish-legacy-dah.ps1 -GitHubUser 420Dasher
```

Its source/overlay material lives under `legacy/DAH-LocalSpotifySupport/`.
