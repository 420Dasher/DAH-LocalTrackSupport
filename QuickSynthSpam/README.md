# QuickSynthSpam

Status: EARLY PROTOTYPE / EXPERIMENTAL
Version: 0.0.8 DEV8
Target: Dalamud API 15 / .NET 10

Automates repeated Quick Synthesis of the selected crafting recipe.

Features:
- Automatic UI opening with the Crafting Log
- Automatic UI hiding when the Crafting Log closes while idle
- Configurable total craft count
- Automatic maximum craftable default for selected recipe
- Manual Use max craftable button
- Persistent Manual mode when entering a custom quantity
- Automatic mode remains the default and can be re-enabled
- Batches of up to 99
- Remainder handling
- Craftable quantity checks
- Recipe eligibility, first-craft completion and Quick Synthesis button checks before every batch
- Overall and per-batch progress bars
- Clear run status and final progress
- Green Start and red Stop controls
- Stop button
- Saved Craft NQ items only checkbox, applied to every batch
- /qspam command

DEV2 core automation and DEV3 window behavior passed in-game testing. DEV4 refines the UI without changing synthesis callbacks.
Batch transitions and interruption handling require in-game tests.

The plugin does not automatically restart after interruption.
Do not rely on it for unattended crafting.

## Planned tests

1. Plugin loads without errors.
2. UI opens with RecipeNote.
3. Single batch of 5 succeeds.
4. Two batches (99 + 11) succeed.
5. Final quantity is not exceeded.
6. Material shortage stops the queue.
7. Stop button interrupts the run.
8. No duplicate batches are triggered.