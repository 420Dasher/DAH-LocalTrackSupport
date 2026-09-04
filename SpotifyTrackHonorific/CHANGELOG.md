# Changelog

## 1.0.5 - Profiles, Portable Settings and Resume Detection

- Added up to five named profiles that capture title, playback, appearance and content-filter settings.
- Saving an existing profile name overwrites it; profiles can be loaded or deleted directly from the Title tab.
- Enhanced the live preview to show current/example source, prefix/suffix position, exact Honorific output, and pre-fit text when smart fitting changes it.
- Added portable JSON settings export/import through the clipboard for backup or transfer between installations.
- Portable exports include current display/filter settings and saved profiles while deliberately excluding Spotify Client ID, refresh token, onboarding/auth state, global enable state, and Honorific supporter-entitlement confirmation.
- Configuration schema advances to v10; existing v1.0.4 settings migrate in place and preserve the existing Spotify connection.
- Paused playback now remains on the normal ~15-second polling cadence so playback resume is detected without a manual retry.
- Truly idle/not-playing playback remains on the ~60-second cadence.
- Existing v1.0.4 content filtering, rate-limit handling and Spotify cooldown behavior are preserved.
## 1.0.4 - Content Filter and Built-In Triggerwords

- Added an optional content filter with custom blacklist entries and smart variation matching.
- Added field-level censoring so matching artist, track, or album metadata is replaced without interrupting `{cycle:...}` title rotation.
- Added optional `artist:`, `track:`, and `album:` scopes for custom blacklist entries.
- Added an optional built-in list of 32 conservative high-sensitivity trigger terms, kept separate from the user's custom blacklist.
- Added a master built-in-list toggle, per-term controls, and Restore built-in defaults.
- Smart matching handles case, punctuation, spacing, common leetspeak forms such as `$uicide`, and conservative typo matching for longer terms.
- Added short-term boundary protection to avoid obvious false positives such as `grape` matching `rape`.
- Default replacement text is `Triggerword censored` and remains user-editable.
- Existing Spotify polling, quota recovery, styling, and title formatting behavior from v1.0.1 is preserved.

## 1.0.3-dev - Field-Level Content Censoring Test Build

- Changed the default blacklist behavior from replacing the entire Honorific title to censoring only the matching Spotify metadata field.
- Artist matches replace only the matching artist name; other credited artists remain visible.
- Track matches replace only `{track}` and album matches replace only `{album}`.
- `{cycle:...}` formatting, elapsed/remaining variables, wrappers, and unaffected metadata continue updating normally while censorship is active.
- The replacement text remains user-editable and defaults to `Triggerword censored`.
- Clear-title and keep-previous-title modes remain available as explicit alternatives.

## 1.0.2-dev - Content Filter Test Build

- Added an optional blacklist/content filter in a dedicated Filter tab.
- Added one-entry-per-line rules with optional `artist:`, `track:`, and `album:` scopes.
- Added Smart Variation Matching for case/spacing/punctuation, common leetspeak substitutions, and conservative typo matching on longer entries.
- Added three match actions: fallback title, clear title, or keep the previous title.
- Default fallback title is `Triggerword censored`.
- Added a built-in matcher test field (pre-filled with `$uicideboy$`).
- Corrected the Advanced tab polling description to the v1.0.1 15s/60s intervals.

## 1.0.1

- Fixed Spotify `Retry-After` values being incorrectly capped at 3600 seconds.
- Added explicit detection of Development Mode `QUOTA_EXCEEDED` 429 responses.
- Added conservative quota cooldown fallback: 1h, 2h, 4h, 8h, then 12h when Spotify provides no retry time.
- Manual retry and settings changes no longer bypass an active Spotify rate/quota cooldown.
- Reduced Web API polling from ~3s playing / ~8s idle to ~15s playing / ~60s idle to lower long-running Development Mode quota usage.
- Progress and `{cycle:...}` templates now advance locally between API polls, preserving smooth title rotation without spending extra Spotify quota.
- Reliability status now renders long cooldowns in readable minute/hour/day form.
- Keeps the last valid Honorific title during temporary Spotify failures as before.

## 1.0.0

- First stable release of SpotifyTrackHonorific.
- Promoted directly from the fully tested v0.0.14 release candidate.
- Standalone Spotify Web API -> Honorific integration with regular and local tracks.
- Configurable templates, rotating titles, cleanup and 32-character smart fitting.
- Honorific color/glow and trust-gated supporter gradient/animation support.
- Rate-limit-aware Spotify reliability and recovery handling.
- Release-oriented Home / Title / Appearance / Advanced settings UI.
- No runtime behavior changes from the passed v0.0.14 RC.

## 0.0.14 - Release Candidate

- Feature freeze for final v1.0 regression testing.
- Release-facing metadata and documentation cleanup.
- Added repeatable release-package helper and RC checklist.
- Centralized the displayed plugin version to avoid mismatched UI/chat version strings.
- No Spotify, formatting, Honorific, supporter-style, configuration-schema, or polling behavior changes intended.

## 0.0.13

- Reworked settings into Home / Title / Appearance / Advanced.
- Added first-run onboarding, clearer status wording, title presets, improved preview, and safer maintenance actions.

## 0.0.12

- Added Spotify reliability/backoff handling, rate-limit awareness, recovery status, manual retry, and concurrency guards.

## 0.0.11

- Added named Honorific gradient-preset and animation-style dropdowns using Honorific's loaded metadata.
- Corrected custom gradients to use all three Honorific color slots.

## 0.0.10

- Added trust-gated Honorific supporter gradient/animation controls.

## 0.0.9

- Added Honorific title color and glow controls.

## 0.0.8

- Improved smart-fit punctuation/separator cleanup.

## 0.0.7

- Added bracketed track-name cleanup and smart-fit title shortening.

## 0.0.6

- Added working `{cycle:SECONDS|...}` formatting.

## 0.0.4

- Added extended Spotify template variables including album, duration, progress, local-file and pause state.

## 0.0.3

- Added the first configuration UI and live title-format updates.

## 0.0.2

- First stable standalone Spotify -> Honorific core with regular tracks, local files, authentication persistence, and no DAH/Discord dependency.
