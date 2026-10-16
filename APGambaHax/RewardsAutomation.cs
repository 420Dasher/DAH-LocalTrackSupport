using System;
using System.Collections.Generic;

namespace APGambaHax;

// Uses Aetherphone's own asynchronous CasinoStore.ClaimBonus.
// No direct HTTP calls and no settlement/result interception.
internal sealed class RewardsAutomation
{
    public static readonly string[] Kinds =
    {
        "welcome",
        "timed",
        "reload",
        "streak",
        "levelup",
        "broke",
        "rebate"
    };

    private readonly Dictionary<string, DateTime> lastAttempts =
        new(StringComparer.Ordinal);

    private string account = "";
    private DateTime nextCheck = DateTime.MinValue;

    public string Status { get; private set; } =
        "Auto claiming disabled";

    public int Requests { get; private set; }

    public string LastRequest { get; private set; } =
        "No claim requested yet.";

    public static int IndexOf(string kind)
    {
        for (int i = 0; i < Kinds.Length; i++)
        {
            if (string.Equals(Kinds[i], kind,
                StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private void CheckAccount(string key)
    {
        if (string.Equals(account, key, StringComparison.Ordinal))
            return;

        account = key;
        lastAttempts.Clear();
        nextCheck = DateTime.MinValue;
        Status = key.Length == 0
            ? "Waiting for Aetherphone account"
            : "Ready to inspect bonuses";
    }

    public void Tick(
        AetherphoneBridge bridge,
        bool enabled,
        bool[] selectedKinds)
    {
        CheckAccount(bridge.AccountKey);

        if (!enabled)
        {
            Status = "Auto claiming disabled";
            return;
        }

        if (!bridge.HasCasinoState || account.Length == 0)
        {
            Status = "Waiting for connected casino state";
            return;
        }

        var now = DateTime.UtcNow;

        if (bridge.ClaimingBonus.Length > 0)
        {
            Status = "Aetherphone is claiming " +
                bridge.ClaimingBonus;
            return;
        }

        if (now < nextCheck)
            return;

        foreach (var bonus in bridge.Bonuses)
        {
            int index = IndexOf(bonus.Kind);

            if (index < 0 ||
                selectedKinds is not { Length: 7 } ||
                !selectedKinds[index] ||
                !bonus.Ready ||
                !bonus.Eligible)
                continue;

            // Avoid hammering a still-ready bonus after a
            // refused claim, delayed refresh, or server outage.
            if (lastAttempts.TryGetValue(bonus.Kind, out var previous) &&
                now - previous < TimeSpan.FromMinutes(5))
                continue;

            if (bridge.TryClaimBonus(bonus.Kind, out string message))
            {
                lastAttempts[bonus.Kind] = now;
                Requests++;
                LastRequest = "Requested: " + bonus.Kind;
                Status = "Claim requested: " + bonus.Kind;
                nextCheck = now.AddSeconds(12);
            }
            else
            {
                LastRequest = message;
                Status = "Claim skipped: " + message;
                nextCheck = now.AddSeconds(30);
            }

            return;
        }

        Status = "Watching for eligible bonuses";
        nextCheck = now.AddSeconds(3);
    }

    public void RequestManual(
        AetherphoneBridge bridge,
        string kind)
    {
        CheckAccount(bridge.AccountKey);

        var now = DateTime.UtcNow;

        if (lastAttempts.TryGetValue(kind, out var previous) &&
            now - previous < TimeSpan.FromSeconds(15))
        {
            Status = "Recently requested: " + kind;
            return;
        }

        if (bridge.TryClaimBonus(kind, out string message))
        {
            lastAttempts[kind] = now;
            Requests++;
            LastRequest = "Requested manually: " + kind;
            Status = "Manual claim requested: " + kind;
            nextCheck = now.AddSeconds(12);
        }
        else
        {
            LastRequest = message;
            Status = "Claim unavailable: " + message;
        }
    }
}
