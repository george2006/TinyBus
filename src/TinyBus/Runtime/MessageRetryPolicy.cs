using System;

namespace TinyBus;

/// <summary>
/// Exposes the validated retry policy to the runtime and transport providers.
/// Applications configure it through <see cref="RetryOptions"/>.
/// </summary>
public sealed class MessageRetryPolicy
{
    public MessageRetryPolicy(
        int maximumAttempts,
        TimeSpan minimumDelay,
        TimeSpan maximumDelay)
    {
        if (maximumAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumAttempts),
                "Maximum attempts must be greater than zero.");
        }

        if (minimumDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumDelay),
                "Minimum retry delay cannot be negative.");
        }

        if (maximumDelay < minimumDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDelay),
                "Maximum retry delay must be greater than or equal to minimum retry delay.");
        }

        MaximumAttempts = maximumAttempts;
        MinimumDelay = minimumDelay;
        MaximumDelay = maximumDelay;
    }

    public int MaximumAttempts { get; }

    public TimeSpan MinimumDelay { get; }

    public TimeSpan MaximumDelay { get; }

    /// <summary>
    /// Calculates bounded linear backoff for a one-based failed attempt count.
    /// </summary>
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
