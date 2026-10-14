# AethernetAssist v0.0.19 DEV4.1 RESEARCH 1

Dalamud API 15 / .NET 10

Development build for the testing repository.

Features:
- Contextual Aethernet window
- Automatic proximity opening
- Learned and unlearned destinations
- Unlearned destinations disabled
- Vanilla-style ordering for major city networks
- One-click native Aethernet interaction
- Configurable range and visibility
- Diagnostic mode

Commands:
- /anet
- /anet debug
- /anet cancel

DEV1 is intended for in-game testing before additional
housing, world visiting, and map-click features are added.

The first automation implementation targets the English
game client and validates the selected destination against
the live Aethernet menu before sending its callback.
DEV2 additions:
- All physical Aethernet destinations from the game sheet.
- Invisible Aethernet destinations, including gates and airship landings.
- Territory-based city grouping and an Other section.
- Disabled destinations when attunement or quest requirements are unmet.
- Existing native teleport callback and diagnostics preserved.