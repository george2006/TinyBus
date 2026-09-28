using System;

namespace TinyBus;

public sealed class MessageRetryPolicy
{
    internal MessageRetryPolicy(
        int maximumAttempts,
        TimeSpan minimumDelay,
        TimeSpan maximumDelay)
    {
        MaximumAttempts = maximumAttempts;
        MinimumDelay = minimumDelay;
        MaximumDelay = maximumDelay;
    }

    public int MaximumAttempts { get; }

    public TimeSpan MinimumDelay { get; }

    public TimeSpan MaximumDelay { get; }

    public TimeSpan CalculateDelay(int failedAttempts)
    {
        if (failedAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(failedAttempts),
                "Failed attempts must be greater than zero.");
        }

        var calculatedTicks = (decimal)MinimumDelay.Ticks * failedAttempts;
        var boundedTicks = Math.Min(calculatedTicks, MaximumDelay.Ticks);
        var delay = TimeSpan.FromTicks((long)boundedTicks);

        return delay;
    }
}
