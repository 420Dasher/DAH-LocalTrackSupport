# BlunderNav DEV1

Experimental Fall Guys course automation project.

Version: 0.0.1.0
Dalamud: API 15
Runtime: .NET 10

## DEV1 functionality

- /bnav opens the plugin
- /bnav stop stops vnavmesh movement
- Detects Fall Guys lobby / duty territory
- Displays player coordinates
- Reports vnavmesh readiness
- Observes nearby enemy cast IDs
- Allows short pathfinding previews
- Executes manually confirmed navigation tests
- Stops movement after timeout or stalls
- Stops own movement when leaving the course

## Limitations

DEV1 does not automatically complete any course.
Dynamic hazards are not yet handled.
Course identification outside the final stage is preliminary.

## Testing

1. Open /bnav.
2. Check vnavmesh connection.
3. Enter Blunderville.
4. Check detected territory and course.
5. Preview a short path on clear ground.
6. Execute and verify movement.
7. Test emergency stop.
8. Report position, course and vnavmesh results.