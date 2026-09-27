using Npgsql;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlCommandRouteSourceTests
    : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlCommandRouteSourceTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Loads_only_distinct_required_command_routes()
    {
        await ResetDatabaseAsync();
        var captureV1 = Command("payments.capture", 1);
        var captureV2 = Command("payments.capture", 2);
        var refund = Command("payments.refund", 1);
        var payments = Topology("payments", captureV1, refund);
        var upgradedPayments = Topology("payments-v2", captureV2);
        var reconciler = CreateReconciler();
        await reconciler.ReconcileAsync(payments);
        await reconciler.ReconcileAsync(upgradedPayments);
        var requiredContracts = new[]
        {
            captureV2.Contract,
            captureV1.Contract,
            captureV1.Contract
        };
        var source = CreateSource();

        var routes = await source.LoadAsync(requiredContracts);

        var captureV1Route = new CommandRoute(captureV1.Contract, payments.Service);
        var captureV2Route = new CommandRoute(
            captureV2.Contract,
            upgradedPayments.Service);
        var refundRoute = new CommandRoute(refund.Contract, payments.Service);
        Assert.Equal(2, routes.Count);
        Assert.Contains(captureV1Route, routes);
        Assert.Contains(captureV2Route, routes);
        Assert.DoesNotContain(refundRoute, routes);
    }

    [Fact]
    public async Task Missing_required_contract_is_not_reported_as_a_route()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture", 1);
        var refund = Command("payments.refund", 1);
        var payments = Topology("payments", capture);
        var reconciler = CreateReconciler();
        await reconciler.ReconcileAsync(payments);
        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var source = CreateSource();

        var routes = await source.LoadAsync(requiredContracts);

        var route = Assert.Single(routes);
        var expectedRoute = new CommandRoute(capture.Contract, payments.Service);
        Assert.Equal(expectedRoute, route);
    }

    [Fact]
    public async Task Empty_requirements_do_not_open_a_database_connection()
    {
        var source = new PostgreSqlCommandRouteSource(
            "Host=invalid;Database=invalid;Username=invalid;Password=invalid");
        var requiredContracts = Array.Empty<ContractIdentity>();

        var routes = await source.LoadAsync(requiredContracts);

        Assert.Empty(routes);
    }

    [Fact]
    public async Task Cancellation_before_loading_does_not_open_a_database_connection()
    {
        var source = new PostgreSqlCommandRouteSource(
            "Host=invalid;Database=invalid;Username=invalid;Password=invalid");
        var capture = new ContractIdentity("payments.capture", 1);
        var requiredContracts = new[] { capture };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Task Load()
        {
            var loading = source.LoadAsync(requiredContracts, cancellation.Token);

            return loading;
        }

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(Load);

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    private PostgreSqlTopologyReconciler CreateReconciler()
    {
        var reconciler = new PostgreSqlTopologyReconciler(postgreSql.ConnectionString);

        return reconciler;
    }

    private PostgreSqlCommandRouteSource CreateSource()
    {
        var source = new PostgreSqlCommandRouteSource(postgreSql.ConnectionString);

        return source;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();

        var migrator = new PostgreSqlMigrator(postgreSql.ConnectionString);
        await migrator.MigrateAsync(CancellationToken.None);
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

    private static MessageDescriptor Command(string name, int version)
    {
        var contract = new ContractIdentity(name, version);
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
