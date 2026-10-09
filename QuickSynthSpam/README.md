# QuickSynthSpam

Status: EARLY PROTOTYPE / EXPERIMENTAL
Version: 0.0.10.1 DEV10.1
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
- Equipped-gear durability monitoring up to 199% and spiritbond monitoring
- DEV10 opt-in automatic self-repair below 50% durability
- Repairs before the first batch or between completed batches
- Uses dark matter via the in-game self-repair interface
- Restores the selected recipe and resumes remaining crafts
- Stops on repair, confirmation or recipe-restoration failures
- Existing DEV9 preview preference does not enable live repair
- Materia extraction remains preview-only
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