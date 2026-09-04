# Developer files

Everything in this directory exists for development, release preparation, verification, or historical reference. None of it is required by Dalamud when installing SpotifyTrackHonorific from the custom repository.

## tools

- `publish.ps1` — build the active SpotifyTrackHonorific source, copy the package to `plugins/SpotifyTrackHonorific/latest.zip`, update the manifest, and run release verification.
- `verify-release.ps1` — version-independent release consistency check.
- `publish-legacy-dah.ps1` — legacy-only rebuild helper for the discontinued DAH fork.
- `verify-layout.ps1` — checks that the repository remains in the organized layout and runtime URLs are unchanged.

## docs

Release workflow/checklist and other maintainer documentation.

## release-notes

Version-specific release notes retained for reference.

## archive

Old test plans or one-off development notes that are no longer part of the active plugin documentation.
