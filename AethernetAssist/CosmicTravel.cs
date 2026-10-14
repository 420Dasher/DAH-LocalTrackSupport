using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    // DEV4.2 TEST1:
    // First live Cosmic travel test, starting near Drivingway.
    //
    // Teleport to Bestways Burrow and vnavmesh navigation
    // are NOT enabled in this build.

    private static readonly string[] CosmicNames =
    {
        "Sinus Ardorum",
        "Phaenna",
        "Oizys",
        "Auxesia"
    };

    private static readonly uint[] CosmicTerritories =
    {
        1237,
        1291,
        1310,
        1319
    };

    private enum CosmicPhase
    {
        Idle,
        OpeningDrivingway,
        SelectingPlanet,
        WaitingForArrival
    }

    private CosmicPhase cosmicPhase = CosmicPhase.Idle;

    private int cosmicTargetIndex = -1;
    private DateTime cosmicStarted;
    private DateTime cosmicLastAction;
    private DateTime cosmicMenuChosenAt;

    private string cosmicStatus =
        "No Cosmic Exploration travel requested.";

    private string cosmicNativeDetails =
        "No Cosmic selector interaction attempted.";

    private bool cosmicMenuChosen;
    private bool cosmicConfirmationSent;
    private DateTime cosmicConfirmationAt;

    private string cosmicPreviousTitle = "";
    private int cosmicExpectedNextIndex = -1;
    private int cosmicSelectionAttempts;

    private void CosmicMessage(string text)
    {
        cosmicStatus = text;
        Log.Information("Cosmic travel: " + text);
        Chat.Print("[AethernetAssist] " + text);
    }

    private void StopCosmicTravel(string reason)
    {
        cosmicPhase = CosmicPhase.Idle;
        cosmicTargetIndex = -1;
        cosmicMenuChosen = false;
        cosmicConfirmationSent = false;
        cosmicConfirmationAt = DateTime.MinValue;
        cosmicPreviousTitle = "";
        cosmicExpectedNextIndex = -1;
        cosmicSelectionAttempts = 0;

        CosmicMessage(reason);
    }

    private IGameObject? FindDrivingway()
    {
        var player = Objects.LocalPlayer;

        if (player == null)
            return null;

        return Objects
            .Where(obj =>
                obj != null &&
                obj.BaseId == 1052581 &&
                obj.IsTargetable &&
                obj.ObjectKind ==
                    Dalamud.Game.ClientState.Objects.Enums
                        .ObjectKind.EventNpc)
            .OrderBy(obj =>
                Vector3.DistanceSquared(
                    player.Position, obj.Position))
            .FirstOrDefault();
    }

    private void StartCosmicTravel(int requestedIndex)
    {
        if (requestedIndex < 0 ||
            requestedIndex >= CosmicNames.Length)
            return;

        if (cosmicPhase != CosmicPhase.Idle ||
            phase != TravelPhase.Idle ||
            housingPhase != HousingPhase.Idle ||
            residentialPhase != ResidentialPhase.Idle)
        {
            CosmicMessage(
                "Another navigation operation is already active.");
            return;
        }

        if (Client.TerritoryType ==
            CosmicTerritories[requestedIndex])
        {
            CosmicMessage(
                "Already in " + CosmicNames[requestedIndex] + ".");
            return;
        }

        if (Client.TerritoryType != 959)
        {
            CosmicMessage(
                "TEST1 requires standing near Drivingway " +
                "in Mare Lamentorum.");
            return;
        }

        if (IsBusy())
        {
            CosmicMessage(
                "Cannot start during a loading transition.");
            return;
        }

        var player = Objects.LocalPlayer;
        var npc = FindDrivingway();

        if (player == null || npc == null ||
            Vector3.Distance(player.Position, npc.Position) > 5f)
        {
            CosmicMessage(
                "Approach Drivingway before starting TEST1.");
            return;
        }

        if (GetAddon("SelectYesno") != null)
        {
            CosmicMessage("Close the existing confirmation first.");
            return;
        }

        cosmicTargetIndex = requestedIndex;
        cosmicStarted = DateTime.UtcNow;
        cosmicLastAction = DateTime.MinValue;
        cosmicMenuChosenAt = DateTime.MinValue;
        cosmicMenuChosen = false;
        cosmicConfirmationSent = false;
        cosmicConfirmationAt = DateTime.MinValue;
        cosmicPreviousTitle = "";
        cosmicExpectedNextIndex = -1;
        cosmicSelectionAttempts = 0;

        cosmicNativeDetails =
            "Requested " + CosmicNames[requestedIndex];

        cosmicPhase = CosmicPhase.OpeningDrivingway;

        CosmicMessage(
            "Starting travel to " +
            CosmicNames[requestedIndex] + "...");
    }

    private bool TryReadCosmicTitle(
        AtkUnitBase* addon,
        out string title)
    {
        title = "";

        if (addon == null ||
            addon->AtkValues == null ||
            addon->AtkValuesCount != 24)
            return false;

        // Verify the four names reported by the
        // English client before any native input.
        for (int i = 0; i < CosmicNames.Length; i++)
        {
            var value = addon->AtkValues[16 + i];

            if (value.Type != AtkValueType.String &&
                value.Type != AtkValueType.ConstString &&
                value.Type != AtkValueType.ManagedString)
                return false;

            if (!string.Equals(
                Clean(value.String.ToString()),
                CosmicNames[i],
                StringComparison.OrdinalIgnoreCase))
                return false;
        }

        var nodes = addon->UldManager.NodeList;

        if (nodes == null)
            return false;

        int count = Math.Min(
            (int)addon->UldManager.NodeListCount, 80);

        for (int i = 0; i < count; i++)
        {
            var node = nodes[i];

            if (node == null ||
                node->NodeId != 17 ||
                node->Type != NodeType.Text)
                continue;

            title = Clean(
                ((AtkTextNode*)node)->NodeText.ToString());

            // An out parameter cannot be captured by a lambda.
            // Use a normal local variable for the comparison.
            string selectedTitle = title;

            return Array.Exists(
                CosmicNames,
                n => string.Equals(
                    n, selectedTitle,
                    StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static void CosmicButtonClick(
        AtkUnitBase* addon,
        int eventParam)
    {
        // Native ButtonClick pattern observed in another
        // open-source Cosmic Exploration implementation.
        // TEST1 validates the resulting visible title
        // before allowing the next action.
        var nativeEvent = default(AtkEvent);

        nativeEvent.State.EventType =
            AtkEventType.ButtonClick;

        nativeEvent.Param = (uint)eventParam;

        addon->ReceiveEvent(
            AtkEventType.ButtonClick,
            eventParam,
            &nativeEvent,
            null);
    }

    private void TickCosmicTravel()
    {
        if (cosmicPhase == CosmicPhase.Idle)
            return;

        var now = DateTime.UtcNow;

        if (cosmicTargetIndex < 0 ||
            cosmicTargetIndex >= CosmicNames.Length)
        {
            StopCosmicTravel("Invalid Cosmic destination.");
            return;
        }

        string targetName = CosmicNames[cosmicTargetIndex];
        uint targetTerritory =
            CosmicTerritories[cosmicTargetIndex];

        if (cosmicPhase == CosmicPhase.WaitingForArrival)
        {
            if (Client.TerritoryType == targetTerritory &&
                !IsBusy() &&
                Objects.LocalPlayer != null)
            {
                StopCosmicTravel(
                    "Arrived: " + targetName);
                return;
            }

            var confirmation = (AddonSelectYesno*)
                GetAddon("SelectYesno");

            if (confirmation != null)
            {
                // Do not submit the same Yes callback twice,
                // even if the addon remains visible briefly.
                if (cosmicConfirmationSent)
                {
                    if ((now - cosmicConfirmationAt)
                        .TotalSeconds > 9)
                    {
                        StopCosmicTravel(
                            "Cosmic confirmation remained open " +
                            "after submitting Yes.");
                    }

                    return;
                }

                if (confirmation->PromptText == null ||
                    confirmation->AtkValues == null ||
                    confirmation->AtkValuesCount < 2 ||
                    confirmation->YesButton == null)
                {
                    StopCosmicTravel(
                        "Native confirmation data unavailable.");
                    return;
                }

                string prompt = Clean(
                    confirmation->PromptText
                        ->NodeText.ToString());

                string firstButton = Clean(
                    confirmation->AtkValues[1]
                        .GetValueAsString());

                cosmicNativeDetails =
                    "Confirmation prompt: " + prompt +
                    " | First button: " + firstButton;

                // Never accept a generic or unrelated
                // confirmation just because Yes/No is open.
                if (!string.Equals(
                    prompt,
                    "Travel to the selected destination?",
                    StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                    firstButton,
                    "Yes",
                    StringComparison.OrdinalIgnoreCase))
                {
                    StopCosmicTravel(
                        "Unexpected confirmation. " +
                        "No automatic response submitted.");
                    return;
                }

                // Revalidate the requested planet immediately
                // before confirming the actual transfer.
                var planet = GetAddon("WKSPlanetSelect");

                if (planet == null ||
                    !TryReadCosmicTitle(
                        planet,
                        out string selectedName) ||
                    !string.Equals(
                        selectedName,
                        targetName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    StopCosmicTravel(
                        "Planet validation failed at confirmation.");
                    return;
                }

                // Native SelectYesno:
                // callback 0 = first (Yes) button.
                confirmation->AtkUnitBase.FireCallbackInt(0);

                cosmicConfirmationSent = true;
                cosmicConfirmationAt = now;
                cosmicLastAction = now;

                cosmicNativeDetails =
                    "Confirmed " + targetName +
                    " | Prompt validated | Yes callback=0";

                CosmicMessage(
                    "Confirmed travel to " +
                    targetName + ". Waiting for arrival.");

                return;
            }

            if (!cosmicConfirmationSent &&
                (now - cosmicLastAction).TotalSeconds > 12 &&
                !IsBusy())
            {
                StopCosmicTravel(
                    "Blast Off confirmation did not appear.");
                return;
            }

            if (cosmicConfirmationSent &&
                (now - cosmicConfirmationAt).TotalSeconds > 25 &&
                GetAddon("WKSPlanetSelect") != null &&
                !IsBusy())
            {
                StopCosmicTravel(
                    "Cosmic transfer was not accepted " +
                    "after confirmation.");
                return;
            }

            if ((now - cosmicLastAction).TotalSeconds > 60)
                StopCosmicTravel(
                    "Cosmic arrival could not be confirmed.");

            return;
        }

        if (Client.TerritoryType != 959 || IsBusy())
        {
            StopCosmicTravel(
                "Left Drivingway before selection completed.");
            return;
        }

        if ((now - cosmicStarted).TotalSeconds > 35)
        {
            StopCosmicTravel(
                "Cosmic destination selection timed out.");
            return;
        }

        var planetAddon = GetAddon("WKSPlanetSelect");

        if (planetAddon != null)
        {
            cosmicPhase = CosmicPhase.SelectingPlanet;

            if (!TryReadCosmicTitle(
                planetAddon, out string currentTitle))
            {
                cosmicNativeDetails =
                    "WKSPlanetSelect visible, but native title " +
                    "or destination data did not validate.";

                if ((now - cosmicStarted).TotalSeconds > 6)
                    StopCosmicTravel(cosmicNativeDetails);

                return;
            }

            int currentIndex = Array.FindIndex(
                CosmicNames,
                x => string.Equals(
                    x, currentTitle,
                    StringComparison.OrdinalIgnoreCase));

            if (currentIndex < 0)
            {
                StopCosmicTravel(
                    "Unexpected planet shown: " + currentTitle);
                return;
            }

            if (cosmicExpectedNextIndex >= 0)
            {
                if (currentTitle == cosmicPreviousTitle)
                {
                    if ((now - cosmicLastAction)
                        .TotalMilliseconds > 1300)
                    {
                        StopCosmicTravel(
                            "Planet selection did not change. " +
                            cosmicNativeDetails);
                    }

                    return;
                }

                if (currentIndex != cosmicExpectedNextIndex)
                {
                    StopCosmicTravel(
                        "Unexpected planet after navigation: " +
                        currentTitle);
                    return;
                }

                cosmicExpectedNextIndex = -1;
                cosmicPreviousTitle = "";
                cosmicNativeDetails =
                    "Selected " + currentTitle +
                    " after validated arrow event.";
            }

            if ((now - cosmicLastAction)
                .TotalMilliseconds < 650)
                return;

            if (currentIndex != cosmicTargetIndex)
            {
                if (cosmicSelectionAttempts >= 5)
                {
                    StopCosmicTravel(
                        "Maximum planet selection attempts reached.");
                    return;
                }

                bool forward =
                    cosmicTargetIndex > currentIndex;

                int eventParam = forward ? 2 : 1;

                cosmicExpectedNextIndex =
                    currentIndex + (forward ? 1 : -1);

                cosmicPreviousTitle = currentTitle;
                cosmicSelectionAttempts++;

                cosmicNativeDetails =
                    "Arrow event=" + eventParam +
                    " | From=" + currentTitle +
                    " | Expected=" +
                    CosmicNames[cosmicExpectedNextIndex] +
                    " | Attempt=" + cosmicSelectionAttempts;

                CosmicButtonClick(planetAddon, eventParam);
                cosmicLastAction = now;

                return;
            }

            // Do not launch without verifying the actual
            // current title against the requested planet.
            if (!string.Equals(
                currentTitle,
                targetName,
                StringComparison.OrdinalIgnoreCase))
            {
                StopCosmicTravel(
                    "Destination verification failed.");
                return;
            }

            cosmicNativeDetails =
                "Confirmed Node 17: " + currentTitle +
                " | Sending Blast Off event 7.";

            CosmicButtonClick(planetAddon, 7);

            cosmicLastAction = now;
            cosmicPhase = CosmicPhase.WaitingForArrival;

            CosmicMessage(
                "Blast Off requested for " + targetName + ".");

            return;
        }

        if (cosmicPhase == CosmicPhase.SelectingPlanet)
        {
            if ((now - cosmicLastAction)
                .TotalSeconds > 5)
            {
                StopCosmicTravel(
                    "Planet selector closed before completion.");
            }
            return;
        }

        if (cosmicMenuChosen)
        {
            if ((now - cosmicMenuChosenAt)
                .TotalSeconds > 8)
            {
                StopCosmicTravel(
                    "Planet selector did not open after " +
                    "the Drivingway travel option.");
            }
            return;
        }

        if ((now - cosmicLastAction)
            .TotalMilliseconds < 750)
            return;

        var selection = (AddonSelectString*)
            GetAddon("SelectString");

        if (selection != null)
        {
            var menu = selection->PopupMenu.PopupMenu;

            if (menu.EntryNames == null ||
                menu.EntryCount < 1 ||
                menu.EntryNames[0].Value == null)
            {
                StopCosmicTravel(
                    "Drivingway menu structure unavailable.");
                return;
            }

            string firstEntry = Clean(
                Marshal.PtrToStringUTF8(
                    (nint)menu.EntryNames[0].Value) ?? "");

            if (!string.Equals(
                firstEntry,
                "Travel to cosmic exploration area.",
                StringComparison.OrdinalIgnoreCase))
            {
                StopCosmicTravel(
                    "Unexpected dialogue option: " +
                    firstEntry);
                return;
            }

            selection->AtkUnitBase.FireCallbackInt(0);

            cosmicMenuChosen = true;
            cosmicMenuChosenAt = now;
            cosmicLastAction = now;
            cosmicNativeDetails =
                "Selected validated Drivingway menu row 0.";

            return;
        }

        var talk = GetAddon("Talk");

        if (talk != null)
        {
            talk->FireCallbackInt(0);
            cosmicLastAction = now;
            return;
        }

        var npc = FindDrivingway();
        var player = Objects.LocalPlayer;

        if (npc == null || player == null ||
            Vector3.Distance(
                player.Position, npc.Position) > 5f)
        {
            StopCosmicTravel(
                "Drivingway is no longer within interaction range.");
            return;
        }

        var target = TargetSystem.Instance();

        if (target == null)
        {
            StopCosmicTravel(
                "Target system unavailable.");
            return;
        }

        target->InteractWithObject(
            (GameObject*)npc.Address,
            false);

        cosmicLastAction = now;
    }

    private string BuildCosmicTravelReport()
    {
        return
            "\n=== COSMIC TRAVEL TEST1 ===\n" +
            "Phase: " + cosmicPhase + "\n" +
            "Target: " +
            (cosmicTargetIndex >= 0
                ? CosmicNames[cosmicTargetIndex]
                : "None") + "\n" +
            "Status: " + cosmicStatus + "\n" +
            "Confirmation submitted: " +
            cosmicConfirmationSent + "\n" +
            "Selection attempts: " +
            cosmicSelectionAttempts + "\n" +
            "Native details: " + cosmicNativeDetails + "\n";
    }
}