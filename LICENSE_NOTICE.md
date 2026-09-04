# License / attribution

**DAH-LocalSpotifySupport** is a modified fork/build of `anya-hichu/DiscordActivityHonorific`.

Fork creator / maintainer credit: **Dash + AI**

Upstream project:
https://github.com/anya-hichu/DiscordActivityHonorific

The upstream project declares `AGPL-3.0-or-later`. Any distributed modified binary should be accompanied by access to its corresponding modified source under the applicable AGPL terms. The retained corresponding source is organized under `legacy/DAH-LocalSpotifySupport/source/`, with the custom overlay under `legacy/DAH-LocalSpotifySupport/overlay/`.

The legacy rebuild helper is located at `dev/tools/publish-legacy-dah.ps1` and refreshes the corresponding source snapshot when used.

Spotify local-file fallback additions use SpotifyAPI-NET / SpotifyAPI.Web and PKCE authentication. No Spotify client secret is stored or required by this fork.
