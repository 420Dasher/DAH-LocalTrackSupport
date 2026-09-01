# DAH-LocalSpotifySupport

Creator: **Dash + AI**

A private-use / unlisted Dalamud fork of DiscordActivityHonorific that keeps the original Discord activity behavior and adds Spotify Web API fallback for **Spotify local-file playback**.

## What stays the same

- Existing DiscordActivityHonorific behavior remains the primary source.
- The existing `/discordactivityhonorific` command stays unchanged for compatibility.
- Normal Spotify/Discord activity continues to behave as before.

## What this fork adds

- Spotify PKCE authentication (no client secret required).
- Spotify local-file detection through the Spotify Web API.
- Local-track metadata support even when a normal Spotify track ID is unavailable.
- Stable local-track fingerprinting so switching between local files updates correctly.
- Spotify fallback only when the local-file path is needed, avoiding unnecessary interference with normal Discord activity.

## Packaging note

This kit defines the required DalamudPackager metadata (`Name`, `Author`, `Punchline`, `Description`, and `RepoUrl`) directly in the API-15 project file. The release build generates a fresh manifest for the renamed fork instead of reusing upstream's `DiscordActivityHonorific.json`.

## First publish

1. Create a **public** GitHub repository named `DAH-LocalSpotifySupport` (or another name you prefer). It may be unadvertised, but Dalamud must be able to fetch the manifest and ZIP without authentication.
2. Put the contents of this folder at the repository root.
3. Run PowerShell from the repo root:

   `./publish.ps1 -GitHubUser YOUR_GITHUB_NAME -Version 0.1.0.0`

4. The script downloads the current upstream source, applies the known-working Spotify-local overlay, writes a complete corresponding source snapshot to `source/`, builds the plugin, creates `plugins/DAH-LocalSpotifySupport/latest.zip`, and generates `pluginmaster.json`.
5. Commit and push **`source/`, `overlay/`, `plugins/`, `pluginmaster.json`, `publish.ps1`, `README.md`, and `LICENSE_NOTICE.md`**.
6. In FFXIV open `/xlsettings` -> **Experimental** -> **Custom Plugin Repositories**.
7. Add:

   `https://raw.githubusercontent.com/YOUR_GITHUB_NAME/DAH-LocalSpotifySupport/main/pluginmaster.json`

8. Save, open `/xlplugins`, search for **DAH-LocalSpotifySupport**, and install it.

If you use a different GitHub repository name, pass `-RepoName YOUR_REPO_NAME` to `publish.ps1` and use that name in the raw URL.

## Spotify setup

Authenticate with:

`/discordactivityhonorific spotify-auth YOUR_SPOTIFY_CLIENT_ID`

No Spotify client secret is needed. Never commit your Dalamud config, Spotify refresh token, Discord token, or other credentials.

## Updating later

Increase the four-part version and publish again, e.g.:

`./publish.ps1 -GitHubUser YOUR_GITHUB_NAME -Version 0.1.0.1`

Then commit/push the updated `source/`, `pluginmaster.json`, and `latest.zip`. Dalamud can then offer the newer version through `/xlplugins`.

## Credits and license

Fork/modified build by **Dash + AI**.

Based on DiscordActivityHonorific by Anya Hichu:
https://github.com/anya-hichu/DiscordActivityHonorific

The upstream project declares AGPL-3.0-or-later. This kit deliberately snapshots the complete modified source used for each published binary under `source/` so the corresponding source can be distributed with the release.
