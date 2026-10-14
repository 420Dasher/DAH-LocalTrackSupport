using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private const uint BestwaysBurrowAetheryteId = 175;

    private static readonly Vector3 DrivingwayWorldPosition =
        new(25.680908f, -137.41669f, -411.30695f);

    private bool cosmicOwnsPath;
    private bool cosmicMovementStarted;
    private bool cosmicTeleportRequested;
    private bool cosmicSawLoading;

    private DateTime cosmicJourneyStarted;
    private DateTime cosmicStageStarted;
    private DateTime cosmicTeleportRequestedAt;
    private DateTime cosmicMovementRequestedAt;
    private DateTime cosmicLastProgressAt;

    private Vector3 cosmicLastProgressPosition;

    private string cosmicJourneyStatus =
        "No automatic Cosmic approach attempted.";

    private void PrepareCosmicApproach()
    {
        cosmicOwnsPath = false;
        cosmicMovementStarted = false;
        cosmicTeleportRequested = false;
        cosmicSawLoading = false;

        cosmicJourneyStarted = DateTime.UtcNow;
        cosmicStageStarted = cosmicJourneyStarted;
        cosmicTeleportRequestedAt = DateTime.MinValue;
        cosmicMovementRequestedAt = DateTime.MinValue;
        cosmicLastProgressAt = DateTime.MinValue;

        cosmicLastProgressPosition =
            Objects.LocalPlayer?.Position ?? Vector3.Zero;

        cosmicJourneyStatus = "Preparing Cosmic travel.";
    }

    private bool CheckCosmicNavigationAvailable(
        out string problem)
    {
        problem = "";

        try
        {
            // This also verifies that the plugin IPC
            // is available before teleporting elsewhere.
            Pi.GetIpcSubscriber<bool>(
                "vnavmesh.Nav.IsReady"
            ).InvokeFunc();

            bool running = Pi.GetIpcSubscriber<bool>(
                "vnavmesh.Path.IsRunning"
            ).InvokeFunc();

            bool finding = Pi.GetIpcSubscriber<bool>(
                "vnavmesh.SimpleMove.PathfindInProgress"
            ).InvokeFunc();

            if (running || finding)
            {
                problem =
                    "vnavmesh is already navigating. " +
                    "Cancel that path before starting.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            problem =
                "vnavmesh is unavailable: " + ex.Message;
            return false;
        }
    }

    private void StopCosmicPath()
    {
        if (!cosmicOwnsPath)
            return;

        cosmicOwnsPath = false;

        try
        {
            Pi.GetIpcSubscriber<object>(
                "vnavmesh.Path.Stop"
            ).InvokeAction();
        }
        catch (Exception ex)
        {
            Log.Warning(
                "Cosmic vnavmesh stop failed: " + ex.Message);
        }
    }

    private void SetCosmicApproachPhase(CosmicPhase next)
    {
        cosmicPhase = next;
        cosmicStageStarted = DateTime.UtcNow;
    }

    private void TickCosmicApproach(DateTime now)
    {
        if (cosmicPhase == CosmicPhase.TeleportingToBurrow)
        {
            if (Client.TerritoryType == 959)
            {
                SetCosmicApproachPhase(
                    CosmicPhase.MovingToDrivingway);
                cosmicJourneyStatus =
                    "Already in Mare Lamentorum.";
                return;
            }

            if (IsBusy())
            {
                StopCosmicTravel(
                    "Cannot initiate teleport while loading.");
                return;
            }

            if (!IsUnlocked(BestwaysBurrowAetheryteId))
            {
                StopCosmicTravel(
                    "Bestways Burrow Aetheryte is not unlocked.");
                return;
            }

            var telepo = Telepo.Instance();

            if (telepo == null)
            {
                StopCosmicTravel(
                    "The game's teleport service is unavailable.");
                return;
            }

            bool accepted = telepo->Teleport(
                BestwaysBurrowAetheryteId, 0);

            if (!accepted)
            {
                StopCosmicTravel(
                    "The game rejected teleport to Bestways Burrow.");
                return;
            }

            cosmicTeleportRequested = true;
            cosmicTeleportRequestedAt = now;

            SetCosmicApproachPhase(
                CosmicPhase.WaitingForBurrow);

            cosmicJourneyStatus =
                "Teleport requested: Bestways Burrow.";

            CosmicMessage(
                "Teleporting to Bestways Burrow...");
            return;
        }

        if (cosmicPhase == CosmicPhase.WaitingForBurrow)
        {
            if (IsBusy())
            {
                cosmicSawLoading = true;
                return;
            }

            if (Client.TerritoryType == 959 &&
                Objects.LocalPlayer != null &&
                (cosmicSawLoading ||
                 (now - cosmicTeleportRequestedAt)
                     .TotalSeconds >= 3))
            {
                cosmicJourneyStatus =
                    "Arrived in Mare Lamentorum.";

                SetCosmicApproachPhase(
                    CosmicPhase.MovingToDrivingway);

                CosmicMessage(
                    "At Bestways Burrow. Finding Drivingway...");
                return;
            }

            if ((now - cosmicTeleportRequestedAt)
                .TotalSeconds > 50)
            {
                StopCosmicTravel(
                    "Timed out teleporting to Bestways Burrow.");
            }

            return;
        }

        if (cosmicPhase != CosmicPhase.MovingToDrivingway)
            return;

        if (IsBusy() || Client.TerritoryType != 959)
        {
            StopCosmicTravel(
                "Left Mare Lamentorum during navigation.");
            return;
        }

        var player = Objects.LocalPlayer;

        if (player == null)
            return;

        // Match the existing Housing behavior:
        // manual WASD/arrow movement cancels the entire
        // navigation request once automatic movement starts.
        if (cosmicMovementStarted &&
            HousingMovementKeysPressed())
        {
            StopCosmicTravel(
                "Cancelled by manual movement.");
            return;
        }

        var npc = FindDrivingway();

        Vector3 goal =
            npc?.Position ?? DrivingwayWorldPosition;

        float distance = Vector3.Distance(
            player.Position, goal);

        if (npc != null &&
            distance <= 4.25f &&
            npc.IsTargetable)
        {
            StopCosmicPath();

            cosmicJourneyStatus =
                "Drivingway reached. Opening dialogue.";

            // Reset the existing 35-second menu timer.
            cosmicStarted = now;
            cosmicLastAction = DateTime.MinValue;

            SetCosmicApproachPhase(
                CosmicPhase.OpeningDrivingway);

            CosmicMessage(
                "Reached Drivingway. Selecting destination...");
            return;
        }

        if ((now - cosmicStageStarted).TotalSeconds > 100)
        {
            StopCosmicTravel(
                "Timed out navigating to Drivingway.");
            return;
        }

        if (!cosmicMovementStarted)
        {
            if (HousingMovementKeysPressed())
            {
                StopCosmicTravel(
                    "Cancelled: movement keys are pressed.");
                return;
            }

            bool ready;

            try
            {
                ready = Pi.GetIpcSubscriber<bool>(
                    "vnavmesh.Nav.IsReady"
                ).InvokeFunc();
            }
            catch (Exception ex)
            {
                StopCosmicTravel(
                    "vnavmesh unavailable: " + ex.Message);
                return;
            }

            if (!ready)
            {
                cosmicJourneyStatus =
                    "Waiting for vnavmesh to load the map.";

                if ((now - cosmicStageStarted).TotalSeconds > 35)
                {
                    StopCosmicTravel(
                        "vnavmesh map did not become ready.");
                }

                return;
            }

            try
            {
                bool alreadyRunning =
                    Pi.GetIpcSubscriber<bool>(
                        "vnavmesh.Path.IsRunning"
                    ).InvokeFunc();

                bool alreadyFinding =
                    Pi.GetIpcSubscriber<bool>(
                        "vnavmesh.SimpleMove.PathfindInProgress"
                    ).InvokeFunc();

                if (alreadyRunning || alreadyFinding)
                {
                    StopCosmicTravel(
                        "Another vnavmesh route is already active.");
                    return;
                }

                // Pathfind from the player's current
                // position to Drivingway, on foot.
                bool started =
                    Pi.GetIpcSubscriber<Vector3, bool, bool>(
                        "vnavmesh.SimpleMove.PathfindAndMoveTo"
                    ).InvokeFunc(goal, false);

                if (!started)
                {
                    StopCosmicTravel(
                        "vnavmesh rejected the route to Drivingway.");
                    return;
                }

                cosmicOwnsPath = true;
                cosmicMovementStarted = true;
                cosmicMovementRequestedAt = now;
                cosmicLastProgressAt = now;
                cosmicLastProgressPosition = player.Position;

                cosmicJourneyStatus =
                    "Navigating to Drivingway (" +
                    distance.ToString("F1") + " yalms).";

                CosmicMessage(
                    "Pathfinding to Drivingway...");
            }
            catch (Exception ex)
            {
                StopCosmicTravel(
                    "Could not start pathfinding: " +
                    ex.Message);
            }

            return;
        }

        if (Vector3.Distance(
            player.Position,
            cosmicLastProgressPosition) >= 0.4f)
        {
            cosmicLastProgressPosition = player.Position;
            cosmicLastProgressAt = now;
        }

        cosmicJourneyStatus =
            "Walking to Drivingway: " +
            distance.ToString("F1") + " yalms remaining.";

        try
        {
            bool finding = Pi.GetIpcSubscriber<bool>(
                "vnavmesh.SimpleMove.PathfindInProgress"
            ).InvokeFunc();

            bool running = Pi.GetIpcSubscriber<bool>(
                "vnavmesh.Path.IsRunning"
            ).InvokeFunc();

            if (!finding && !running &&
                (now - cosmicMovementRequestedAt)
                    .TotalSeconds > 3)
            {
                StopCosmicTravel(
                    "vnavmesh stopped before reaching Drivingway.");
                return;
            }

            double stalled =
                (now - cosmicLastProgressAt).TotalSeconds;

            if ((!finding && stalled > 12) ||
                (finding && stalled > 30))
            {
                StopCosmicTravel(
                    "vnavmesh navigation stopped progressing.");
            }
        }
        catch (Exception ex)
        {
            StopCosmicTravel(
                "Lost vnavmesh status: " + ex.Message);
        }
    }

    private string BuildCosmicApproachReport()
    {
        return
            "Approach: " + cosmicJourneyStatus + "\n" +
            "Owns vnavmesh path: " + cosmicOwnsPath + "\n" +
            "Movement started: " + cosmicMovementStarted + "\n" +
            "Teleport requested: " + cosmicTeleportRequested + "\n" +
            "Saw loading: " + cosmicSawLoading + "\n" +
            "Journey started: " +
                cosmicJourneyStarted.ToString("O") + "\n";
    }
}