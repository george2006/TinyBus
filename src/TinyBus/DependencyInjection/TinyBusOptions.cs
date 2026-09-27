using System;
using Microsoft.Extensions.DependencyInjection;

namespace TinyBus;

/// <summary>
/// Configures the logical service hosted by this TinyBus runtime.
/// </summary>
public sealed class TinyBusOptions
{
    private int maximumConcurrentMessages = Environment.ProcessorCount;

    internal TinyBusOptions(IServiceCollection services)
    {
        Services = services;
    }

    /// <summary>
    /// Registrations available to transport provider extensions. Changes are applied to the
    /// application collection only after TinyBus configuration succeeds.
    /// </summary>
    public IServiceCollection Services { get; }

    internal ServiceIdentity ServiceIdentity { get; private set; }

    public int MaximumConcurrentMessages
    {
        get
        {
            return maximumConcurrentMessages;
        }

        set
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaximumConcurrentMessages),
                    "Maximum concurrent messages must be greater than zero.");
            }

            maximumConcurrentMessages = value;
        }
    }

    public void Service(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ServiceIdentity = new ServiceIdentity(name);
    }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(ServiceIdentity.Value))
        {
            throw new InvalidOperationException("TinyBus requires a service identity. Configure it with bus.Service(name).");
        }
    }
}
