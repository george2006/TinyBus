namespace TinyBus.PostgreSql.Tests;

internal sealed class RouteTestTransport : ITransport
{
    private readonly TopologyAccumulator reconciler;
    private readonly TopologyAccumulator routeSource;
    private readonly IReadOnlyCollection<ContractIdentity> requiredContracts;
    private readonly CommandRouteCache routeCache;

    public RouteTestTransport(
        TopologyAccumulator reconciler,
        TopologyAccumulator routeSource,
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CommandRouteCache routeCache)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(routeSource);
        ArgumentNullException.ThrowIfNull(requiredContracts);
        ArgumentNullException.ThrowIfNull(routeCache);

        this.reconciler = reconciler;
        this.routeSource = routeSource;
        this.requiredContracts = new List<ContractIdentity>(requiredContracts);
        this.routeCache = routeCache;
    }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reconciliation = reconciler.ReconcileAsync(topology, cancellationToken);
        await reconciliation.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var loading = routeSource.LoadAsync(requiredContracts, cancellationToken);
        var routes = await loading.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequiredRoutes(routes);

        routeCache.Replace(routes, cancellationToken);
    }

    public ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    private void ValidateRequiredRoutes(IReadOnlyCollection<CommandRoute> routes)
    {
        var availableContracts = new HashSet<ContractIdentity>();
        foreach (var route in routes)
        {
            availableContracts.Add(route.Contract);
        }

        foreach (var contract in requiredContracts)
        {
            if (availableContracts.Contains(contract))
            {
                continue;
            }

            var error = $"Required command '{contract.Name}' version {contract.Version} has no owner.";
            throw new InvalidOperationException(error);
        }
    }
}
