# APGambaHax

Modular Aetherphone Gamba companion.

## DEV2 TEST1 - v0.1.1.0

- Dalamud API 15 / .NET 10
- Native Dalamud testing channel
- Compact AethernetAssist-style UI
- Six configurable modules
- Default dark and matched Dalamud colors
- Read-only Aetherphone reflection bridge
- Casino bankroll and level snapshots
- Bonus eligibility and cooldown display
- Daily-spin cached status
- Barkeep shift progress
- Manual reminder and payout estimator

The bridge uses Aetherphone's internal objects via reflection.
It does not access account tokens, send network requests,
modify shift grades, claim rewards or initiate wagers.

Unverified integration against future Aetherphone releases may
stop working. DEV2 TEST1 tests the read-only connection first.
