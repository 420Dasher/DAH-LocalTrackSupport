# Release workflow

## Local development / smoke test

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-dev.ps1
```

Point Dalamud Dev Plugin Locations at `built-plugin`.

## Build a distributable package

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build-release.ps1
```

The script performs a clean build and creates a versioned ZIP plus SHA-256 checksum under `release/`.

For v1.0.0 the expected package is:

`release/SpotifyTrackHonorific-v1.0.0.zip`

## Custom repository

The target custom repository is:

`420Dasher/DAH-LocalTrackSupport`

It remains a multi-plugin repository. `DAH-LocalSpotifySupport` stays available as a discontinued legacy entry while `SpotifyTrackHonorific` is the active plugin.

Use the repository integration publisher supplied with the v1.0.0 repo kit. It preserves unrelated existing manifest entries, marks the old DAH fork discontinued, builds SpotifyTrackHonorific, copies its `latest.zip`, and writes `pluginmaster.json` as a JSON array.
