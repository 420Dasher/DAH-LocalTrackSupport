# SpotifyTrackHonorific v1.0.0 Release Smoke Test

v1.0.0 is runtime-identical to the passed v0.0.14 RC except for version/release metadata.

Before publishing the generated ZIP:

- Build v1.0.0 successfully with .NET 10 / Dalamud API 15.
- Load it over the tested v0.0.14 configuration.
- Confirm Spotify authorization and all settings carry over unchanged.
- Confirm `/sth status` and the settings header report v1.0.0.
- Play a regular Spotify track and confirm the Honorific appears.
- Play a Spotify local file and confirm the Honorific appears.
- Confirm track switching, pause/resume and configured clear behavior.
- Confirm the active template/cycle still renders correctly.
- Confirm smart-fit/bracket cleanup if enabled.
- Confirm color/glow and supporter gradient/animation settings if enabled.
- Confirm Home / Title / Appearance / Advanced open without errors.
- Reload the plugin while Spotify is playing and confirm it recovers normally.
- Run `build-release.ps1` and verify the versioned release ZIP is created.

If any runtime regression is discovered, do not silently replace the v1.0.0 package after publishing. Fix it as a new version.
