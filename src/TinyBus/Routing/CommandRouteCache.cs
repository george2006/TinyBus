using System.Collections.Frozen;
using System.Collections.Generic;

namespace TinyBus;

internal sealed class CommandRouteCache
{
    private readonly FrozenDictionary<ContractIdentity, ServiceIdentity> routes;

    public CommandRouteCache(IEnumerable<CommandRoute> commandRoutes)
    {
        var owners = new Dictionary<ContractIdentity, ServiceIdentity>();

        foreach (var route in commandRoutes)
        {
            if (owners.TryGetValue(route.Contract, out var existingOwner))
            {
                route.ValidateOwner(existingOwner);
                continue;
            }

            owners.Add(route.Contract, route.Service);
        }

        routes = owners.ToFrozenDictionary();
    }

    public bool TryResolve(ContractIdentity contract, out ServiceIdentity service)
    {
        return routes.TryGetValue(contract, out service);
    }
}
