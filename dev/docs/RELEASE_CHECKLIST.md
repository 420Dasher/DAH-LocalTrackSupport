# SpotifyTrackHonorific v1.0.1 Release Smoke Test

This build specifically targets long-running Spotify 429 handling.

## Build / migration

- Build successfully with .NET 10 / Dalamud API 15.
- Upgrade directly over v1.0.0.
- Confirm Spotify auth, template, colors, gradients, and other settings survive unchanged.
- Confirm `/sth status` and the settings header report v1.0.1.

## Normal playback regression

- Normal Spotify track appears.
- Spotify local file appears.
- Normal <-> local switching still works.
- Pause/resume behavior still follows settings.
- Cycle templates, progress variables, smart-fit, colors/glow, and supporter gradients still work.
- Confirm progress variables and `{cycle:...}` continue advancing about once per second locally even though Spotify itself is polled about every 15 seconds.

## 429 / quota behavior

If the account is still currently limited, this is the most valuable test:

- Load v1.0.1 and let it make one Spotify request.
- If Spotify returns `reason: QUOTA_EXCEEDED`, Home/Advanced status should say Development Mode quota exceeded rather than generic rate limited.
- If Spotify sends a Retry-After longer than one hour, the displayed retry time must remain longer than one hour. It must NOT collapse to 3600 seconds.
- Press Retry while the cooldown is active. The plugin should refuse to bypass Spotify's wait period and should not make an immediate API request.
- Changing title/appearance settings during the cooldown should update the cached title when possible without forcing an early Spotify API request.
- After the cooldown expires and Spotify accepts requests again, normal polling should recover automatically.

Do not publish v1.0.1 until normal playback regression passes. The real long-cooldown behavior can be verified against the currently active Spotify 429 if available.
