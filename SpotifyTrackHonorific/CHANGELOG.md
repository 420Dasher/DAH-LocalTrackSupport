# Changelog

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
