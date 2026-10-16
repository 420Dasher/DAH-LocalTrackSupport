using System;
using System.Collections.Generic;

namespace APGambaHax;

// Claims through Aetherphone's native CasinoFloorStore.
// Never consumes the result intended for Aetherphone's own UI.
internal sealed class MissionsAutomation
{
    private readonly Dictionary<string, DateTime> attempts =
        new(StringComparer.Ordinal);

    private string account = "";
    private int dayIndex = -1;
    private DateTime nextCheckUtc = DateTime.MinValue;

    public bool Enabled { get; set; }

    public string Status { get; private set; } =
        "Automatic mission claiming disabled";

    public string LastRequest { get; private set; } =
        "No mission claims requested yet.";

    public int Requests { get; private set; }

    private void Sync(AetherphoneBridge bridge)
    {
        string key = bridge.AccountKey;

        if (key.Length == 0)
        {
            Status = "Waiting for Aetherphone account";
            return;
        }

        if (!string.Equals(account, key, StringComparison.Ordinal))
        {
            account = key;
            dayIndex = -1;
            attempts.Clear();
            nextCheckUtc = DateTime.MinValue;
        }

        if (bridge.MissionsLoaded &&
            dayIndex != bridge.MissionsDayIndex)
        {
            dayIndex = bridge.MissionsDayIndex;
            attempts.Clear();
            nextCheckUtc = DateTime.MinValue;
        }
    }

    private static bool ResetPending(AetherphoneBridge bridge) =>
        bridge.MissionsResetsAtUnix > 0 &&
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() >=
            bridge.MissionsResetsAtUnix;

    public void Tick(AetherphoneBridge bridge, bool enabled)
    {
        Sync(bridge);

        if (!enabled)
        {
            Status = "Automatic mission claiming disabled";
            return;
        }

        if (bridge.AccountKey.Length == 0 ||
            !bridge.MissionsLoaded)
        {
            Status = "Waiting for mission data";
            return;
        }

        if (ResetPending(bridge))
        {
            Status = "Waiting for daily mission reset";
            return;
        }

        if (bridge.ClaimingMission.Length > 0)
        {
            Status = "Aetherphone is claiming a mission";
            return;
        }

        DateTime now = DateTime.UtcNow;

        if (now < nextCheckUtc)
            return;

        foreach (var mission in bridge.Missions)
        {
            if (!mission.Complete || mission.Claimed)
                continue;

            string attemptKey = dayIndex + ":" + mission.Id;

            if (attempts.TryGetValue(attemptKey, out var previous)
                && now - previous < TimeSpan.FromMinutes(3))
                continue;

            if (bridge.TryClaimMission(mission.Id, out var result))
            {
                attempts[attemptKey] = now;
                Requests++;
                LastRequest = "Auto requested: " + mission.Id;
                Status = "Claim requested: " + mission.Description;
                nextCheckUtc = now.AddSeconds(12);
            }
            else
            {
                LastRequest = result;
                Status = "Claim skipped: " + result;
                nextCheckUtc = now.AddSeconds(20);
            }

            return;
        }

        Status = "Watching for completed missions";
        nextCheckUtc = now.AddSeconds(3);
    }

    public void RequestManual(
        AetherphoneBridge bridge,
        string missionId)
    {
        Sync(bridge);

        if (ResetPending(bridge))
        {
            Status = "Waiting for daily mission reset";
            return;
        }

        string key = dayIndex + ":" + missionId;
        DateTime now = DateTime.UtcNow;

        if (attempts.TryGetValue(key, out var previous) &&
            now - previous < TimeSpan.FromSeconds(20))
        {
            Status = "Mission claim recently requested";
            return;
        }

        if (bridge.TryClaimMission(missionId, out var result))
        {
            attempts[key] = now;
            Requests++;
            LastRequest = "Manual request: " + missionId;
            Status = "Manual mission claim requested";
            nextCheckUtc = now.AddSeconds(12);
        }
        else
        {
            LastRequest = result;
            Status = "Claim unavailable: " + result;
        }
    }
}
