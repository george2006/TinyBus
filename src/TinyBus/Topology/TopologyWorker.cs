using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace TinyBus;

internal sealed class TopologyWorker : BackgroundService
{
    private readonly ITopologyReconciler reconciler;
    private readonly ICommandRouteSource routeSource;
    private readonly ServiceTopology serviceTopology;
    private readonly IReadOnlyCollection<ContractIdentity> requiredContracts;
    private readonly CommandRouteCache routeCache;

    public TopologyWorker(
        ITopologyReconciler reconciler,
        ICommandRouteSource routeSource,
        ServiceTopology serviceTopology,
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CommandRouteCache routeCache)
    {
        ArgumentNullException.ThrowIfNull(reconciler);
        ArgumentNullException.ThrowIfNull(routeSource);
        ArgumentNullException.ThrowIfNull(serviceTopology);
        ArgumentNullException.ThrowIfNull(requiredContracts);
        ArgumentNullException.ThrowIfNull(routeCache);

        this.reconciler = reconciler;
        this.routeSource = routeSource;
        this.serviceTopology = serviceTopology;
        this.requiredContracts = new List<ContractIdentity>(requiredContracts);
        this.routeCache = routeCache;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var reconciliation = reconciler.ReconcileAsync(serviceTopology, cancellationToken);
        await reconciliation.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var loading = routeSource.LoadAsync(requiredContracts, cancellationToken);
        var routes = await loading.ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        ValidateRequiredRoutes(routes);

        routeCache.Replace(routes, cancellationToken);

        var starting = base.StartAsync(cancellationToken);
        await starting.ConfigureAwait(false);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Background refresh is deferred; initial loading belongs exclusively to StartAsync.
        return Task.CompletedTask;
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
