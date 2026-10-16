# APGambaHax

## DEV2 TEST5 FIX1 - v0.1.9.0

- Dalamud API 15 / .NET 10
- Native testing-exclusive release
- Barkeep daily net-win ceiling: 20,000 chips
- Account-specific persisted server daily observations
- Stores SHA256 account identifiers, never raw account IDs
- Reuses Aetherphone's existing history cache
- History positive winnings act as a conservative cap signal
- Unknown daily total blocks new automatic batches
- Confirmed daily totals cap the maximum batch length
- Prior settled results cannot masquerade as fresh settlements
- Existing Pause, Resume, Stop, grading and payout logic retained
- Shared per-bet ceiling display retained
- Compact single-scroll interface and theme matching retained
- Optional 512x512 icon picker

The actual server daily reset instant is not established.
A new UTC date invalidates an earlier saved observation.
History below the cap cannot by itself establish the exact
server daily total. A fresh confirmed settlement is required
before starting automatic batches when no saved observation
exists for the account and current UTC date.

Other games do not have verified game-specific daily profit
limits in the current client source.
