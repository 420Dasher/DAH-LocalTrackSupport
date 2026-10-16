using System;
using System.Collections.Generic;

namespace APGambaHax;

// An immutable observation supplied by an Aetherphone adapter.
// This component never submits wagers or claims.
internal readonly record struct RoundObservation(
    string Key,
    string GameKind,
    string Source,
    long Stake,
    long Payout,
    long Net,
    long SettledAtUnix,
    bool AmountsKnown);

internal readonly record struct RoundTotals(
    int Rounds,
    int Wins,
    int Losses,
    int Pushes,
    int AmountsKnown,
    long Staked,
    long Returned,
    long Net);

internal sealed class UniversalRoundTracker
{
    private readonly Dictionary<string, RoundObservation> observations =
        new(StringComparer.Ordinal);

    private string account = "";
    private long startedAtUnix =
        DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public string Status { get; private set; } =
        "Waiting for authenticated Aetherphone data";

    public int ObservedCount => observations.Count;

    public void Tick(AetherphoneBridge bridge)
    {
        string key = bridge.AccountKey;

        if (key.Length == 0)
        {
            Status = "Waiting for authenticated casino data";
            return;
        }

        if (!string.Equals(account, key, StringComparison.Ordinal))
        {
            account = key;
            observations.Clear();
            startedAtUnix =
                DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        foreach (var round in bridge.RoundObservations)
        {
            if (round.Key.Length == 0 ||
                round.GameKind.Length == 0 ||
                round.SettledAtUnix < startedAtUnix ||
                round.SettledAtUnix >
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30)
                continue;

            if (observations.TryGetValue(round.Key, out var existing))
            {
                // A live record with wager details supersedes a
                // partial table-history record.
                if (!existing.AmountsKnown && round.AmountsKnown)
                    observations[round.Key] = round;

                continue;
            }

            // Prevent unbounded growth during long sessions.
            if (observations.Count >= 10000)
            {
                Status = "Tracker capacity reached";
                return;
            }

            observations.Add(round.Key, round);
        }

        Status = "Tracking completed chip rounds";
    }

    public RoundTotals Totals(string gameKind = "")
    {
        int rounds = 0;
        int wins = 0;
        int losses = 0;
        int pushes = 0;
        int known = 0;
        long staked = 0;
        long returned = 0;
        long net = 0;

        foreach (var round in observations.Values)
        {
            if (gameKind.Length > 0 &&
                !string.Equals(
                    round.GameKind,
                    gameKind,
                    StringComparison.Ordinal))
                continue;

            rounds++;

            if (round.Net > 0)
                wins++;
            else if (round.Net < 0)
                losses++;
            else
                pushes++;

            if (round.AmountsKnown)
            {
                known++;
                staked += round.Stake;
                returned += round.Payout;
            }

            net += round.Net;
        }

        return new RoundTotals(
            rounds, wins, losses, pushes,
            known, staked, returned, net);
    }
}
