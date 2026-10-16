using System;
using System.Reflection;

namespace APGambaHax;

// Uses Aetherphone's own practice shift object.
// Deliberately contains no wager-start, wager-finish or network calls.
internal sealed class PracticeBarkeepAutomation
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private object? trackedShift;
    private bool faulted;

    public string Status { get; private set; } = "Disabled";
    public int AutoServed { get; private set; }

    private static object? Member(object? target, string name)
    {
        if (target is null)
            return null;

        var type = target.GetType();

        return type.GetProperty(name, Flags)?.GetValue(target)
            ?? type.GetField(name, Flags)?.GetValue(target);
    }

    private static MethodInfo RequireMethod(
        object target,
        string name,
        params Type[] parameterTypes)
    {
        return target.GetType().GetMethod(
            name, Flags, null, parameterTypes, null)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
    }

    private static int ReadInt(object target, string name)
    {
        return Member(target, name) is int value
            ? value
            : throw new MissingMemberException(target.GetType().FullName, name);
    }

    public void Reset()
    {
        trackedShift = null;
        AutoServed = 0;
        faulted = false;
        Status = "Waiting for practice shift";
    }

    public void Stop()
    {
        trackedShift = null;
        Status = "Disabled";
    }

    public void Tick(object? cabinet)
    {
        if (faulted)
            return;

        if (cabinet is null)
        {
            Status = "Waiting for Aetherphone";
            return;
        }

        try
        {
            var mode = Member(cabinet, "mode")
                ?? throw new MissingFieldException("Barkeep mode is unavailable.");

            // Hard guard: only free-play practice, never a wager.
            if (!string.Equals(mode.ToString(), "Practice", StringComparison.Ordinal))
            {
                trackedShift = null;
                Status = "Waiting for practice shift";
                return;
            }

            var shift = Member(cabinet, "shift");

            if (shift is null)
            {
                trackedShift = null;
                Status = "Practice idle";
                return;
            }

            // Practice shifts have no server wager round ID.
            if (Member(shift, "RoundId") is not string roundId ||
                roundId.Length != 0)
            {
                Status = "Ignored non-practice round";
                return;
            }

            if (!ReferenceEquals(trackedShift, shift))
            {
                trackedShift = shift;
                AutoServed = 0;
            }

            if (Member(cabinet, "practiceSettled") is true)
            {
                Status = "Practice complete";
                return;
            }

            int patrons = ReadInt(shift, "PatronCount");
            int completed = ReadInt(shift, "CompletedOrders");

            if (patrons < 1 || patrons > 14 ||
                completed < 0 || completed > patrons)
                throw new InvalidOperationException("Unexpected practice patron state.");

            if (completed == patrons)
            {
                Status = "All patrons served - waiting for result";
                return;
            }

            double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;
            double elapsed = (double)(
                RequireMethod(shift, "ElapsedSeconds", typeof(double))
                    .Invoke(shift, new object[] { now }) ?? 0d
            );

            // Preserve AutoBarkeep DEV13's one-order-per-four-seconds pacing.
            int available = Math.Min(
                patrons, (int)Math.Floor(Math.Max(0d, elapsed) / 4d));

            if (completed >= available)
            {
                Status = $"Practice active - {completed}/{patrons} patrons";
                return;
            }

            bool arrived = (bool)(
                RequireMethod(shift, "CurrentPatronArrived", typeof(double))
                    .Invoke(shift, new object[] { elapsed }) ?? false
            );

            if (!arrived)
            {
                Status = $"Waiting for patron - {completed}/{patrons}";
                return;
            }

            int stepCount = ReadInt(shift, "CurrentStepCount");
            int stepIndex = ReadInt(shift, "CurrentStepIndex");

            if (stepCount < 1 || stepCount > 4 ||
                stepIndex < 0 || stepIndex >= stepCount)
                throw new InvalidOperationException("Unexpected practice step state.");

            var commit = RequireMethod(
                shift, "CommitStepGrade", typeof(int));

            // Complete only the current patron's remaining steps.
            // Already-played grades are intentionally not rewritten.
            for (int index = stepIndex; index < stepCount; index++)
                commit.Invoke(shift, new object[] { 100 });

            var verbStage = Member(cabinet, "verbStage");

            if (verbStage is not null)
                RequireMethod(verbStage, "Cancel").Invoke(verbStage, null);

            AutoServed++;

            int after = ReadInt(shift, "CompletedOrders");
            Status = $"Auto-perfect practice - {after}/{patrons} patrons";
        }
        catch (Exception ex)
        {
            faulted = true;
            Status = "Paused: " + ex.GetBaseException().GetType().Name;
        }
    }
}
