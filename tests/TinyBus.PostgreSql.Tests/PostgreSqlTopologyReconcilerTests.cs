using Npgsql;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlTopologyReconcilerTests
    : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlTopologyReconcilerTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Older_replica_confirms_existing_commands_without_removing_newer_commands()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var newerReplica = Topology("payments", capture, refund);
        var olderReplica = Topology("payments", capture);
        var reconciler = CreateReconciler();

        await reconciler.ReconcileAsync(newerReplica);
        await reconciler.ReconcileAsync(olderReplica);
        var owners = await ReadOwnersAsync();

        Assert.Equal(2, owners.Count);
        Assert.Contains(new StoredCommandOwner("payments.capture", 1, "payments"), owners);
        Assert.Contains(new StoredCommandOwner("payments.refund", 1, "payments"), owners);
    }

    [Fact]
    public async Task Conflicting_owner_is_rejected_and_the_original_owner_persists()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var checkout = Topology("checkout", capture);
        var firstReconciler = CreateReconciler();
        await firstReconciler.ReconcileAsync(payments);
        var recreatedReconciler = CreateReconciler();

        Task ReconcileCheckout()
        {
            var reconciliation = recreatedReconciler.ReconcileAsync(checkout);

            return reconciliation;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(ReconcileCheckout);
        var owners = await ReadOwnersAsync();

        Assert.Equal(
            "Command 'payments.capture' version 1 has conflicting owners 'checkout' and 'payments'.",
            exception.Message);
        var owner = Assert.Single(owners);
        Assert.Equal(new StoredCommandOwner("payments.capture", 1, "payments"), owner);
    }

    [Fact]
    public async Task Conflicting_topology_is_rejected_without_applying_its_other_commands()
    {
        await ResetDatabaseAsync();
        var existing = Command("z.existing");
        var unowned = Command("a.unowned");
        var payments = Topology("payments", existing);
        var checkout = Topology("checkout", unowned, existing);
        var reconciler = CreateReconciler();
        await reconciler.ReconcileAsync(payments);

        Task ReconcileCheckout()
        {
            var reconciliation = reconciler.ReconcileAsync(checkout);

            return reconciliation;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(ReconcileCheckout);
        var owners = await ReadOwnersAsync();

        var owner = Assert.Single(owners);
        Assert.Equal(new StoredCommandOwner("z.existing", 1, "payments"), owner);
    }

    [Fact]
    public async Task Concurrent_claims_choose_one_owner_and_reject_the_other()
    {
        await ResetDatabaseAsync();
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var checkout = Topology("checkout", capture);
        var firstReconciler = CreateReconciler();
        var secondReconciler = CreateReconciler();

        var firstClaim = firstReconciler.ReconcileAsync(payments);
        var secondClaim = secondReconciler.ReconcileAsync(checkout);
        var firstOutcome = ObserveAsync(firstClaim);
        var secondOutcome = ObserveAsync(secondClaim);
        var outcomes = await Task.WhenAll(firstOutcome, secondOutcome);
        var owners = await ReadOwnersAsync();

        Assert.Single(outcomes, outcome => outcome is null);
        var rejection = Assert.Single(outcomes, outcome => outcome is not null);
        Assert.IsType<InvalidOperationException>(rejection);
        var owner = Assert.Single(owners);
        var possibleOwners = new[] { "checkout", "payments" };
        Assert.Contains(owner.ServiceName, possibleOwners);
    }

    private PostgreSqlTopologyReconciler CreateReconciler()
    {
        var reconciler = new PostgreSqlTopologyReconciler(postgreSql.ConnectionString);

        return reconciler;
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

    private async Task<IReadOnlyList<StoredCommandOwner>> ReadOwnersAsync()
    {
        var owners = new List<StoredCommandOwner>();
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT contract_name, contract_version, service_name
            FROM tinybus.command_owners
            ORDER BY contract_name, contract_version;
            """;
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var contractName = reader.GetString(0);
            var contractVersion = reader.GetInt32(1);
            var serviceName = reader.GetString(2);
            var owner = new StoredCommandOwner(
                contractName,
                contractVersion,
                serviceName);
            owners.Add(owner);
        }

        return owners;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    private static async Task<Exception?> ObserveAsync(Task reconciliation)
    {
        try
        {
            await reconciliation;
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
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

    private sealed record StoredCommandOwner(
        string ContractName,
        int ContractVersion,
        string ServiceName);

    private sealed record TestMessage;

    private sealed class TestHandler;
}
