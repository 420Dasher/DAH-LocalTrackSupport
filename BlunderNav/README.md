# BlunderNav DEV6.1

Version: 0.0.13.0
Dalamud API 15 / .NET 10

## Objective discovery research

DEV6.1 gathers evidence for future automatically
calculated routes. It does not guess destinations.

During each active Fall Guys course, it observes:

- Current Map ID
- Player starting position
- Player position coverage
- Nearby game-object Base IDs
- Whether objects are targetable
- Object names and positions
- Repeated object types
- Object movement
- Player target changes
- Observed cast action IDs

All runtime observations are scoped to the current map.

## Optional goal samples

Diagnostics > Objective research tools

- Mark finish / goal here
- Mark selected target

These are optional one-time research samples,
NOT manually recorded paths.

Samples persist in the existing plugin configuration.

They are not used for guidance until objectives
have been verified.

## Current map IDs

Territory 1165:

878 Gentlebean's Fever
879 Manderville-can Parade
880 The Gold Swiveller
881 Saucery Siege
882 Manderville Mountain
883 Pre-round Waiting Room

Territory 1197:

Blunderville Hub

## Existing functionality

- Automatic map detection
- Automatic route profile selection
- Optional recorded route guidance
- Read-only vnavmesh suggestions
- Splatoon visual overlays
- Map-specific cast observation
- Manual test hazard

## DEV6.1 test

1. Enter a Fall Guys course.
2. Open /bnav > Diagnostics.
3. Expand Objective research tools.
4. Move through the map normally.
5. Optionally mark the actual finish or objective.
6. Near the end, select Capture + copy objective report.
7. Share the report for objective analysis.

For Saucery Siege, samples of crystal pickup
and delivery locations are especially valuable.

## Safety

No automatic character movement.
No guessed objective route.
No inferred AoE shapes.

DEV6.2 will use verified destinations
to test automatically calculated visual paths.