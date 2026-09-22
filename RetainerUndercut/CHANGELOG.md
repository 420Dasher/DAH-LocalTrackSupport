# Changelog

## 0.1.0 — 2026-09-22

Initial public release.

### Added

- Dry Run and Live Run for the current retainer and all enabled retainers.
- Quick Run controls beneath the native Retainer List and Markets windows.
- Multi-retainer traversal with empty-retainer handling and per-retainer enable/disable.
- Fixed-gil and percentage undercut modes.
- Matchlist protection with exact-price matching at the cheapest eligible tier.
- Optional Price Recovery for safe upward repricing while remaining cheapest.
- Per-item Item ID + HQ/NQ rules with Ignore and pricing/safety overrides.
- Optional minimum-price, max-drop, outlier, and max-recovery-raise protections.
- Persistent run history and detailed preview results.
- Controlled market-board throttling retries and emergency stop behavior.

### Reliability

- Strict HQ/NQ market separation with native listing-cache cross-checking.
- Exact active market-request correlation to reject stale same-item responses.
- Native current-request price correction when packet data is stale.
- Multi-packet market result accumulation and completion validation.
- Raw retainer market-slot identity checks before interaction and after confirmation.
- Sell-list row-to-market-slot validation before opening Adjust Price.
- Asking-price write/read verification before confirmation.
- Safe hard-stop behavior when a confirmation result becomes uncertain.
