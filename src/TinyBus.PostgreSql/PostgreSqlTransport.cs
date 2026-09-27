using System;
using System.Threading;
using System.Threading.Tasks;
using TinyBus;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTransport : ITransport
{
    private readonly PostgreSqlMigrator migrator;
    private readonly PostgreSqlTopologyReconciler topologyReconciler;
    private bool initialized;

    internal PostgreSqlTransport(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        migrator = new PostgreSqlMigrator(connectionString);
        topologyReconciler = new PostgreSqlTopologyReconciler(connectionString);
    }

    public async ValueTask InitializeAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);
        ThrowIfInitialized();

        await migrator.MigrateAsync(cancellationToken);
        await topologyReconciler.ReconcileAsync(topology, cancellationToken);

        initialized = true;
    }

    private void ThrowIfInitialized()
    {
        if (initialized)
        {
            throw new InvalidOperationException("The PostgreSQL transport is already initialized.");
        }
    }
}
