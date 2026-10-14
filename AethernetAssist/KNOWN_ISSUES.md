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
DEV4 FIX5:
- Gate detection now uses the configured 8-11.1-yalm range.
- Proximity-opened Housing closes after leaving that range.
- Manually opened windows are preserved.
- Active Housing operations prevent premature auto-close.
- Optional vnavmesh gate walk, started only by user action.
- Stop movement on menu appearance, keyboard input, or timeout.
- Manual gate travel remains available without vnavmesh.
- Housing dialogue delays reduced conservatively.
- Obsolete DataId/String8 references replaced.
- Automatic gate movement pending in-game validation.
- Flame Private NPC detection not yet implemented.
DEV4 FIX6:
- Manual movement now cancels the entire active gate-travel operation.
- Both vnavmesh movement and Housing state machine are stopped.
- Cancellation works while walking and while awaiting the native gate menu.
- Manual fallback remains available when vnavmesh did not start.
- Existing Cancel button and /anet cancel remain unchanged.
- Existing ward travel, NPC interactions, and Aethernet callbacks unchanged.
- Mist and Goblet auto-walk and Gridania NPC routes were user-tested in FIX5.
DEV4 FIX7 - Unified proximity window:
- Aethernet shards, main Aetherytes, Housing NPCs, and
  housing gates share the configured proximity range.
- Auto-close applies to every proximity-opened window.
- Manual /anet and Open Main UI windows are preserved.
- Active Aethernet and Housing travel is not interrupted.
- Re-evaluates the auto-window after territory changes.
- Removed duplicate Housing and Aethernet close handlers.
- Native travel callbacks and gate movement are unchanged.
- Lavender Beds NPC proximity close requires validation.
DEV4.1 RESEARCH 1 (v0.0.19):
- Read-only residential Aethernet diagnostics added.
- HousingAethernet sheet inspected by territory and order.
- HousingManager division and ward state captured.
- Nearby shard object identities, distances, and XYZ captured.
- Native SelectString and TelepotTown names captured if visible.
- Main/subdivision row split is provisional, not validated.
- No residential travel callbacks are implemented yet.
- All previously tested ward and gate automation preserved.
- Next: verify row names/order against native housing menus,
  then implement residential shard teleportation.
DEV4.1 RESEARCH 2 (v0.0.20):
- Main and subdivision menus independently confirmed to
  contain the same 18 native entries in Lavender Beds.
- Added nearby unfiltered non-player object inspection.
- Added pre-menu object snapshot caching.
- Added native TelepotTown row type/state/callback inspection.
- Residential transport callback remains disabled.
- Known-working city and Housing navigation unchanged.