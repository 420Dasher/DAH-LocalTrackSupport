# SpotifyTrackHonorific Release Workflow

## Branches

- `main` is the current stable Dalamud release.
- `testing` is the reusable staging branch.
- When no test release is active, `testing` should mirror `main`.

## Development

1. Develop and verify changes in a standalone DEV workspace.
2. Build and test locally through Dalamud Dev Plugin Locations.
3. Verify persistence and any affected features.
4. Promote the exact tested source into the repository.
5. Commit the candidate changes to `testing`.
6. Build the testing package from the committed `testing` source.

## Native Dalamud Testing Channel

The normal repository remains:

`https://raw.githubusercontent.com/420Dasher/DAH-LocalTrackSupport/main/pluginmaster.json`

Do not add the `testing` branch as a second custom repository.

The stable STH entry in `main/pluginmaster.json` advertises the test version using:

- `AssemblyVersion`: current stable version
- `TestingAssemblyVersion`: newer testing version
- `DalamudApiLevel`: current stable API level
- `TestingDalamudApiLevel`: matching testing API level
- `DownloadLinkInstall`: stable ZIP on `main`
- `DownloadLinkUpdate`: stable ZIP on `main`
- `DownloadLinkTesting`: testing ZIP on `testing`

Dalamud users can then right-click the installed plugin and select:

`Receive plugin testing versions`

## RepoUrl Compatibility Note

For STH, keep `RepoUrl` omitted from the remote pluginmaster entry unless the packaged local manifest also contains the exact same value.

A local/remote `RepoUrl` mismatch can make Dalamud show that a testing version exists while leaving `Receive plugin testing versions` disabled.

## Promoting Testing to Stable

1. Confirm the testing build works through the real Dalamud testing download path.
2. Confirm reload and configuration persistence.
3. Bring the exact tested source onto `main`.
4. Build again from `main`.
5. Verify the DLL FileVersion.
6. Replace `main/plugins/SpotifyTrackHonorific/latest.zip`.
7. Update `main/pluginmaster.json`:
   - set `AssemblyVersion` to the new stable version
   - remove the old `TestingAssemblyVersion`
   - remove the old `TestingDalamudApiLevel`
   - update the changelog
   - point `DownloadLinkTesting` back to the stable ZIP until another test build exists
8. Commit and push the stable release.
9. Test the real stable Dalamud update/install path.
10. Record the stable ZIP SHA-256.
11. Tag the release.
12. Reset `testing` to the new stable `main` baseline.

## v1.0.12 Reference

Release commit:

`5d5f17a`

Stable ZIP SHA-256:

`008EF711AC7F02D3ABAD98ECCCA18867360EBE2758BECF3262222D6926CF2B50`

Release tag:

`v1.0.12`