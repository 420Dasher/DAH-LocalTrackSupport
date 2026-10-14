# AethernetAssist v0.0.25 DEV4.2 RESEARCH1

Dalamud API 15 / .NET 10
Development branch: testing

## Features

- Contextual Aethernet travel interface
- Native city Aethernet destinations and attunement checks
- Main and subdivision residential Aethernet destinations
- Residential support in all five housing districts
- Ward selection (1-30) from city and residential entrances
- Automatic Mist and Goblet gate navigation via vnavmesh
- Native NPC travel for Lavender Beds, Shirogane and Empyreum
- Configurable proximity auto-open and auto-close
- Manually opened windows retain their visibility
- City-style destination rows and scrolling
- Manual movement interruption and travel cancellation
- Operational diagnostics in Settings

## Commands

- /anet - Open navigation window
- /anet housing - Open Housing page
- /anet settings - Open settings
- /anet debug - Toggle diagnostics
- /anet cancel - Cancel active navigation

## Development status

DEV4.1 residential networks were tested in:
Mist, Lavender Beds, Goblet, Shirogane and Empyreum.

Residential research modules were removed in v0.0.24.
Operational transport diagnostics remain available.

Cosmic Exploration is planned for DEV4.2.
World Travel is not yet implemented.
## DEV4.2 Cosmic Exploration

Planned command destinations:
- /anet sinus
- /anet phaenna
- /anet oizys
- /anet auxesia

DEV4.2 RESEARCH1 only captures NPC and native-menu data.
No Cosmic Exploration travel command is active yet.

Drivingway reference: Mare Lamentorum map X21.9 Y13.2 Z-1.4.
The raw world XYZ position must be obtained from the object table.