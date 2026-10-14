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

DEV4.0 initial scope:
- Five residential districts.
- Wards 1-30 through native city Aetheryte menus.
- Current-city main Aetheryte required.
- English native menu matching initially.
- Subdivision Aethernet transfers not yet implemented.
- Ward travel pending in-game validation.