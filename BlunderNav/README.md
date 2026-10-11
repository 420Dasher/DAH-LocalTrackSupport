# BlunderNav DEV6.3

Version: 0.0.16.0
Dalamud API 15 / .NET 10
Testing branch only

## Live hazard research

The first experimental hazard model supports
Manderville Mountain, Map ID 882.

BlunderNav observes new in-game casts of
action 34812 from the known obstacle emitters.

It displays square forecast outlines through
the existing Splatoon bridge.

Colors:

Blue: early predicted activation.
Yellow: approaching predicted activation.
Red: simple projected player overlap near
     the predicted activation time.

The red state is NOT proof of an actual hit.
The yellow state is NOT proof of a safe route.

Current modeled square half-width: 3 yalms.
Current forecast timing:
- first square approximately 1 second
  after the cast is observed
- subsequent squares every 0.5 seconds

The live cast observer polls every 300 ms,
so timing can lag real game events.

Exact hit windows and collision predictions
must be verified in-game before any routing
system uses these forecasts.

## Scope

- No character movement
- No automatic hazard avoidance
- No fixed finish goal for Mountain yet
- Existing DEV6.2 FIX1 path previews retained
- Existing research and diagnostics retained
- Other four repository plugins untouched

## Test instructions

Enter Manderville Mountain and proceed
toward the obstacle section at Z ~190.

Open /bnav.

Under Guide, confirm:
- Experimental hazard forecasts enabled
- New 34812 casts recognized
- Squares appear near relevant lanes
- Color changes as the event approaches

Compare actual danger against yellow/red
warnings and note any obvious mismatch.

Use Diagnostics > Copy hazard timing report
to share cast anchors and forecast counts.

## Research reference

vFallguy's public Map3 and MainWindow source
provided the initial mechanical model:

https://github.com/awgil/ffxiv_vfallguy

This is an independent BlunderNav implementation.
It uses read-only Dalamud cast observation and
BlunderNav's Splatoon visual lines, not vFallguy's
hooking, navigation, or drawing code.

## Known limitations

Only the 34812 Mountain square sequence is
modeled in this build.

34803 has been observed, but its geometry
has not yet been verified.

Some 34812 timings are sourced from
action-effect events in vFallguy, whereas
BlunderNav currently observes cast starts.

The two are not assumed to be equivalent.

Revised timing and fuller collision models
are planned only after in-game confirmation.