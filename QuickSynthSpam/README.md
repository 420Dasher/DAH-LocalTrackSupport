# QuickSynthSpam

Status: EXPERIMENTAL / TESTING
Version: 0.0.12.1 DEV12.1
Target: Dalamud API 15 / .NET 10

Automates repeated Quick Synthesis of the selected crafting recipe.

## Features

- Quick Synthesis batching beyond the game's 99-craft limit
- Preserves the total crafting target across batches
- Handles remainder batches automatically
- Checks available crafting materials
- Auto-fill maximum craftable or enter a manual amount
- Optional NQ-only Quick Synthesis
- Automatic opening with the Crafting Log
- Start, Stop, overall progress and batch progress
- Configurable automatic self-repair
- Repair threshold from 5% to 100% in increments of 5%
- Default repair threshold: 50%
- Equipped-gear durability readings up to 199%
- Repair using dark matter before crafting or between batches
- Automatic recipe restoration after repair
- Stops with an explanatory status when repair cannot complete
- /qspam command

## Repair behavior

Auto-repair is optional and disabled by default for new installations.

The enable/disable preference is captured when a crafting run starts.

The repair percentage can be changed while crafting.
Changes take effect when the next batch boundary is reached.

An active Quick Synthesis batch is never interrupted for maintenance.

Once a repair begins, the threshold used to trigger it remains
fixed until that repair is complete.

Materia extraction is not implemented.

## Testing

Verified in game:
- Quick Synthesis batching beyond 99 crafts
- Initial self-repair before crafting
- Automatic self-repair between completed batches
- Recipe restoration after repair
- Durability monitoring above 100%

This is an experimental plugin.
Do not rely on it for unattended crafting.