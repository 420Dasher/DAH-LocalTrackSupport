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
- A release-oriented settings UI split into Home / Title / Appearance / Filter / Advanced.
- Optional triggerword filtering with custom rules, smart variation matching, field-level censoring, and a built-in starter list.

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

Each entry lasts roughly ten seconds. Spotify is normally polled about every 15 seconds while playing (and about every 60 seconds while idle) to reduce Development Mode quota usage. Between API polls, elapsed/remaining time and cycle templates advance locally about once per second; actual track changes may take up to roughly one Spotify polling interval to appear.

### Appearance

Normal Honorific color/glow controls plus optional Honorific supporter gradients and animation styles.

Supporter controls are intentionally trust-based. The user must explicitly confirm that they are entitled to use Honorific supporter features before those controls unlock. SpotifyTrackHonorific does not independently verify supporter status.

Honorific's own colored-title and animation settings must also permit the effects.

### Filter

Optional triggerword filtering for artist, track, and album metadata. Custom rules can be unscoped or prefixed with `artist:`, `track:`, or `album:`. Smart matching handles case, spacing, punctuation and common leetspeak forms.

The default action censors only the matching metadata field with `Triggerword censored`, so rotating `{cycle:...}` titles continue normally. A separate optional built-in list provides conservative common trigger terms and can be customized term-by-term without overwriting the user's custom rules.

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

Temporary network errors, Spotify server errors, rate limits, and Development Mode quota exhaustion do not immediately erase a valid title. The plugin keeps the last good title, distinguishes Spotify `QUOTA_EXCEEDED` responses from ordinary rate limits, honors the full server `Retry-After` interval, prevents manual retry from bypassing an active Spotify cooldown, and returns to normal polling after recovery.

If Spotify authorization becomes invalid, the UI asks the user to reconnect instead of retrying forever.

## Version 1.0.5

v1.0.5 adds up to five saved title profiles, enhanced live preview details, portable settings export/import, and faster playback-resume detection while keeping the validated Spotify quota-friendly polling behavior.
## Version 1.0.4

v1.0.4 adds the optional content filter, smart variation matching, field-level censoring that keeps title cycles running, and a separate built-in triggerword starter list with per-term controls.

## Version 1.0.1

v1.0.1 is the first post-release reliability update. It fixes a one-hour `Retry-After` clamp, recognizes Spotify Development Mode `QUOTA_EXCEEDED` responses, backs off conservatively when Spotify supplies no quota retry time, and reduces normal API polling from 3s/8s to 15s/60s.

## Version 1.0.0

v1.0.0 is the promoted release of the fully regression-tested v0.0.14 release candidate. No runtime feature changes were introduced during promotion.
