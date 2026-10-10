# BlunderNav v0.0.9 DEV4.1

Dalamud API 15 / .NET 10

## Visual navigation only

BlunderNav NEVER moves the character automatically.

Splatoon displays recorded routes, waypoint markers,
suggested paths and manually placed test hazard circles.

vnavmesh calculates suggestions without invoking movement.

## DEV4.1 changes

- Start visual guidance from anywhere along a recorded route
- Select the next checkpoint from the nearest route segment
- Rejoin the recorded route from your current position
- Display the upcoming six recorded route segments by default
- Continue automatically advancing checkpoints as you walk
- Preserve the existing recorded-route configuration

## Controls

/bnav

/bnav stop

The stop command stops and hides visual guidance.
It does not affect character movement.

## Testing

1. Enter the Blunderville lobby with Splatoon enabled.
2. Select the existing recorded route.
3. Stand near the middle of the route, not checkpoint one.
4. Click Start visual guidance.
5. Verify the highlighted checkpoint is nearby and ahead.
6. Walk toward it.
7. Verify subsequent checkpoints advance automatically.
8. Click Rejoin from here at another section of the route.
9. Verify the suggested section changes accordingly.

## Limitations

Near overlapping or crossing route sections, the nearest
geometric segment may not be the intended segment.

Rejoin from here provides manual correction.

Recorded paths do not yet automatically avoid live
Fall Guys obstacle mechanics.

Manual test hazard circles are not actual AoE detection.

## DEV5 direction

- Automatically detect Fall Guys courses / stages
- Automatically choose the matching route profile
- Identify real obstacle danger zones
- Recalculate recommended paths around detected hazards

Multiple Fall Guys stages share territory 1165.
Stage identification must therefore use additional
course-specific evidence, with manual override.