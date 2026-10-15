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

For v1.0.1 the expected package is:

`release/SpotifyTrackHonorific-v1.0.1.zip`

## Custom repository

The target custom repository is:

`420Dasher/Dashs-Plugins`

This is a multi-plugin repository. Stable builds are published through main, while testing builds use the native Dalamud testing channel.

DAH-LocalSpotifySupport and BlunderNav are retired and must not be republished.
