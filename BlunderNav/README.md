# BlunderNav v0.0.3 DEV2

Dalamud API 15 / .NET 10
Status: Experimental

## Commands

/bnav - Open the window
/bnav stop - Emergency navigation stop

## DEV2

- Persistent saved route profiles
- Manual checkpoint recording
- Automatic distance-based recording
- Route profiles for five Fall Guys course layouts
- Short-distance vnavmesh movement testing
- Sequential checkpoint playback
- Optional automatic checkpoint advancement
- Hold points for manual obstacle timing
- Stuck, interrupted-path and timeout detection
- Pause, resume, retry and stop controls
- Existing cast observations retained

## Important limitations

Course identification is preliminary.
Only the final-stage coordinate region is specifically recognized.

Route profiles for the other courses must be selected manually.

Dynamic hazards, knockbacks, jumps, portals, crystals
and mechanic timing are NOT automated in DEV2.

Recorded routes are stored in Dalamud plugin configuration,
not in the Git repository.

## First test

Enter a Fall Guys course and open /bnav.

Select Unclassified / Test Route.

Record four or five checkpoints on clear ground.

Return to the first checkpoint.

Leave Auto-advance disabled for the first test.

Start recorded route.

Verify each checkpoint is reached and paused correctly.

Then enable Auto-advance and test the same safe route.

Finally, test Stop and recovery from interrupted movement.