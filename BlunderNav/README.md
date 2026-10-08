# BlunderNav DEV4 - Visual Navigation

Version: 0.0.8.0
Dalamud API 15 / .NET 10

## Design

BlunderNav is now a visual-only navigation assistant.

The plugin NEVER invokes vnavmesh movement or stop APIs.

## Integrations

Splatoon:
- Recorded route lines
- Highlighted suggested path
- Next checkpoint marker
- Manual hazard circle

vnavmesh:
- Read-only pathfinding
- Experimental pathfinding around one manually
  marked hazard circle

## Existing routes

Routes are preserved through the existing
RouteConfiguration and RecordedRoute structures.

Lobby territory: 1197
Duty territory: 1165

## Controls

/bnav
/bnav stop

The stop command stops VISUAL guidance.
It does not control character movement.

## DEV4 testing

1. Enter the Blunderville lobby.
2. Ensure Splatoon is enabled.
3. Open /bnav.
4. Choose an existing recorded route.
5. Enable the in-world overlay.
6. Check the cyan recorded route.
7. Return to checkpoint one.
8. Start visual guidance.
9. Walk manually and check the green suggestion.
10. Optionally mark a test hazard.

## Limitations

Real Fall Guys AoE detection is not implemented yet.

Manual test hazards are illustrative markers, not
automatically recognized game mechanics.

The point avoidance API is not a guarantee of safety.
Candidate paths intersecting the manual hazard are
rejected rather than shown as safe.

Splatoon integration uses dynamic elements.
If Splatoon is unavailable, routes are still saved
and visible as coordinates in the BlunderNav UI.