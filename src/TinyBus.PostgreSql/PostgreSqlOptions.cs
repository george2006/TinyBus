using System;

namespace TinyBus.PostgreSql;

/// <summary>
/// Configures the PostgreSQL transport.
/// </summary>
public sealed class PostgreSqlOptions
{
    private TimeSpan commandLeaseDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets how long a receiver owns a command before another receiver can reclaim it.
    /// The default is five minutes.
    /// </summary>
    public TimeSpan CommandLeaseDuration
    {
        get
        {
            return commandLeaseDuration;
        }

        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    "Command lease duration must be greater than zero.");
            }

            commandLeaseDuration = value;
        }
    }
}
