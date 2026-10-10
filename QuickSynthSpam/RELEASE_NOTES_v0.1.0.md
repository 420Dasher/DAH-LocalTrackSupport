# QuickSynthSpam v0.1.0

Channel: Stable / main
Framework: Dalamud API 15 / .NET 10

## Initial stable release

- Quick Synthesis automation beyond 99 crafts
- Automatic batch chaining and remainder handling
- Total target and per-batch progress tracking
- Optional NQ-only synthesis
- Maximum craftable quantity detection
- Configurable automatic self-repair
- Repair threshold from 5% to 100%
- Equipped-gear durability monitoring up to 199%
- Recipe restoration following repair
- Context-aware crafting and repair indicators

## Testing completed

- Full 1,360-craft endurance run
- Multiple 99-craft batches
- Final remainder batch
- Automatic repairs before crafting
- Automatic repairs between batches
- Crafting continuation after repair
- Repair threshold persistence
- v0.1.0 RC1 smoke test

## Stable promotion

Source promoted from the tested RC1.

Only the in-plugin release wording was changed.
Crafting and self-repair logic remains unchanged.

The stable package and manifest are served from main.

## Important

Crafting progress is not persisted after a plugin reload.
The plugin does not automatically resume interrupted runs.
Do not rely on it for unattended crafting.