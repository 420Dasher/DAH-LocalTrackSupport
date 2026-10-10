# BlunderNav DEV5

Version: 0.0.10.0
Dalamud API 15 / .NET 10

## Visual-only navigation

BlunderNav never controls character movement.

Splatoon provides in-world rendering.
vnavmesh provides path suggestions only.

## DEV5: Automatic course selection

- Uses territory 1197 for lobby and 1165 for duty
- Matches player position to existing recorded routes
- Compares three-dimensional distance to route segments
- Requires a route match within 8 yalms
- Requires at least 4 yalms separation from competing routes
- Requires three stable detection scans
- Refuses ambiguous or unknown matches
- Leaves recorded routes unchanged
- Manual route selection disables automatic selection
- Re-enable via Select course automatically

Automatic course selection is a conservative first stage.

Stages that share the same territory and overlapping
route geometry cannot yet be reliably distinguished.

## DEV5: Diagnostic capture

Open /bnav and expand:

DEV5 - Object and mechanic diagnostics

Select Capture nearby objects.

Then select Copy diagnostic report.

The report includes:
- Territory ID
- Player location
- Selected profile and detection status
- Saved route information
- Up to 60 nearby game objects
- Data IDs, object IDs, positions and object kinds
- Recently observed cast action IDs

Use captures from different Fall Guys courses to
establish reliable course/object signatures.

## Not implemented yet

Automatic identification of real Fall Guys danger zones.

The existing red test circle remains a manual marker.
It must not be treated as confirmed mechanic detection.

## Existing features retained

- Start visual guidance anywhere
- Rejoin from current position
- Automatic checkpoint advancement
- Splatoon route and checkpoint overlays
- Read-only vnavmesh suggestions
- Route recording
- Manual test hazard and avoidance experiment

## Testing

1. Enter the Blunderville lobby.
2. Verify saved route automatically selects if distinct.
3. Start visual guidance and walk manually.
4. Verify checkpoint advancement still works.
5. Switch to a course with a recorded route.
6. Check whether profile selection is confident.
7. Capture diagnostic reports during different courses.
8. Manually select a route when detection is ambiguous.