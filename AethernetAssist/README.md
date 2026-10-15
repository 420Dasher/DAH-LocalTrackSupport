# AethernetAssist v0.2.0

Dalamud API 15 / .NET 10

A contextual FFXIV travel helper for Aethernet,
housing, residential, Cosmic Exploration and
same-data-center World Visit.

## Features

### City Aethernet
- Nearby Aethernet detection
- Destination and attunement checks
- Native city Aethernet travel
- Context-sensitive navigation window

### Housing and Residential Aethernet
- Mist, Lavender Beds, Goblet, Shirogane,
  and Empyreum
- Main and subdivision residential Aethernet
- Ward selection and supported city access
- Supported housing entrance navigation

### Cosmic Exploration
- Cosmic Exploration planet travel
- Local Cosmic Aethernet destinations
- Four supported Cosmic Exploration planets
- Guarded native destination selection

### Same-DC World Visit
- Available from the main Aetherytes in
  Limsa Lominsa, New Gridania and Ul'dah
- Shows worlds on the current data center
- Automatically opens the native World Visit menu
- Validates and selects the requested destination
- Confirms only a verified destination-specific prompt
- Monitors World Visit progress without retries
- Detects the new current world after arrival

## Commands

- /anet - Open the travel window
- /anet housing - Open the Housing page
- /anet settings - Open settings
- /anet debug - Toggle diagnostics
- /anet cancel - Stop supported active automation
- /anet sinus - Cosmic travel to Sinus Ardorum
- /anet phaenna - Cosmic travel to Phaenna
- /anet oizys - Cosmic travel to Oizys
- /anet auxesia - Cosmic travel to Auxesia

## Important limits

- World Visit supports the same data center only.
- Cross-data-center travel is not supported.
- World Visit queues belong to FFXIV.
- A queued World Visit cannot be cancelled by
  AethernetAssist.
- No repeated World Visit requests are issued after
  native confirmation.
- Unknown or localized confirmation prompts require
  manual player confirmation.

## Stable release - v0.2.0

First feature-complete stable milestone.

Includes:
- City and residential Aethernet travel
- Housing ward travel and supported gate navigation
- Cosmic Exploration planet and local Aethernet travel
- Same-data-center World Visit
- Remembered navigation tabs
- Context-sensitive auto-open and auto-close
- Guarded native destination selection and retries

The native travel implementation is carried forward
from the verified v0.1.7 testing release.

## Planned features

- Regular overworld Aetheryte travel
- Map marker integration
- Automatic multi-stage Aethernet routing

These features are not part of v0.2.0.

Historical development notes are kept in
KNOWN_ISSUES.md and Git history.