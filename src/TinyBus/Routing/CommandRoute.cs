using System;

namespace TinyBus;

public readonly record struct CommandRoute(
    ContractIdentity Contract,
    ServiceIdentity Service)
{
    public void ValidateOwner(ServiceIdentity service)
    {
        if (Service == service)
        {
            return;
        }

        var owners = new[] { Service.Value, service.Value };
        Array.Sort(owners, StringComparer.Ordinal);
        var error = $"Command '{Contract.Name}' version {Contract.Version} "
            + $"has conflicting owners '{owners[0]}' and '{owners[1]}'.";
        throw new InvalidOperationException(error);
    }
}
