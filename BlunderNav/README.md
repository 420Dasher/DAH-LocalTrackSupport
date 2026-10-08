# BlunderNav DEV3.1

Version: 0.0.6.0
Dalamud API 15 / .NET 10

## New in DEV3.1

- Experimental curve smoothing in Blunderville lobby
- Adjustable corner radius, 0.25 to 1.0 yalms
- Per-curve reachable-navmesh sampling
- Original waypoint fallback for rejected curves
- Last-run smoothing diagnostics
- Continuous groups up to 64 checkpoint legs
- Original recorded routes preserved

## Existing functionality

- Route recording
- Lobby / duty route separation
- Continuous playback
- Manual HOLD checkpoints
- Bounded automatic recovery
- Emergency stop with /bnav stop
- Movement and pathfinding timeouts

## Test procedure

1. Enter Blunderville lobby.
2. Open /bnav.
3. Select your recorded lobby route.
4. Enable Auto-advance checkpoints.
5. Leave Smooth corners OFF and test the baseline.
6. Return to the start.
7. Enable Smooth corners.
8. Set radius to 0.65 yalms.
9. Run the same route again.
10. Compare turn smoothness and endpoint accuracy.

Try radius 0.85 only after successful clear-ground testing.

## Limitations

Smoothing only applies in lobby territory 1197.

Point-on-mesh sampling does not guarantee full collision clearance.
Do not use smoothing near edges or narrow platforms yet.

Dynamic hazards and jump mechanics are not automated.

Recorded routes are stored in Dalamud configuration.