using Npgsql;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlTransportTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlTransportTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Initialization_migrates_reconciles_and_publishes_required_routes()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var requiredContracts = new[] { capture.Contract };
        var cache = new CommandRouteCache();
        var transport = CreateTransport(requiredContracts, cache);

        await transport.InitializeAsync(topology);

        var found = cache.TryResolve(capture.Contract, out var owner);
        Assert.True(found);
        Assert.Equal(topology.Service, owner);
    }

    [Fact]
    public async Task Missing_required_route_fails_without_publishing_the_cache()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var topology = Topology("payments", capture);
        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var cache = new CommandRouteCache();
        var transport = CreateTransport(requiredContracts, cache);

        Task Initialize()
        {
            var initialization = transport.InitializeAsync(topology);
            var task = initialization.AsTask();

            return task;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Initialize);

        Assert.Equal(
            "Required command 'payments.refund' version 1 has no owner.",
            exception.Message);
        AssertCacheIsNotInitialized(cache, capture.Contract);

        var source = new PostgreSqlCommandRouteSource(postgreSql.ConnectionString);
        var persistedRoutes = await source.LoadAsync(requiredContracts);
        var persistedRoute = Assert.Single(persistedRoutes);
        Assert.Equal(capture.Contract, persistedRoute.Contract);
    }

    [Fact]
    public async Task Ownership_conflict_fails_before_publishing_the_cache()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var checkout = Topology("checkout", capture);
        var reconciler = new PostgreSqlTopologyReconciler(postgreSql.ConnectionString);
        await reconciler.ReconcileAsync(payments);
        var requiredContracts = new[] { capture.Contract };
        var cache = new CommandRouteCache();
        var transport = CreateTransport(requiredContracts, cache);

        Task Initialize()
        {
            var initialization = transport.InitializeAsync(checkout);
            var task = initialization.AsTask();

            return task;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Initialize);

        Assert.Equal(
            "Command 'payments.capture' version 1 has conflicting owners 'checkout' and 'payments'.",
            exception.Message);
        AssertCacheIsNotInitialized(cache, capture.Contract);
    }

    private PostgreSqlTransport CreateTransport(
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CommandRouteCache cache)
    {
        var transport = new PostgreSqlTransport(
            postgreSql.ConnectionString,
            requiredContracts,
            cache);

        return transport;
    }

    private async Task ResetDatabaseAsync()
    {
        await DropSchemaAsync();
        var migrator = new PostgreSqlMigrator(postgreSql.ConnectionString);
        await migrator.MigrateAsync(CancellationToken.None);
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    private static void AssertCacheIsNotInitialized(
        CommandRouteCache cache,
        ContractIdentity contract)
    {
        void Resolve()
        {
            cache.TryResolve(contract, out _);
        }

        var exception = Assert.Throws<InvalidOperationException>(Resolve);
        Assert.Equal("Command routes have not been initialized.", exception.Message);
    }

    private static ServiceTopology Topology(
        string serviceName,
        params MessageDescriptor[] messages)
    {
        var service = new ServiceIdentity(serviceName);
        var topology = new ServiceTopology(service, messages);

        return topology;
    }

    private static MessageDescriptor Command(string name)
    {
        var contract = new ContractIdentity(name, 1);
        var descriptor = new MessageDescriptor(
            contract,
            typeof(TestMessage),
            typeof(TestHandler),
            MessageKind.Command);

        return descriptor;
    }

    private sealed record TestMessage;

    private sealed class TestHandler;
}
