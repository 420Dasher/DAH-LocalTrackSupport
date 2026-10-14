using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Keys;

namespace AethernetAssist;

public sealed unsafe partial class Plugin
{
    private readonly record struct GateRoute(
        Vector3 Approach,
        Vector3 Across
    );

    private bool housingGateWalkAttempted;
    private bool housingGateWalkOwnsPath;
    private bool housingGateWalkWasStarted;
    private DateTime housingGateWalkStarted;
    private DateTime housingGateLastProgress;
    private Vector3 housingGateLastPosition;

    private string housingGateWalkDiagnostic =
        "No automatic gate movement attempted.";

    private bool TryGetHousingGateRoute(out GateRoute route)
    {
        route = default;
        var player = Objects.LocalPlayer;

        if (player == null)
            return false;

        foreach (var entrance in WardEntrances)
        {
            if (entrance.Territory != Client.TerritoryType ||
                entrance.NpcBaseId != 0)
                continue;

            float d1 = Vector3.Distance(
                player.Position, entrance.First);
            float d2 = Vector3.Distance(
                player.Position, entrance.Second);

            if (MathF.Min(d1, d2) > config.OpenRange)
                return false;

            bool subdivision = d2 < d1;

            if (entrance.Territory == 339)
            {
                route = subdivision
                    ? new GateRoute(
                        new(-539.831f, 48.578f, -714.265f),
                        new(-534.134f, 48.330f, -714.389f))
                    : new GateRoute(
                        new(-10.033f, 48.731f, -162.866f),
                        new(-10.0f, 48.347f, -174.5f));

                return true;
            }

            if (entrance.Territory == 341)
            {
                route = subdivision
                    ? new GateRoute(
                        new(-509.080f, -11.077f, -710.303f),
                        new(-505.632f, -11.077f, -715.153f))
                    : new GateRoute(
                        new(-10.556f, -11.077f, -198.263f),
                        new(-14.580f, -11.077f, -201.230f));

                return true;
            }
        }

        return false;
    }

    private void PrepareHousingGateWalk()
    {
        StopHousingGateWalk();
        housingGateWalkAttempted = false;
        housingGateWalkWasStarted = false;
        housingGateWalkDiagnostic = "Waiting for gate action.";
    }

    private void StopHousingGateWalk()
    {
        if (!housingGateWalkOwnsPath)
            return;

        housingGateWalkOwnsPath = false;

        try
        {
            Pi.GetIpcSubscriber<object>(
                "vnavmesh.Path.Stop"
            ).InvokeAction();
        }
        catch (Exception ex)
        {
            Log.Warning(
                "Gate movement stop failed: " + ex.Message);
        }
    }

    private bool HousingMovementKeysPressed()
    {
        return
            KeyState[VirtualKey.W] ||
            KeyState[VirtualKey.A] ||
            KeyState[VirtualKey.S] ||
            KeyState[VirtualKey.D] ||
            KeyState[VirtualKey.UP] ||
            KeyState[VirtualKey.DOWN] ||
            KeyState[VirtualKey.LEFT] ||
            KeyState[VirtualKey.RIGHT];
    }

    private void StartHousingGateWalk()
    {
        housingGateWalkAttempted = true;

        if (!config.AutoWalkHousingGates)
        {
            housingGateWalkDiagnostic = "Auto-walk disabled.";
            return;
        }

        if (!TryGetHousingGateRoute(out var route))
        {
            housingGateWalkDiagnostic = "No nearby gate route.";
            return;
        }

        if (HousingMovementKeysPressed())
        {
            housingGateWalkDiagnostic =
                "Movement keys pressed; manual fallback.";
            return;
        }

        try
        {
            if (Pi.GetIpcSubscriber<bool>(
                "vnavmesh.Path.IsRunning"
            ).InvokeFunc())
            {
                housingGateWalkDiagnostic =
                    "Navigation already running; manual fallback.";
                return;
            }

            var points = new List<Vector3>
            {
                route.Approach,
                route.Across
            };

            Pi.GetIpcSubscriber<List<Vector3>, bool, object>(
                "vnavmesh.Path.MoveTo"
            ).InvokeAction(points, false);

            housingGateWalkOwnsPath = true;
            housingGateWalkWasStarted = true;
            housingGateWalkStarted = DateTime.UtcNow;
            housingGateLastProgress = DateTime.UtcNow;
            housingGateLastPosition =
                Objects.LocalPlayer?.Position ?? Vector3.Zero;

            housingGateWalkDiagnostic =
                "Automatic walk started.";

            housingStatus = "Walking through gate...";
        }
        catch (Exception ex)
        {
            housingGateWalkDiagnostic =
                "Navigation unavailable: " + ex.Message;

            housingStatus =
                "Walk through the gate manually.";
        }
    }

    private void TickHousingGateWalk()
    {
        if (!housingGateSource)
            return;

        bool gatePhase =
            housingPhase == HousingPhase.SelectGoToWard ||
            housingPhase == HousingPhase.SelectExit;

        if (!gatePhase)
        {
            StopHousingGateWalk();
            return;
        }

        if (GetAddon("SelectString") != null ||
            GetAddon("HousingSelectBlock") != null)
        {
            if (housingGateWalkOwnsPath)
            {
                housingGateWalkDiagnostic =
                    "Native menu opened; movement stopped.";
            }

            StopHousingGateWalk();
            return;
        }

        if (!housingGateWalkAttempted)
        {
            StartHousingGateWalk();
            return;
        }

        // A manual override must cancel the entire Housing
        // request, not just the vnavmesh movement.
        //
        // Keep this check active after the path ends, until
        // the native gate menu actually appears.
        //
        // Do not cancel the manual fallback when auto-walk
        // was disabled, unavailable, or never started.
        if (housingGateWalkWasStarted &&
            HousingMovementKeysPressed())
        {
            housingGateWalkDiagnostic =
                "Full travel cancelled by manual movement.";

            StopHousing("Cancelled by manual movement.");
            return;
        }

        if (!housingGateWalkOwnsPath)
            return;

        if (!config.AutoWalkHousingGates ||
            IsBusy())
        {
            housingGateWalkDiagnostic =
                "Movement interrupted or disabled.";

            StopHousingGateWalk();
            return;
        }

        var now = DateTime.UtcNow;
        var player = Objects.LocalPlayer;

        if (player == null)
        {
            StopHousingGateWalk();
            return;
        }

        if (Vector3.Distance(
            player.Position,
            housingGateLastPosition) >= 0.35f)
        {
            housingGateLastPosition = player.Position;
            housingGateLastProgress = now;
        }

        if ((now - housingGateWalkStarted).TotalSeconds > 9 ||
            (now - housingGateLastProgress).TotalSeconds > 3)
        {
            housingGateWalkDiagnostic =
                "Timed out or stopped progressing.";

            housingStatus =
                "Automatic walk stopped. Cross gate manually.";

            StopHousingGateWalk();
            return;
        }

        try
        {
            bool running = Pi.GetIpcSubscriber<bool>(
                "vnavmesh.Path.IsRunning"
            ).InvokeFunc();

            if (!running &&
                (now - housingGateWalkStarted).TotalSeconds > 1.5)
            {
                housingGateWalkDiagnostic =
                    "Movement ended; waiting for native menu.";

                StopHousingGateWalk();
            }
        }
        catch (Exception ex)
        {
            housingGateWalkDiagnostic =
                "Navigation status unavailable: " + ex.Message;

            StopHousingGateWalk();
        }
    }
}