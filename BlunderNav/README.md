# BlunderNav DEV5 FIX2

Version 0.0.12.0
Dalamud API 15 / .NET 10

## Verified Fall Guys Map IDs

Territory 1165:
- 878: Gentlebean's Fever
- 879: Manderville-can Parade
- 880: The Gold Swiveller
- 881: Saucery Siege
- 882: Manderville Mountain
- 883: Pre-round Waiting Room

Territory 1197:
- Blunderville Hub

These identifiers were collected during live testing.

Map identification works without recorded routes.

## Automatic route loading

When enabled, a recognized course selects the
corresponding saved route profile.

The waiting room never loads a course route.

Manual route selection disables automatic route
loading until it is re-enabled in the Guide tab.

## Interface

Guide:
- Detected map
- Automatic route setting
- Start, stop, rejoin
- Checkpoint progress
- Overlay visibility

Routes:
- Profile selection
- Checkpoint recording
- Recording spacing
- Route clearing
- Recorded checkpoint list

Diagnostics:
- Map and position details
- Capture and copy object/cast report
- Cast history
- Experimental manual test hazard

## Runtime behavior

Map changes clear stale cast observations and
stop active visual guidance.

The waiting room does not render a course route.

Recorded routes and configuration are preserved.

## Navigation

BlunderNav NEVER controls player movement.

Splatoon is responsible for route visualization.
vnavmesh is used for read-only path suggestions.

Real automatic Fall Guys AoE detection remains
future work and is not claimed by this build.