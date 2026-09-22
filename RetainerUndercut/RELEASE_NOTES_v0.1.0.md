# Retainer Undercut v0.1.0

First public release of Retainer Undercut.

This release includes preview-first current-retainer and multi-retainer repricing, attached Quick Run controls, fixed/percentage undercutting, Matchlist protection, optional Price Recovery, per-item rules, pricing safety guards, run history, strict HQ/NQ separation, market-board request correlation, stale-price correction, controlled throttling retries, and post-confirm verification.

The release is based on the fully tested 0.1.0.62 development candidate; release cleanup only changes branding/documentation and Dry Run wording (`Would recover` instead of `Price recovered`). The pricing engine itself is unchanged.

Known limitations:

- English client is currently required for the `Adjust Price` context-menu validation.
- Quote caching is intentionally conservative and may perform a fresh market check for the same item on a later retainer.
