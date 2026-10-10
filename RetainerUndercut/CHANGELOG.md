## 0.1.3 - simplified interface and responsive retainer controls

- Redesigns the main window around the simpler Preview -> Review -> Apply workflow.
- Reduces the main navigation to Home, Item Rules, and Settings.
- Simplifies preview results and latest-run summaries for normal end users.
- Moves developer diagnostics under Advanced diagnostics in Settings.
- Renames Dry Run / Live Run controls to Preview / Apply Prices.
- Uses clearer Protected sellers wording for friend and FC price protection.
- Moves the quick controls underneath the native Adjust Price dialog while it is open, preventing overlap on smaller resolutions.
- Pricing, market-check, scanner, safety, and automation behavior are unchanged.
## 0.1.2 TEST2 - responsive Adjust Price overlay

- Moves the Retainer Undercut quick controls underneath the native Adjust Price dialog while it is open.
- Returns the controls underneath the normal retainer window when Adjust Price closes.
- Prevents the quick controls from covering Adjust Price controls on smaller resolutions.
- Pricing, market-check, scanner, safety, and automation behavior are unchanged.
## 0.1.2 TEST1 - simplified end-user interface

- Reworks the main window around the simple Preview -> Review -> Apply workflow.
- Reduces the main navigation to Home, Item Rules, and Settings.
- Hides developer diagnostics under Advanced diagnostics in Settings.
- Simplifies preview results to Item, Current, New, and Result.
- Moves retainer management out of the main workflow until it is needed.
- Renames Dry Run / Live Run wording in the retainer overlay to Preview / Apply Prices.
- Pricing, safety, market-check, scanner, and automation behavior are unchanged.
# Changelog


## 0.1.1 - 2026-10-06

- Fixes stale same-item Market Board prices being reused during repricing.
- Keeps fresh Market Board packet prices authoritative while retaining strict HQ/NQ validation.
- Fixes unnecessary HQ retries and restores normal pricing-run performance.
- Adds installed-plugin icon metadata and keeps the repository icon available to Dalamud.

## 0.1.0 - 2026-09-22

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

