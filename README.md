# SpotifyTrackHonorific

Show your currently playing Spotify track as an Honorific title in Final Fantasy XIV.

## Requirements

- Final Fantasy XIV with Dalamud
- Honorific installed
- A Spotify account
- Your own Spotify Developer app Client ID

## 1. Add the custom plugin repository

In FFXIV, open:

`/xlsettings` → **Experimental** → **Custom Plugin Repositories**

Add this repository URL:

`https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/pluginmaster.json`

Save the settings, then open:

`/xlplugins`

Find **SpotifyTrackHonorific** and install it.

## 2. Create a Spotify Developer app

Open the Spotify Developer Dashboard and create an app.

For the app's Redirect URI, add exactly:

`http://127.0.0.1:5000/callback`

Save the app settings and copy the app's **Client ID**.

SpotifyTrackHonorific does **not** require your Client Secret.

## 3. Connect SpotifyTrackHonorific

In FFXIV, open:

`/sth`

Paste your Spotify **Client ID** into the setup field and press **Connect Spotify**.

Your browser will open Spotify's authorization page. Approve the connection, then return to FFXIV.

Once connected, SpotifyTrackHonorific will begin displaying your current Spotify track through Honorific.

## 4. Customize your title

Open `/sth` again to configure:

- normal Spotify tracks and Spotify local files
- title templates
- rotating `{cycle:...}` formats
- bracket cleanup and smart 32-character fitting
- title color and glow
- optional Honorific supporter gradients and animations
- pause behavior and advanced options

Example format:

`♪ {artist} - {track}`

Example rotating format:

`» {cycle:10|vibing to music|{track}|{artist}} «`

## Useful commands

- `/sth` — open settings
- `/sth status` — show Spotify/plugin status
- `/sth now` — show the current detected track
- `/sth retry` — retry Spotify after a recoverable error
- `/sth clear` — clear the currently applied title
- `/sth help` — show available commands
