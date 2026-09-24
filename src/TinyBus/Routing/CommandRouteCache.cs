using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace TinyBus;

internal sealed class CommandRouteCache
{
    private FrozenDictionary<ContractIdentity, ServiceIdentity>? routes;

    public CommandRouteCache()
    {
    }

    public CommandRouteCache(IEnumerable<CommandRoute> commandRoutes)
    {
        Replace(commandRoutes);
    }

    public bool TryResolve(ContractIdentity contract, out ServiceIdentity service)
    {
        var snapshot = Volatile.Read(ref routes);
        if (snapshot is null)
        {
            throw new InvalidOperationException("Command routes have not been initialized.");
        }

        return snapshot.TryGetValue(contract, out service);
    }

    public void Replace(
        IEnumerable<CommandRoute> commandRoutes,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = CreateSnapshot(commandRoutes);

        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref routes, snapshot);
    }

    private static FrozenDictionary<ContractIdentity, ServiceIdentity> CreateSnapshot(
        IEnumerable<CommandRoute> commandRoutes)
    {
        var owners = new Dictionary<ContractIdentity, ServiceIdentity>();

        foreach (var route in commandRoutes)
        {
            if (owners.TryGetValue(route.Contract, out var existingOwner))
            {
                ValidateRouteConsistency(route, existingOwner);
                continue;
            }

            owners.Add(route.Contract, route.Service);
        }

        return owners.ToFrozenDictionary();
    }

    private static void ValidateRouteConsistency(CommandRoute route, ServiceIdentity existingOwner)
    {
        if (route.Service == existingOwner)
        {
            return;
        }

        var owners = new[] { route.Service.Value, existingOwner.Value };
        Array.Sort(owners, StringComparer.Ordinal);
        var error = $"Command '{route.Contract.Name}' version {route.Contract.Version} "
            + $"has conflicting owners '{owners[0]}' and '{owners[1]}'.";
        throw new InvalidOperationException(error);
    }
}
