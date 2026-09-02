# SpotifyTrackHonorific

SpotifyTrackHonorific is a standalone Dalamud plugin that shows the track you are listening to on Spotify as an Honorific title in Final Fantasy XIV.

It talks directly to Spotify's Web API and Honorific. It does **not** require Discord or DiscordActivityHonorific.

## Features

- Regular Spotify tracks and Spotify local files.
- Prefix or suffix Honorific titles.
- Templates using track, artist, album, duration, playback progress, local-file state, and pause state.
- Rotating titles with `{cycle:SECONDS|...}`.
- Optional removal of bracketed additions such as `(Remastered 2026)`.
- Smart fitting to Honorific's 32-character title limit with separator cleanup.
- Honorific title color and glow.
- Trust-gated Honorific supporter gradients and animation styles.
- Spotify/network retry handling with rate-limit-aware backoff.
- A release-oriented settings UI split into Home / Title / Appearance / Advanced.

## Requirements

- Final Fantasy XIV with Dalamud.
- Dalamud API 15 / .NET 10 compatible environment.
- Honorific installed and enabled.
- A Spotify account and your own Spotify Developer app Client ID.

SpotifyTrackHonorific never asks for or stores your Spotify password. Authorization uses Spotify PKCE and stores the refresh token in the normal Dalamud plugin configuration.

## First-time setup

1. Create or open an app in the Spotify Developer Dashboard.
2. Add this exact Redirect URI:

   `http://127.0.0.1:5000/callback`

3. Copy the app's **Client ID**. A client secret is not required.
4. In FFXIV, open SpotifyTrackHonorific with `/sth`.
5. Paste the Client ID and press **Connect Spotify**.
6. Approve access in the browser window that opens.
7. Make sure Honorific is installed/enabled and start playing music in Spotify.

The **Home** tab should show Spotify as connected and display the detected track.

## Settings

### Home

Connection status, Spotify setup/reconnect controls, current track, retry controls, and an Honorific test button.

### Title

Choose regular/local tracks, pause behavior, prefix/suffix placement, quick presets, custom templates, bracket cleanup, and smart fitting.

Useful variables:

- `{artist}` - primary artist
- `{artists}` - all artists
- `{track}` - track name
- `{album}` - album name
- `{duration}` - track duration
- `{elapsed}` - playback position
- `{remaining}` - remaining time
- `{is_local}` - `true` or `false`
- `{paused}` - `true` or `false`

Example rotating title:

`» {cycle:10|vibing to music|{track}|{artist}} «`

Each entry lasts roughly ten seconds. Spotify is normally polled about every three seconds while playing, so cycle/progress changes may appear up to roughly one polling interval late.

### Appearance

Normal Honorific color/glow controls plus optional Honorific supporter gradients and animation styles.

Supporter controls are intentionally trust-based. The user must explicitly confirm that they are entitled to use Honorific supporter features before those controls unlock. SpotifyTrackHonorific does not independently verify supporter status.

Honorific's own colored-title and animation settings must also permit the effects.

### Advanced

Reliability/debug information, manual retry/test/clear controls, command help, display reset, and Spotify authorization removal.

## Commands

- `/sth` - open settings
- `/sth status` - show connection/poll state
- `/sth now` - show the cached Spotify track and generated Honorific title
- `/sth retry` - reset backoff and retry Spotify immediately
- `/sth ipc-test` - send a test Honorific title
- `/sth clear` - clear this plugin's Honorific title
- `/sth enable` / `/sth disable` - toggle Spotify polling
- `/sth auth <client-id>` - authenticate from chat instead of the UI

## Reliability

Temporary network errors, Spotify server errors, and rate limits do not immediately erase a valid title. The plugin keeps the last good title while retrying with increasing delays, respects Spotify `Retry-After` responses, and returns to normal polling after recovery.

If Spotify authorization becomes invalid, the UI asks the user to reconnect instead of retrying forever.

## Version 1.0.0

v1.0.0 is the promoted release of the fully regression-tested v0.0.14 release candidate. No runtime feature changes were introduced during promotion.
