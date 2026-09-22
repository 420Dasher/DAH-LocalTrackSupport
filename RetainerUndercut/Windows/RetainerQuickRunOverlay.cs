using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RetainerUndercut.Services;

namespace RetainerUndercut.Windows;

/// <summary>
/// Compact run controls attached to the native retainer windows.
/// - RetainerSellList: current-retainer Dry Run / Live Run.
/// - RetainerList: all-enabled-retainers Dry Run / Live Run.
///
/// This remains an ImGui companion rather than a native-node injection so a game UI update cannot
/// leave persistent custom nodes behind or break the retainer addon layout.
/// </summary>
internal sealed class RetainerQuickRunOverlay
{
    private const float WindowWidth = 300f;

    private readonly IGameGui gameGui;
    private readonly RetainerListingScanner scanner;
    private readonly SingleListingMarketCheck marketCheck;
    private readonly AutoRetainerUndercutRunner autoRunner;
    private readonly MultiRetainerUndercutRunner multiRunner;
    private readonly IPluginLog log;
    private bool drawFailureLogged;
    private bool retainerListWasVisible;

    private enum QuickRunSurface
    {
        CurrentRetainer,
        AllRetainers,
    }

    public RetainerQuickRunOverlay(
        IGameGui gameGui,
        RetainerListingScanner scanner,
        SingleListingMarketCheck marketCheck,
        AutoRetainerUndercutRunner autoRunner,
        MultiRetainerUndercutRunner multiRunner,
        IPluginLog log)
    {
        this.gameGui = gameGui;
        this.scanner = scanner;
        this.marketCheck = marketCheck;
        this.autoRunner = autoRunner;
        this.multiRunner = multiRunner;
        this.log = log;
    }

    public void Draw()
    {
        try
        {
            DrawInternal();
        }
        catch (Exception ex)
        {
            // Keep an overlay failure from affecting the repricing engine itself, without spamming the log every frame.
            if (!drawFailureLogged)
            {
                drawFailureLogged = true;
                log.Error(ex, "Retainer Undercut quick-run overlay draw failed.");
            }
        }
    }

    private unsafe void DrawInternal()
    {
        // Prefer the sell list if a transition briefly leaves both addons visible.
        var sellListRef = gameGui.GetAddonByName("RetainerSellList");
        if (!sellListRef.IsNull && sellListRef.IsVisible)
        {
            retainerListWasVisible = false;
            DrawForAddon((AtkUnitBase*)sellListRef.Address, QuickRunSurface.CurrentRetainer);
            return;
        }

        var retainerListRef = gameGui.GetAddonByName("RetainerList");
        if (!retainerListRef.IsNull && retainerListRef.IsVisible)
        {
            // Refresh once when the bell list opens so the quick buttons immediately reflect the current retainers,
            // enabled state, and listing counts even if the main plugin window has never been opened this session.
            if (!retainerListWasVisible && !multiRunner.IsRunning && !autoRunner.IsRunning)
                multiRunner.RefreshRetainers();

            retainerListWasVisible = true;
            DrawForAddon((AtkUnitBase*)retainerListRef.Address, QuickRunSurface.AllRetainers);
            return;
        }

        retainerListWasVisible = false;
    }

    private unsafe void DrawForAddon(AtkUnitBase* addon, QuickRunSurface surface)
    {
        if (addon is null)
            return;

        var addonWidth = addon->GetScaledWidth(true);
        var addonHeight = addon->GetScaledHeight(true);
        if (addonWidth <= 0f || addonHeight <= 0f)
            return;

        var position = ResolvePosition(addon, addonWidth, addonHeight);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(WindowWidth, 0f), ImGuiCond.Always);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(9f, 7f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6f, 5f));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.055f, 0.065f, 0.075f, 0.96f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0.25f, 0.55f, 0.42f, 0.75f));

        const ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing;

        var open = true;
        if (ImGui.Begin("##RetainerUndercutQuickRun", ref open, flags))
            DrawContents(surface);

        ImGui.End();
        ImGui.PopStyleColor(2);
        ImGui.PopStyleVar(3);
    }

    private static unsafe Vector2 ResolvePosition(AtkUnitBase* addon, float addonWidth, float addonHeight)
    {
        _ = addonWidth;

        // FFXIV addon bounds include a little invisible/shadow padding below and to the left of
        // the visible frame. Placing at +2 therefore still looks detached. Match the proven
        // bottom-attachment offsets used by Allagan's generic overlay helper: inset 10 px from
        // the addon's X and overlap the reported bottom edge by 10 px. Visually this lands the
        // companion bar directly against the native window without covering its usable content.
        return new Vector2(
            addon->X + 10f,
            addon->Y + addonHeight - 10f);
    }

    private void DrawContents(QuickRunSurface surface)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.88f, 0.68f, 1f));
        ImGui.TextUnformatted(surface == QuickRunSurface.AllRetainers
            ? "Retainer Undercut — All retainers"
            : "Retainer Undercut — Current retainer");
        ImGui.PopStyleColor();

        if (multiRunner.IsRunning)
        {
            DrawStopButton(
                multiRunner.DryRunMode ? "Stop preview##quick_stop_all" : "EMERGENCY STOP##quick_stop_all",
                multiRunner.RequestStop);
            ImGui.SameLine();
            ImGui.TextDisabled($"{multiRunner.CurrentRetainerNumber}/{multiRunner.TotalRetainers} • {multiRunner.CurrentRetainerName}");
            return;
        }

        if (autoRunner.IsRunning)
        {
            DrawStopButton(
                autoRunner.DryRunMode ? "Stop preview##quick_stop_current" : "EMERGENCY STOP##quick_stop_current",
                autoRunner.RequestStop);
            ImGui.SameLine();
            ImGui.TextDisabled($"{autoRunner.CurrentNumber}/{autoRunner.TotalItems}");
            return;
        }

        if (surface == QuickRunSurface.AllRetainers)
        {
            DrawAllRetainerControls();
            return;
        }

        DrawCurrentRetainerControls();
    }

    private void DrawCurrentRetainerControls()
    {
        var canStart = CanStartCurrentRetainer();
        if (!canStart)
            ImGui.BeginDisabled();

        if (ImGui.Button("Dry Run##quick_preview_current", new Vector2(137f, 0f)))
            autoRunner.Start(dryRun: true);

        ImGui.SameLine();
        DrawLiveButton("Live Run##quick_live_current", new Vector2(137f, 0f), () => autoRunner.Start(dryRun: false));

        if (!canStart)
            ImGui.EndDisabled();

        ImGui.TextDisabled(canStart
            ? $"{scanner.ActiveRetainerName} • {scanner.Listings.Count} listing(s)"
            : CurrentBlockReason());
    }

    private void DrawAllRetainerControls()
    {
        var canStart = multiRunner.CanStartAll;
        if (!canStart)
            ImGui.BeginDisabled();

        if (ImGui.Button("Dry Run All##quick_preview_all", new Vector2(137f, 0f)))
            multiRunner.Start(dryRun: true);

        ImGui.SameLine();
        DrawLiveButton("Live Run All##quick_live_all", new Vector2(137f, 0f), () => multiRunner.Start(dryRun: false));

        if (!canStart)
            ImGui.EndDisabled();

        ImGui.TextDisabled(canStart
            ? $"{multiRunner.EnabledRetainerCount} enabled • {multiRunner.EnabledListingCount} listing(s)"
            : multiRunner.StartBlockReason);
    }

    private bool CanStartCurrentRetainer() =>
        !multiRunner.IsRunning &&
        !autoRunner.IsRunning &&
        !marketCheck.IsBusy &&
        scanner.SellListVisible &&
        scanner.MarketContainerLoaded &&
        scanner.UiOrderMappingReady &&
        scanner.Listings.Count > 0;

    private string CurrentBlockReason()
    {
        if (marketCheck.IsBusy)
            return "Waiting for the current market check to finish.";
        if (!scanner.MarketContainerLoaded)
            return "Waiting for retainer market data.";
        if (scanner.Listings.Count == 0)
            return "No active market listings detected.";
        if (!scanner.UiOrderMappingReady)
            return "Waiting for the sell-list rows to finish loading.";
        return "Current retainer is not ready yet.";
    }

    private static void DrawLiveButton(string label, Vector2 size, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.48f, 0.34f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.60f, 0.42f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.12f, 0.38f, 0.28f, 1f));
        if (ImGui.Button(label, size))
            onClick();
        ImGui.PopStyleColor(3);
    }

    private static void DrawStopButton(string label, Action onClick)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.58f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.72f, 0.22f, 0.22f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.46f, 0.13f, 0.13f, 1f));
        if (ImGui.Button(label))
            onClick();
        ImGui.PopStyleColor(3);
    }
}
