# BlunderNav DEV3

Version: 0.0.5.0
Dalamud API 15 / .NET 10

## Commands

/bnav
/bnav stop

## DEV3 features

- Persistent checkpoint recording
- Lobby and duty route separation
- Smooth multi-checkpoint path playback
- Precalculated route legs
- Stitched vnavmesh movement paths
- Manual HOLD checkpoints
- Per-checkpoint manual mode
- Automatic recovery (maximum two attempts)
- Unexpected displacement detection
- Pathfinding and movement timeouts
- Territory-change safety stop
- Emergency navigation stop

## Route playback

Auto-advance OFF:
Each checkpoint pauses for manual continuation.

Auto-advance ON:
Ordinary checkpoints form a continuous movement path.
The route stops at HOLD checkpoints or the final destination.

Long routes are divided into bounded groups of up to 24 legs.
Very large paths require shorter groups or HOLD checkpoints.

## Current limitations

- Dynamic hazards are not predicted
- No automated jumping or obstacle timing
- Course classification is preliminary
- Navigation must be tested on safe ground first

## DEV3 test

1. Enter Blunderville lobby.
2. Record 5-8 checkpoints with several turns.
3. Enable Auto-advance.
4. Return to the first checkpoint.
5. Start recorded route.
6. Check whether ordinary checkpoints are crossed smoothly.
7. Test a marked HOLD checkpoint.
8. Test manual Pause and Resume.
9. Test emergency stop.

Routes remain stored in Dalamud configuration.