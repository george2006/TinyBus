using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TinyBus;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTransport : ITransport
{
    private readonly PostgreSqlMigrator migrator;
    private readonly PostgreSqlTopologyReconciler topologyReconciler;
    private readonly PostgreSqlCommandRouteSource routeSource;
    private readonly IReadOnlyCollection<ContractIdentity> requiredContracts;
    private readonly CommandRouteCache routeCache;
    private bool initialized;

    internal PostgreSqlTransport(
        string connectionString,
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CommandRouteCache routeCache)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(requiredContracts);
        ArgumentNullException.ThrowIfNull(routeCache);

        migrator = new PostgreSqlMigrator(connectionString);
        topologyReconciler = new PostgreSqlTopologyReconciler(connectionString);
        routeSource = new PostgreSqlCommandRouteSource(connectionString);
        this.requiredContracts = new List<ContractIdentity>(requiredContracts);
        this.routeCache = routeCache;
    }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ThrowIfInitialized();

        await migrator.MigrateAsync(cancellationToken);
        await topologyReconciler.ReconcileAsync(topology, cancellationToken);

        var routes = await routeSource.LoadAsync(requiredContracts, cancellationToken);
        ValidateRequiredRoutes(routes);

        routeCache.Replace(routes, cancellationToken);
        initialized = true;
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

            var message = $"Required command '{contract.Name}' version {contract.Version} has no owner.";
            var exception = new InvalidOperationException(message);

            throw exception;
        }
    }

    private void ThrowIfInitialized()
    {
        if (initialized)
        {
            throw new InvalidOperationException("The PostgreSQL transport is already initialized.");
        }
    }
}
