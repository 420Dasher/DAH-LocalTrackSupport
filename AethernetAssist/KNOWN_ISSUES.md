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

## Housing and residential Aethernet

Status: Validated through DEV4.1 TEST3.

- Ward selection 1-30 uses native game menus.
- City-to-ward and same-district ward changes tested.
- Mist and Goblet walk-through gates support vnavmesh movement.
- Manual movement cancels automatic gate travel and pending state.
- Lavender Beds, Shirogane and Empyreum use native entrance NPCs.
- Main Division and Subdivision are detected through HousingManager.
- Residential destinations are loaded from HousingAethernet.
- Residential teleportation is validated in all five districts.
- Same-division and cross-division travel were tested.
- Proximity auto-open/close is configurable (8-11.1 yalms).
- Manually opened windows remain open.
- Residential teleport callbacks use native name, state and ID
  validation, with no more than two guarded attempts.

### Remaining follow-ups

- Flame Private NPC detection was previously noted as missing.
- Residential Ready-indicator color may need cosmetic polish.
- Cosmic Exploration navigation is planned for DEV4.2.
- World Visit functionality is not yet implemented.

### Cleanup v0.0.24

- Removed ResidentialResearch.cs.
- Removed ResidentialObjectResearch.cs.
- Removed per-frame residential object snapshots.
- Removed large native research reports.
- Retained lightweight operational travel diagnostics.
- Kept live residential and Housing travel implementation intact.

Historical development notes remain available in Git history.