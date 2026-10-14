# AethernetAssist - Known Issues

## Intermittent TelepotTown callback non-acknowledgment

First observed during v0.0.10 DEV3 FIX2 testing.

Affected destinations:
- Kugane: Pier #1, native row 3, callback 2
- Eulmore: Nightsoil Pots, native row 4, callback 3
- Tuliyollal: The For'ard Cabins, native row 4, callback 3

Observed:
- Correct destination names were matched.
- Two native callbacks were submitted.
- TelepotTown remained open.
- No territory transition or detectable movement occurred.
- All three exact destinations later worked without code changes.

Current conclusion:
- Intermittent, not reproducibly city-specific.
- Native-menu readiness, game state, and callback timing are
  possible causes but remain unconfirmed.

Future investigation:
- Record elapsed time since the TelepotTown addon appeared.
- Track menu changes between the first and second callback.
- Compare native CurrentDestination before and after selection.
- Capture occupancy, animation, and interaction conditions.
- Avoid blind retries or changing the proven callback format
  until a reproducible failure pattern is established.

Status: OPEN - low frequency, investigation deferred.

## Housing

DEV4 FIX2 scope:
- Automatically detected residential district from the nearby main city Aetheryte.
- Clickable ward grid (1-30) through native city Aetheryte menus.
- Current-city main Aetheryte required.
- English native menu matching initially.
- Subdivision navigation is intentionally manual.
- Ward travel pending in-game validation.

Housing diagnostics are shown exclusively in Settings >
Diagnostics alongside Aethernet diagnostics. Future DC Travel
diagnostics should use the same section without additional
main-window buttons.

DEV4 FIX3 ward entrance coverage:
- Native NPC exits: Lavender Beds, Shirogane, Empyreum.
- Native walk-through gates: Mist, Goblet.
- Leaving the residential district uses the native exit menu.
- Gate menus require manual movement to trigger.
- NPC Talk dialogue may require manual advancement.
- Same-territory ward transfers require observed loading.
- In-game validation of each native exit remains pending.
- Subdivision navigation remains manual.

DEV4 FIX4 reliability changes:
- ConfirmTravel no longer accepts matching territory alone as success.
- Native HousingManager current ward must match the requested ward.
- Existing Yes/No and ward-selection menus must close before arrival.
- Entrance NPC Talk dialogs advance automatically during plugin travel.
- Already occupied ward is marked and cannot be reselected.
- Verify both city-to-ward and same-territory ward-to-ward travel.