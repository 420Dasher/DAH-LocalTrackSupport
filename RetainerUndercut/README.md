# Retainer Undercut

Retainer Undercut is a Dalamud plugin for fast, preview-first repricing of FFXIV retainer market listings.

It is built around live game state rather than fixed timing chains: each listing is revalidated before interaction, market results are correlated to the active request, HQ and NQ listings stay separate, and confirmed changes are checked against the retainer's live market slot afterward.

## Highlights

- **Dry Run / Live Run** for the current retainer or all enabled retainers.
- **Quick Run controls** attached beneath both the Retainer List and individual Markets window.
- **Strict HQ/NQ separation**, including native market-cache cross-checking.
- **Fixed-gil or percentage undercutting**.
- **Matchlist** support to match protected sellers instead of undercutting them.
- **Price Recovery** to safely raise an already-cheapest listing toward the next eligible competitor.
- **Per-item rules** for Ignore, pricing overrides, minimum price, and maximum one-run drop.
- Optional **minimum-price**, **max-drop**, **outlier**, and **max-raise** safeguards.
- **Multi-retainer automation** with per-retainer enable/disable.
- **Run history** with before/after prices, proposed changes, decisions, and reasons.
- Market-board throttling detection, controlled retries, emergency stop, and post-confirm verification.

## Usage

Open the plugin with:

```text
/rundercut
```

The Dashboard provides four main actions:

- **Preview current retainer** — runs the full pricing logic without writing prices.
- **Apply to current retainer** — applies the same decisions to the currently open retainer.
- **Preview all enabled retainers** — visits enabled retainers and builds a read-only preview.
- **Apply to all enabled retainers** — runs the live repricing flow across enabled retainers.

The attached Quick Run controls expose the same current-retainer or all-retainer actions directly from the native retainer windows.

## Pricing behavior

The default rule is a fixed **1 gil undercut**. Percentage undercutting is also available.

Retainer Undercut excludes your own retainers when choosing the normal competitor price. HQ and NQ listings are evaluated independently. If a seller at the cheapest eligible tier is on the Matchlist, the plugin matches that tier instead of undercutting it.

**Price Recovery** is optional and disabled by default. When enabled, a listing that is already strictly cheapest can be raised toward the next eligible competitor while remaining cheapest. Recovery can require a minimum gap and can be capped by a maximum one-run raise percentage.

## Safety features

All optional pricing guards are conservative: when a guard triggers, the listing is skipped rather than guessed.

- **Minimum price** prevents a proposed price from dropping below a configured floor.
- **Maximum drop** limits how much one run may reduce a listing.
- **Outlier protection** can block repricing against an isolated suspiciously low cheapest tier.
- **Maximum recovery raise** limits upward Price Recovery moves.
- **Item rules** can override global pricing or exclude a specific Item ID + HQ/NQ variant entirely.

Before writing a price, the plugin verifies the active retainer, selected raw market slot, item identity, HQ/NQ state, quantity, current price, sell-list row mapping, and asking-price readback. After confirmation it verifies the live retainer market slot again.

## Market-board handling

Market results arrive in multiple packets. Retainer Undercut accumulates them, correlates them to the native active request ID, rejects stale same-item responses, and cross-checks the native `InfoProxyItemSearch` cache before using the quote. This is also used to correct stale packet prices and HQ/NQ disagreements when the native current-request listing is authoritative.

Same item + quality results are cached within a current-retainer run to avoid unnecessary repeated searches. The cache is intentionally conservative and may be refreshed again when moving to another retainer.

## Building

Requirements:

- Dalamud API 15 development environment
- .NET 10 SDK

From PowerShell:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\BUILD_DEV.ps1
```

The script restores and builds the project, locates `RetainerUndercut.dll`, and copies `images/icon.png` next to the built DLL so the Dalamud dev-plugin entry displays the correct icon.

Then add the reported output directory under:

```text
/xlsettings -> Experimental -> Dev Plugin Locations
```

For a Release configuration:

```powershell
.\BUILD_DEV.ps1 -Configuration Release
```

## Current limitations

- The context-menu validation currently targets the English client's **Adjust Price** entry.
- Quote caching is scoped conservatively; identical items on a later retainer may perform a fresh market check.

## License

MIT. See [LICENSE](LICENSE).

Retainer Undercut is an unofficial third-party plugin and is not affiliated with or endorsed by Square Enix.
