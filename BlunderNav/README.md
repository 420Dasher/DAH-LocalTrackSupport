# BlunderNav v0.0.7 DEV3.1 FIX1

Dalamud API 15 / .NET 10

## Regression recovery

Restores the previously tested DEV3 route controller.

The original controller successfully navigated a
33-checkpoint route in the Blunderville lobby.

Temporarily disables experimental curved-path playback.

Restores bounded 24-leg movement groups.

The occasional group-boundary pause is expected.

## Retained functionality

- Persistent lobby and duty routes
- Manual and automatic checkpoint recording
- Continuous playback within each route group
- Manual HOLD checkpoints
- Auto-advance option
- Bounded automatic recovery
- Emergency stop
- Territory-change protection

## Important

Existing route recordings are not modified.

Experimental smoothing configuration fields may
remain in saved settings but are not used by this build.

Smoothing will be reintroduced separately after
movement reliability has been reverified.

## Test

Run the existing 33-checkpoint lobby route.

Verify the character follows the entire path.

Verify the route no longer rapidly skips checkpoints.

A small pause at the 24-leg group boundary is expected.