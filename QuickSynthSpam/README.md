# QuickSynthSpam

Status: EARLY PROTOTYPE / EXPERIMENTAL
Version: 0.0.1 DEV1
Target: Dalamud API 15 / .NET 10

Automates repeated Quick Synthesis of the selected crafting recipe.

Features:
- Automatic UI opening with the Crafting Log
- Configurable total craft count
- Batches of up to 99
- Remainder handling
- Craftable quantity checks
- Progress display
- Stop button
- /qspam command

This is an unverified first development build.
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