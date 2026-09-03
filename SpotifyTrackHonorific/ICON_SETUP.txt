SpotifyTrackHonorific v1.0.1 - installer icon dev setup

This package is runtime-identical to the v1.0.1 hotfix test build.
Only plugin metadata/assets were changed:
- images/icon.png added (512x512)
- <IconUrl> added to SpotifyTrackHonorific.csproj

Canonical icon URL expected by the dev/release manifest:
https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/SpotifyTrackHonorific/images/icon.png

IMPORTANT:
The image must exist at SpotifyTrackHonorific/images/icon.png in the GitHub repo before Dalamud can load the remote icon.
Do not interrupt an active endurance test just to reload this build; swap to it after the current test if you want to preserve the uninterrupted-session test.

Build as usual:
  .\build-dev.ps1

For the eventual custom-repo release, publish.ps1 must also add the same IconUrl property to the SpotifyTrackHonorific entry in pluginmaster.json.
