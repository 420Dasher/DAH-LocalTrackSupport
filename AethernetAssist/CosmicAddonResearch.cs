using System;
using System.Collections.Generic;
using System.Text;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // Temporary Cosmic Exploration research.
    // No callbacks, native mutations or automatic actions.

    private bool cosmicCaptureActive;
    private DateTime cosmicCaptureStarted;

    private readonly HashSet<string> cosmicCaptureSeen =
        new(StringComparer.Ordinal);

    private readonly List<string> cosmicCaptureTimeline = new();

    private void StartCosmicUiCapture()
    {
        StopCosmicUiCapture();

        cosmicCaptureSeen.Clear();
        cosmicCaptureTimeline.Clear();
        cosmicCaptureStarted = DateTime.UtcNow;

        cosmicCaptureActive = true;

        AddonLifecycle.RegisterListener(
            AddonEvent.PostDraw,
            OnCosmicAddonDraw);
    }

    private void StopCosmicUiCapture()
    {
        if (!cosmicCaptureActive)
            return;

        cosmicCaptureActive = false;

        AddonLifecycle.UnregisterListener(
            AddonEvent.PostDraw,
            OnCosmicAddonDraw);
    }

    private void OnCosmicAddonDraw(
        AddonEvent type,
        AddonArgs args)
    {
        if (!cosmicCaptureActive ||
            Client.TerritoryType != 959 ||
            cosmicCaptureTimeline.Count >= 200)
            return;

        string name = args.AddonName;

        if (string.IsNullOrWhiteSpace(name) ||
            !cosmicCaptureSeen.Add(name))
            return;

        int values = -1;

        var addon = (AtkUnitBase*)args.Addon.Address;

        if (addon != null)
            values = addon->AtkValuesCount;

        cosmicCaptureTimeline.Add(
            DateTime.UtcNow.ToString("HH:mm:ss.fff") +
            " | " + name +
            " | AtkValues=" + values);
    }

    private string BuildCosmicAddonCaptureReport()
    {
        var output = new StringBuilder();

        output.AppendLine();
        output.AppendLine(
            "=== COSMIC NATIVE ADDON DISCOVERY ===");

        output.AppendLine(
            "Capture active: " + cosmicCaptureActive);

        output.AppendLine(
            "Capture started UTC: " +
            (cosmicCaptureStarted == default
                ? "Not started"
                : cosmicCaptureStarted.ToString("O")));

        output.AppendLine(
            "Unique visible addons: " +
            cosmicCaptureTimeline.Count);

        output.AppendLine(
            "Entries are ordered by first appearance.");

        foreach (string item in cosmicCaptureTimeline)
            output.AppendLine(item);

        output.AppendLine(
            "Read-only lifecycle observation.");
        return output.ToString();
    }
}