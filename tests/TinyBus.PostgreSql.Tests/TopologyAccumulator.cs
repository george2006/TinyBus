namespace TinyBus.PostgreSql.Tests;

// Simulates shared transport topology for tests. It retains routing facts, never manifests.
internal sealed class TopologyAccumulator
{
    private readonly Dictionary<ContractIdentity, CommandRoute> commandRoutes = new();
    private readonly Dictionary<ContractIdentity, HashSet<ServiceIdentity>> eventSubscriptions = new();

    // Tests can delay or fail access without replacing the actual reconciliation behavior.
    public Task Availability { get; set; } = Task.CompletedTask;

    public async ValueTask ReconcileAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        var availability = Availability.WaitAsync(cancellationToken);
        await availability.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        ValidateCommandOwnership(topology);

        // Absence is not deletion intent: an older replica must preserve newer declarations.
        RegisterCommandOwners(topology);
        RegisterEventSubscriptions(topology);
    }

    public async ValueTask<IReadOnlyCollection<CommandRoute>> LoadAsync(
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CancellationToken cancellationToken = default)
    {
        var availability = Availability.WaitAsync(cancellationToken);
        await availability.ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var contracts = new HashSet<ContractIdentity>(requiredContracts);
        var routes = new List<CommandRoute>();

        foreach (var contract in contracts)
        {
            if (commandRoutes.TryGetValue(contract, out var route))
            {
                routes.Add(route);
            }
        }

        return routes.AsReadOnly();
    }

    public IReadOnlyCollection<ServiceIdentity> GetEventSubscribers(ContractIdentity contract)
    {
        if (!eventSubscriptions.TryGetValue(contract, out var subscribers))
        {
            return Array.Empty<ServiceIdentity>();
        }

        var snapshot = new ServiceIdentity[subscribers.Count];
        subscribers.CopyTo(snapshot);
        return snapshot;
    }

    private void ValidateCommandOwnership(ServiceTopology topology)
    {
        foreach (var message in topology.Messages)
        {
            if (message.Kind != MessageKind.Command)
            {
                continue;
            }

            if (!commandRoutes.TryGetValue(message.Contract, out var existingRoute))
            {
                continue;
            }

            if (existingRoute.Service == topology.Service)
            {
                continue;
            }

            var owners = new[] { existingRoute.Service.Value, topology.Service.Value };
            Array.Sort(owners, StringComparer.Ordinal);
            var error = $"Command '{message.Contract.Name}' version {message.Contract.Version} "
                + $"has conflicting owners '{owners[0]}' and '{owners[1]}'.";
            throw new InvalidOperationException(error);
        }
    }

    private void RegisterCommandOwners(ServiceTopology topology)
    {
        foreach (var message in topology.Messages)
        {
            if (message.Kind != MessageKind.Command)
            {
                continue;
            }

            var route = new CommandRoute(message.Contract, topology.Service);
            commandRoutes[message.Contract] = route;
        }
    }

    private void RegisterEventSubscriptions(ServiceTopology topology)
    {
        foreach (var message in topology.Messages)
        {
            if (message.Kind != MessageKind.Event)
            {
                continue;
            }

            if (!eventSubscriptions.TryGetValue(message.Contract, out var subscribers))
            {
                subscribers = new HashSet<ServiceIdentity>();
                eventSubscriptions.Add(message.Contract, subscribers);
            }

            subscribers.Add(topology.Service);
        }
    }
}
