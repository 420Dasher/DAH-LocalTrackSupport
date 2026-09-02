# DAH-LocalTrackSupport custom Dalamud repository

Custom plugin repository maintained by **Dash + AI** / GitHub user **420Dasher**.

## Active plugin: SpotifyTrackHonorific

**SpotifyTrackHonorific v1.0.0** is the active standalone plugin. It talks directly to Spotify's Web API and Honorific and does not depend on DiscordActivityHonorific or Discord.

Highlights:

- regular Spotify tracks and Spotify local files
- configurable title templates and rotating `{cycle:...}` formats
- bracket cleanup and smart fitting to Honorific's 32-character limit
- Honorific color/glow
- trust-gated Honorific supporter gradients and animations
- Spotify retry/backoff and rate-limit handling
- beginner-friendly Home / Title / Appearance / Advanced UI

The source lives in `SpotifyTrackHonorific/`.

## Legacy plugin: DAH-LocalSpotifySupport

`DAH-LocalSpotifySupport` is retained for existing users but is **discontinued** and replaced by SpotifyTrackHonorific.

The legacy fork remains subject to its upstream DiscordActivityHonorific licensing/notice requirements. Keep its existing `source/`, `overlay/`, and `LICENSE_NOTICE.md` material in the repository.

## Add this custom repository to Dalamud

In FFXIV, open `/xlsettings` -> **Experimental** -> **Custom Plugin Repositories** and add:

`https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/pluginmaster.json`

Then open `/xlplugins` and install **SpotifyTrackHonorific**.

## Spotify setup

SpotifyTrackHonorific requires your own Spotify Developer app Client ID.

Use this Redirect URI in the Spotify app:

`http://127.0.0.1:5000/callback`

Then open `/sth`, enter the Client ID, and press **Connect Spotify**. No client secret is required.

## Publishing SpotifyTrackHonorific

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\publish.ps1
```

The publisher:

1. verifies .NET 10;
2. builds the source under `SpotifyTrackHonorific/`;
3. copies the release package to `plugins/SpotifyTrackHonorific/latest.zip`;
4. preserves every existing `pluginmaster.json` entry;
5. marks the old DAH fork discontinued if it is present;
6. adds/updates SpotifyTrackHonorific;
7. validates that `pluginmaster.json` remains a JSON array.

Review the changes before pushing:

```powershell
git status
git diff
git add SpotifyTrackHonorific plugins/SpotifyTrackHonorific pluginmaster.json README.md publish.ps1 publish-legacy-dah.ps1
git commit -m "Release SpotifyTrackHonorific v1.0.0"
git push origin main
```

## Legacy publisher

`publish-legacy-dah.ps1` exists only in case the discontinued DAH fork ever needs to be rebuilt. Unlike the old one-plugin publisher, it preserves SpotifyTrackHonorific and any other manifest entries.
