# APGambaHax

## DEV2 TEST5 - v0.1.8.0

- Dalamud API 15 / .NET 10
- Native testing-exclusive release
- Verified Barkeep daily net limit: 20,000 chips
- Up to +2,000 net per perfect shift
- Maximum 10 fresh profitable perfect shifts per day
- Batch maximum decreases with known server daily net
- Stop on Capped flag OR reported NetWinToday >= 20,000
- Existing shift, profit, loss and manual stops remain
- Existing Pause / Resume retained
- Shared cap-policy foundation for future game modules
- Displays live global house-game per-bet ceiling
- Per-bet clipping is not treated as a daily cap
- Single-scroll compact UI and theme matching retained
- Optional plugin icon picker

Important limitation:
Aetherphone's regular casino state does not contain Barkeep
NetWinToday. This value is available after a Barkeep settlement.
After a plugin restart, the initial daily net is unknown.
A provisional UTC date change expires the previous observation,
but the actual server reset time is not established.

Only Barkeep has a confirmed game-specific daily net cap.
Other game automation modules are not implemented in this release.
