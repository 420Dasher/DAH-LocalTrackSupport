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
## DEV4.2 Cosmic Exploration Research 1

- Starting point: verified v0.0.24 residential baseline.
- Goblet regression passed after the residential cleanup.
- Drivingway reference: Mare Lamentorum (21.9, 13.2, -1.4).
- NPC BaseID and world coordinates pending live capture.
- Drivingway native dialogue/menu structure pending capture.
- Teleport and vnavmesh automation not implemented yet.
- Local Cosmoliner transportation will be researched separately.
- Field operations intentionally excluded from planned scope.
## DEV4.2 Research 2 (v0.0.26)

- Drivingway verified at NPC BaseID 1052581.
- World position (25.681, -137.417, -411.307).
- Initial SelectString row 0 opens Cosmic Exploration travel.
- Planet selector not identified by standard addon checks.
- Temporary read-only AddonLifecycle PostDraw capture added.
- No Cosmic travel callbacks or movement enabled yet.
## DEV4.2 Research 3 (v0.0.27)

- Drivingway opens the confirmed addon WKSPlanetSelect.
- WKSPlanetSelect reports 24 AtkValues.
- Added on-demand AtkValue inspection.
- Added native UI node ID, type and text inspection.
- No selection callbacks or planet switching automated yet.
- Purpose: identify current destination and confirmation controls.
## DEV4.2 TEST1 (v0.0.28)

- Four new /anet Cosmic destination commands.
- TEST1 starts only within interaction range of Drivingway.
- Native selection validates the current planet text node.
- Arrow event params 1/2 and Blast Off param 7 are experimental
  on this client and require in-game testing.
- Maximum five navigation clicks, with transition validation.
- No automatic vnavmesh movement or initial teleport yet.
- No changes to city, Housing, or residential travel.