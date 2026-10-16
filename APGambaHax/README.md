# APGambaHax

## Rewards DEV1 - v0.1.11.0

- Dalamud API 15 / .NET 10
- Native testing-exclusive release
- Opt-in automatic claims for eligible bonuses
- Per-bonus-type enable/disable selection
- Manual claim buttons for currently eligible bonuses
- Uses Aetherphone CasinoStore.ClaimBonus
- No direct HTTP requests or authentication duplication
- Checks Ready and Eligible before each request
- One outstanding bonus request at a time
- Throttles repeated attempts for the same bonus
- Account identity rechecked before a claim
- Shows request status, not fictitious success messages
- Free daily spin is display-only for now
- Existing Barkeep automation and safeguards unchanged
- Compact theme-aware interface retained
- Optional 512x512 icon picker

Next module: Missions progress and claiming.

Rewards require Aetherphone to be signed in and its
casino state to be loaded. The plugin can submit a
claim but Aetherphone remains responsible for
processing and displaying the server response.
