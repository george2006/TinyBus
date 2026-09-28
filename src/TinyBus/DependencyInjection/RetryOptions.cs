using System;

namespace TinyBus;

public sealed class RetryOptions
{
    private int maximumAttempts = 5;
    private TimeSpan minimumDelay = TimeSpan.FromSeconds(1);
    private TimeSpan maximumDelay = TimeSpan.FromSeconds(30);

    public int MaximumAttempts
    {
        get
        {
            return maximumAttempts;
        }

        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumAttempts),
                    "Maximum attempts must be greater than zero.");
            }

            maximumAttempts = value;
        }
    }

    public TimeSpan MinimumDelay
    {
        get
        {
            return minimumDelay;
        }

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MinimumDelay),
                    "Minimum retry delay cannot be negative.");
            }

            minimumDelay = value;
        }
    }

    public TimeSpan MaximumDelay
    {
        get
        {
            return maximumDelay;
        }

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumDelay),
                    "Maximum retry delay cannot be negative.");
            }

            maximumDelay = value;
        }
    }

    internal void Validate()
    {
        if (maximumDelay < minimumDelay)
        {
            throw new InvalidOperationException(
                "Maximum retry delay must be greater than or equal to minimum retry delay.");
        }
    }

    internal MessageRetryPolicy CreatePolicy()
    {
        var policy = new MessageRetryPolicy(
            maximumAttempts,
            minimumDelay,
            maximumDelay);

        return policy;
    }
}
