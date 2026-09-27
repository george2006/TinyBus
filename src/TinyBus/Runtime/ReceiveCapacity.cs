using System;

namespace TinyBus;

public readonly record struct ReceiveCapacity
{
    public ReceiveCapacity(int maximum, int available)
    {
        if (maximum <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximum),
                "Maximum receive capacity must be greater than zero.");
        }

        if (available <= 0 || available > maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(available),
                "Available receive capacity must be between one and the maximum capacity.");
        }

        Maximum = maximum;
        Available = available;
    }

    public int Maximum { get; }

    public int Available { get; }
}
