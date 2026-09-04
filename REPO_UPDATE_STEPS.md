# SpotifyTrackHonorific v1.0.4 repo update

This kit is meant to be copied **over your existing local clone** of:

`https://github.com/420Dasher/DAH-LocalTrackSupport`

It deliberately does not delete or replace the discontinued legacy plugin files already in your clone.

## 1. Start from a clean/up-to-date clone

From your existing repository root:

```powershell
git status
git pull --rebase origin main
```

If `git status` shows unrelated local changes, deal with those first instead of overwriting them.

## 2. Copy this kit over the repository root

Copy the **contents** of this RepoKit folder into the root of your local `DAH-LocalTrackSupport` clone and allow matching files to be replaced.

Important: do not delete the existing repo first. The legacy `source/`, `overlay/`, legacy plugin package and license/notice material should remain where they already are.

## 3. Verify v1.0.4 before committing

Run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\VERIFY_V1.0.4.ps1
```

Expected package SHA-256:

`9d0f74af90ef91bb843a13c41f3ad456193081849f0c649de8a35d58c00a4136`

Then review:

```powershell
git status
git diff -- SpotifyTrackHonorific pluginmaster.json README.md publish.ps1 REPO_UPDATE_STEPS.md
```

`latest.zip` is binary, so Git cannot show a useful textual diff for it; the verification script checks its SHA-256 and embedded manifest instead.

## 4. Commit only the intended release files

```powershell
git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json README.md REPO_UPDATE_STEPS.md NO_LICENSE_CHANGE.txt publish.ps1 publish-legacy-dah.ps1 V1.0.4_RELEASE_NOTES.txt VERIFY_V1.0.4.ps1
git status
git commit -m "Release SpotifyTrackHonorific v1.0.4"
git push origin main
```

Do **not** use a force push.

## 5. Final repo-install test

After GitHub shows the new commit:

1. Disable/remove the v1.0.4 Dev Plugin Location so the dev copy cannot take precedence.
2. Restart FFXIV/Dalamud or refresh/reopen the plugin installer.
3. Open `/xlplugins` and confirm SpotifyTrackHonorific shows v1.0.4.
4. Update/install it from the custom repository.
5. Test a normal track, a filtered artist/track, smart variation matching, and a `{cycle:...}` format.
6. Run `/sth status` and confirm Spotify polling remains healthy.

The custom repository URL remains:

`https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/pluginmaster.json`
