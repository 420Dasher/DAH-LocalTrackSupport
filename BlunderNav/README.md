# BlunderNav DEV5 FIX1

Version: 0.0.11.0
Dalamud API 15 / .NET 10

## Actual map identification

Map identification does not use recorded routes.

Evidence:
- Territory type
- Current Map ID
- Player position
- Distinctive non-player object Data IDs

Built-in:
- Blunderville lobby: territory 1197
- Manderville Mountain: published Stage 3 coordinate region

Other course signatures must initially be collected in-game.

## Calibration

1. Enter a Fall Guys course.
2. Open /bnav.
3. Expand Map identification calibration.
4. Choose the actual course name.
5. Click Teach current map.
6. Capture additional samples in different areas.
7. Repeat for other courses.

Learned signatures persist in the existing Dalamud config.

## Route integration

Once a map is detected, its matching route profile
can be selected automatically.

Unidentified maps do not trigger route switching.

Manual route selection disables auto-loading, but map
identification itself continues.

## Safety

No movement automation.

No guessed obstacle AoEs.

The existing Splatoon visual guidance is retained.