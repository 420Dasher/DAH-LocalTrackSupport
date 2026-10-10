# QuickSynthSpam

Version: 0.1.0
Status: Stable
Framework: Dalamud API 15 / .NET 10

QuickSynthSpam automates consecutive Quick Synthesis batches
beyond the game's normal 99-craft batch limit.

## Features

- Quick Synthesis chaining in batches of up to 99
- Automatic remainder handling
- Overall and per-batch progress displays
- Material availability checks
- Recipe Quick Synthesis eligibility checks
- Automatic maximum craftable quantity
- Persistent manual crafting target
- Optional NQ-only crafting
- Optional automatic opening with the Crafting Log
- Start and Stop controls
- Context-aware synthesis and repair status indicators
- Configurable automatic self-repair
- Adjustable repair threshold from 5% to 100%
- Equipped-gear durability tracking up to 199%
- Automatic recipe restoration after repair
- /qspam chat command

## Usage

1. Open the Crafting Log.
2. Select an eligible Quick Synthesis recipe.
3. Set the crafting target or use the maximum craftable.
4. Configure NQ-only and self-repair as desired.
5. Click Start Quick Synthesis.

Commands:
- /qspam
- /qspam 250

The plugin runs consecutive batches until the crafting target
is reached or a stop condition occurs.

## Automatic self-repair

Self-repair is optional and disabled by default.

The default repair threshold is 50%, configurable from
5% to 100% in increments of 5%.

Equipped gear is checked before crafting and between
completed synthesis batches.

The current synthesis batch is never interrupted for repair.

If repair is required, the plugin attempts to:
1. Exit crafting stance.
2. Open the native Repair interface.
3. Select equipped items.
4. Repair using dark matter.
5. Verify repaired gear condition.
6. Reopen the original crafting recipe.
7. Continue the remaining crafting target.

The Auto-repair toggle is captured when the run starts.

Threshold changes made during a run apply at the next
batch boundary. Once repair starts, its threshold is fixed
until that repair completes.

Materia extraction is not implemented.

## State detection

The UI distinguishes:
- Crafting Log connected
- Crafting Log closed
- Starting Quick Synthesis
- Quick Synthesis running
- Returning to Crafting Log
- Automatic self-repair in progress

## Verified in-game

- Complete 1,360-craft run
- Batching beyond 99 crafts
- Remainder batch completion
- Automatic self-repair before crafting
- Automatic self-repair between batches
- Recipe restoration after self-repair
- Durability readings above 100%
- Persistent repair threshold
- Correct synthesis-state detection in DEV12.2

## Limitations

QuickSynthSpam is a community plugin. Game updates may require compatibility fixes.

Do not rely on it for unattended crafting.

The plugin does not resume an interrupted run automatically.
Progress is not persisted across plugin reloads.

Normal Quick Synthesis eligibility, materials, suitable
dark matter and repair capability are still required.

## Release channel

The stable plugin is distributed from the main branch.
Experimental versions may be published to testing.