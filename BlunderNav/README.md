# BlunderNav DEV6.2

Version 0.0.14.0
Dalamud API 15 / .NET 10

## Experimental calculated route previews

DEV6.2 adds a separate read-only long-range
vnavmesh path calculation pipeline.

Supported objective destinations:

Map 878 - Gentlebean's Fever
Post-finish reference:
-198.32, 13.59, -364.44

Map 879 - Manderville-can Parade
Post-finish reference:
-3.38, 18.94, -379.94

Map 881 - Saucery Siege
Manual test targets:

Pickup:
-200.30, 5.46, 252.90

Blue:
-200.26, 6.00, 204.02

Red:
-241.04, 6.00, 274.59

Yellow:
-158.90, 6.00, 273.09

Saucery Siege does NOT detect carried
crystal color automatically in DEV6.2.

Map 880 - The Gold Swiveller
Survival mode; no fixed-route preview.

Map 882 - Manderville Mountain
Two qualifying finish gates, exact
positions not yet verified.
No guessed target is used.

## How it works

The Guide tab automatically requests a
calculated path on supported maps.

The pathfinder is read-only.

Results undergo basic validation:
- finite coordinates
- reasonable length
- expected endpoint
- bounded number of nodes
- stale-origin rejection
- calculation timeout

Splatoon draws only the nearby portion
of a returned route.

Paths may cross active Fall Guys hazards.
A calculated route is not a safe route.

No movement commands are sent.

## Existing features retained

- Automatic Map ID detection
- Waiting room detection
- Optional saved route profiles
- Recorded checkpoint guide
- DEV6.1 objective observer
- Cast diagnostics
- Experimental manual hazard marker

## Test priorities

1. Gentlebean's Fever:
   Does vnavmesh return a valid path?
   Does the green route follow the course?

2. Manderville-can Parade:
   Same tests, especially near obstacles.

3. Saucery Siege:
   Verify central pickup, then manually
   select Blue, Red or Yellow and test
   whether the target is reachable.

Do not assume pathfinding understands
moving obstacles or qualification triggers.