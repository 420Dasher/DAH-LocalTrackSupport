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
## DEV4.2 TEST2 (v0.0.29)

- Confirmed /anet phaenna can select the correct planet
  and activate the native Blast Off control.
- Native Yes/No confirmation observed in TEST1.
- Added automatic Yes response only for the exact
  "Travel to the selected destination?" prompt.
- Verifies the first native button is Yes.
- Revalidates WKSPlanetSelect Node 17 against the target.
- Submits at most one Yes callback.
- Unknown confirmation prompts are not accepted.
- Tracks confirmation status and arrival separately.
- Bestways Burrow teleport and vnavmesh travel are
  not implemented yet.
- Housing and residential implementations unchanged.
## DEV4.2 TEST3 (v0.0.30)

- Native Drivingway travel tested successfully to
  Sinus Ardorum, Phaenna, Oizys and Auxesia in v0.0.29.
- Added full journey from other ordinary territories.
- Bestways Burrow Aetheryte ID: 175.
- Drivingway world XYZ: (25.681,-137.417,-411.307).
- Added vnavmesh pathfinding, ownership tracking,
  manual movement cancellation and navigation timeouts.
- Missing unlocks and unavailable IPC stop safely.
- Arrival from distant starting zones pending live test.
- Cosmic local Cosmoliner networks remain future work.
## DEV4.2 LOCAL RESEARCH1 (v0.0.31)

- Sinus Ardorum shard verified:
  targetable EventObj BaseID 2015100.
- Non-targetable overlapping BaseID 2015052 observed.
- Existing ordinary shard detection doesn't recognize
  the targetable Cosmic EventObj.
- WKS sheet mapping is required for the network list.
- Added read-only WKS records and addon discovery.
- Destination callbacks remain unmodified and untested.
- No Cosmoliner automation planned.
- Working Cosmic journey and residential code preserved.
## DEV4.2 LOCAL RESEARCH2 (v0.0.32)

- Confirmed native Cosmic Aethernet addon is TelepotTown.
- Confirmed standard Aetheryte sheet contains no
  destinations in Sinus Ardorum.
- WKS sheets provide the 20 shard records.
- Inspecting native callback values before automation.
- All working journey and residential code preserved.
## DEV4.2 LOCAL TEST1 (v0.0.33)

- First live local Cosmic Aethernet integration.
- Native TelepotTown menu confirmed in Sinus Ardorum.
- Header slot 0, destination slots 1-5.
- Callbacks 0-4 mapped from WKS subrow ordering.
- Only states 0 and 4 accepted for selectable destinations
  pending further in-game verification.
- Sends one native selection callback only.
- All four planet catalogs are available.
- Test Sinus Ardorum first before other planets.
- Existing city, Housing, residential and
  Cosmic journey implementations remain unchanged.