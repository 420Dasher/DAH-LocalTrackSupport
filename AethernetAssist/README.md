# AethernetAssist v0.0.40 DEV4.3 WORLD VISIT TEST2 FIX1

Dalamud API 15 / .NET 10
Development branch: testing

## Features

- Contextual Aethernet travel interface
- Native city Aethernet destinations and attunement checks
- Main and subdivision residential Aethernet destinations
- Residential support in all five housing districts
- Ward selection (1-30) from city and residential entrances
- Automatic Mist and Goblet gate navigation via vnavmesh
- Native NPC travel for Lavender Beds, Shirogane and Empyreum
- Configurable proximity auto-open and auto-close
- Manually opened windows retain their visibility
- City-style destination rows and scrolling
- Manual movement interruption and travel cancellation
- Operational diagnostics in Settings

## Commands

- /anet - Open navigation window
- /anet housing - Open Housing page
- /anet settings - Open settings
- /anet debug - Toggle diagnostics
- /anet cancel - Cancel active navigation

## Development status

DEV4.1 residential networks were tested in:
Mist, Lavender Beds, Goblet, Shirogane and Empyreum.

Residential research modules were removed in v0.0.24.
Operational transport diagnostics remain available.

Cosmic Exploration is planned for DEV4.2.
World Travel is not yet implemented.
## DEV4.2 Cosmic Exploration

Planned command destinations:
- /anet sinus
- /anet phaenna
- /anet oizys
- /anet auxesia

DEV4.2 RESEARCH1 only captures NPC and native-menu data.
No Cosmic Exploration travel command is active yet.

Drivingway reference: Mare Lamentorum map X21.9 Y13.2 Z-1.4.
The raw world XYZ position must be obtained from the object table.
## Cosmic Travel TEST1

Commands:
- /anet sinus
- /anet phaenna
- /anet oizys
- /anet auxesia

TEST1 requires standing near Drivingway in Mare Lamentorum.

The plugin attempts to:
- Interact with verified Drivingway NPC 1052581
- Advance Talk
- Select native row 0 by exact text
- Verify WKSPlanetSelect names and current node 17 text
- Switch planets with a bounded native event sequence
- Request Blast Off only when the correct planet is selected
- Verify the destination territory after travel

Automatic teleport to Bestways Burrow and vnavmesh movement
are planned for a later test build.
## DEV4.2 TEST3 - Automatic Cosmic Travel

Supported commands:
- /anet sinus
- /anet phaenna
- /anet oizys
- /anet auxesia
- /anet cancel

From outside Mare Lamentorum:
1. Teleport to Bestways Burrow (Aetheryte ID 175).
2. Wait for territory 959 and loaded player.
3. Use vnavmesh to approach Drivingway.
4. Interact with targetable NPC 1052581.
5. Select the requested planet using the validated
   native WKSPlanetSelect travel sequence.
6. Automatically confirm the exact travel prompt.
7. Verify arrival in the requested Cosmic territory.

Inside Mare Lamentorum:
- Skip teleport and pathfind directly to Drivingway.
- Skip pathfinding when already in interaction range.

Safety:
- Won't take over an existing vnavmesh route.
- Requires Bestways Burrow to be unlocked.
- Stops only the vnavmesh path it started.
- Manual movement cancels navigation.
- /anet cancel cancels active travel.
- Timeouts guard teleport, pathfinding and arrival.
- Existing residential and city travel remains unchanged.

vnavmesh is required when pathfinding is needed.
## DEV4.2 LOCAL RESEARCH1

Scope: local Aethernet shards ONLY. No Cosmoliners.

The four Cosmic travel commands are already tested.
This version does not modify those implementations.

The Cosmic Exploration shard networks use the WKS
sheets instead of the ordinary city Aethernet records.

Research captures:
- Nearby EventObj shard BaseIDs and targetability
- Standard Aetheryte sheet membership
- WKSAetheryte ID, name and placed EObj group IDs
- WKSAethernet network metadata
- WKSAetheryteList destination metadata
- Native addon names during manual shard interaction

In Settings > Diagnostics, start Cosmic capture, open a
Cosmic Aethernet Shard manually, and copy diagnostics
while its destination menu is open.

No native callbacks are submitted by research code.
## DEV4.2 LOCAL RESEARCH2

The Cosmic local Aethernet uses TelepotTown.

Read-only diagnostics now expose:
- WKSAethernet and WKSAetheryteList group mapping
- Resolved native WKS destination names
- TelepotTown name slots
- Native slot type, state and callback values

Open a Cosmic shard and copy diagnostics while
TelepotTown remains visible.

Existing Cosmic journey and city/residential travel
are unchanged. No Cosmoliner automation.
## DEV4.2 LOCAL TEST1

Native Cosmic Exploration Aethernet support.

- Auto-detect WKS EventObj Aethernet shards.
- Resolve WKS destination lists for four planets.
- Display five local destinations in the existing
  AethernetAssist city-style window.
- Identify the current shard by its object-group IDs.
- Validate native TelepotTown planet header,
  network ID, five destination names and callback IDs.
- Submit exactly one guarded callback.
- Confirm arrival using game state and position.
- Support /anet cancel and manual movement cancellation.

The native selection callback is experimental in TEST1.
No Cosmoliners are included.

Previous Cosmic journey automation is unchanged.
## DEV4.2 LOCAL TEST2

Fixes Cosmic TelepotTown destination selection.

TEST1 confirmed:
- Proximity detection and auto-window opening/closing
- Correct current-shard identification
- Correct destination and callback ID
- Detection at the destination after manual teleport

TEST2 adds:
- Two-stage native TelepotTown selection when necessary
- 250 ms delay before the second callback
- Complete menu and destination revalidation
- Maximum two callback attempts
- Arrival verification and explicit failure diagnostics

The Cosmic journey and all other Aethernet systems
remain unchanged.
## DEV4.2 LOCAL TEST3 (v0.0.35)

Fixes Cosmic local Aethernet validation on Phaenna.

The TelepotTown AtkValues[1] field does not represent
the WKSAethernet network row ID. Phaenna reports 1,
while its WKS network row is 2.

Removed the incorrect ID comparison.

Retained:
- Territory and native planet name validation
- Native menu layout checks
- All five destination-name validations
- Destination availability checks
- Callback-index verification
- Guarded two-stage selection with 250 ms interval
- Two-callback maximum and arrival verification

Previously working Cosmic journey, city, residential
and housing implementations remain untouched.

No Cosmoliner automation.
## DEV4.2 UI POLISH (v0.0.36)

Testing branch only. NOT a stable release.

Navigation tabs are context-sensitive:
- Aethernet remains available.
- Housing appears near supported city Aetherytes
  or residential ward entrances.
- Housing stays available during active housing travel.
- DC Travel is hidden until it is implemented.
- Available tabs automatically fill the window width.
- /anet housing observes the same availability rule.

Cosmic cleanup:
- Removed temporary addon capture.
- Removed read-only WKS research panels.
- Retained working local Cosmic subnet travel.
- Retained Cosmic journey and local travel diagnostics.
- No changes to native Cosmic teleport callbacks.

Cosmic subnet testing completed across all four planets.

DC Travel is still unfinished. Do not release to main.
## DEV4.3 WORLD VISIT RESEARCH1 (v0.0.37)

Scope: Same-data-center World Visit only.

The DC Travel tab is visible only at the main
Aetherytes in:
- Limsa Lominsa (territory 129)
- New Gridania (territory 132)
- Ul'dah (territory 130)

The actual main Aetheryte must be targetable and
within the configured plugin proximity range.

The tab is hidden near regular Aethernet shards,
residential shards, housing entrances, Cosmic shards
and all other territories.

This version adds read-only inspection of:
- SelectString World Visit options
- WorldTravelSelect native addon and AtkValues
- Native visible destination labels

World Visit selection and travel confirmation
are not automated yet.

Cross-data-center travel is explicitly out of scope.
Do not push to main.
## DEV4.3 WORLD VISIT TEST1 (v0.0.38)

Same-data-center World Visit selection testing.

- DC Travel appears only at the three original
  starting-city main Aetherytes.
- World names are read from the native WorldTranslate
  destination list.
- Names are verified against the game's World sheet.
- DataCenter IDs must match the current World.
- Every native destination must have a matching
  visible WorldTravelSelect label.
- Current World is not selectable.
- Native callback uses the validated destination
  index plus two, consistent with World Visit.
- The in-game Yes/No confirmation is manual in TEST1.
- No automatic logout or cross-DC travel.
- No changes to Cosmic, Housing or residential travel.

Retains the complete read-only research diagnostics.

Stay on testing. Never release to main automatically.
## DEV4.3 WORLD VISIT TEST2 (v0.0.39)

Same-DC one-click World Visit development.

- Show available same-DC worlds before opening
  the game's native World Visit menu.
- Interact with the supported city Aetheryte.
- Select Visit Another World automatically.
- Revalidate the exact target world against the
  native WorldTravelSelect destination list.
- Use the previously tested native world callback.
- Confirm automatically only when the prompt
  explicitly identifies the destination.
- Otherwise leave confirmation to the player.
- Monitor World Visit queues without auto-retrying.
- Verify arrival using the actual current world.
- /anet cancel stops plugin-side monitoring.
- No cross-DC travel or character-selection automation.

Testing branch only. Do not release to main.
## DEV4.3 WORLD VISIT TEST2 FIX1 (v0.0.40)

Fixed World Visit menu recognition.

Native SelectString options can contain formatting
prefixes. The observed Gridania entry was:

F Visit Another World Server.

The selector now recognizes the distinctive
"Visit Another World" phrase anywhere in the
cleaned text, while requiring exactly one match.

Unchanged:
- Automatic main Aetheryte interaction
- Live World Visit destination verification
- Same-DC restrictions
- Destination-specific confirmation safeguards
- World Visit queue and arrival monitoring
- Existing Aethernet, Housing and Cosmic travel

Testing only. Do not publish to main.