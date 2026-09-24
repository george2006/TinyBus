using System;

namespace TinyBus;

/// <summary>
/// Configures the logical service hosted by this TinyBus runtime.
/// </summary>
public sealed class TinyBusOptions
{
    internal ServiceIdentity ServiceIdentity { get; private set; }

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
