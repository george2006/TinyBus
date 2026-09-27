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
    public async Task Initialization_migrates_and_reconciles_before_completing()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var transport = CreateTransport();

        await transport.InitializeAsync(topology);

        var source = new PostgreSqlCommandRouteSource(postgreSql.ConnectionString);
        var requiredContracts = new[] { capture.Contract };
        var persistedRoutes = await source.LoadAsync(requiredContracts);
        var persistedRoute = Assert.Single(persistedRoutes);
        Assert.Equal(capture.Contract, persistedRoute.Contract);
        Assert.Equal(topology.Service, persistedRoute.Service);
    }

    [Fact]
    public async Task Ownership_conflict_fails_initialization()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var checkout = Topology("checkout", capture);
        var reconciler = new PostgreSqlTopologyReconciler(postgreSql.ConnectionString);
        await reconciler.ReconcileAsync(payments);
        var transport = CreateTransport();

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
    }

    private PostgreSqlTransport CreateTransport()
    {
        var transport = new PostgreSqlTransport(postgreSql.ConnectionString);

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
