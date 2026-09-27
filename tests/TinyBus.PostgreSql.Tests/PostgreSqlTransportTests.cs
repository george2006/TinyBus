using System.Text.Json;
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

        var owner = await ReadCommandOwnerAsync(capture.Contract);

        Assert.Equal(topology.Service, owner);
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

    [Fact]
    public async Task Send_persists_the_envelope_for_the_command_owner()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var messageId = Guid.NewGuid();
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var envelope = new MessageEnvelope(
            messageId,
            capture.Contract,
            "{\"amount\":100}",
            "correlation-1",
            "causation-1",
            headers);

        await transport.SendAsync(envelope);

        var stored = await ReadStoredCommandAsync();
        var storedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(stored.Headers);

        Assert.Equal(messageId, stored.MessageId);
        Assert.Equal("payments", stored.DestinationService);
        Assert.Equal(capture.Contract, stored.Contract);
        Assert.Equal(envelope.Payload, stored.Payload);
        Assert.Equal(envelope.CorrelationId, stored.CorrelationId);
        Assert.Equal(envelope.CausationId, stored.CausationId);
        Assert.NotNull(storedHeaders);
        Assert.Equal("north", storedHeaders["tenant"]);
    }

    [Fact]
    public async Task Send_rejects_a_command_without_an_owner()
    {
        await DropSchemaAsync();
        var topology = Topology("payments");
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var contract = new ContractIdentity("payments.capture", 1);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(messageId, contract, "{}");

        Task Send()
        {
            var sending = transport.SendAsync(envelope);
            var task = sending.AsTask();

            return task;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(Send);

        Assert.Equal(
            "Command 'payments.capture' version 1 has no owner.",
            exception.Message);
    }

    [Fact]
    public async Task Receive_claims_only_available_capacity_and_complete_removes_the_delivery()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var first = new MessageEnvelope(
            Guid.NewGuid(),
            capture.Contract,
            "{\"number\":1}",
            "correlation-1",
            "causation-1",
            headers);
        var second = new MessageEnvelope(Guid.NewGuid(), capture.Contract, "{\"number\":2}");
        await transport.SendAsync(first);
        await transport.SendAsync(second);
        var capacity = new ReceiveCapacity(maximum: 8, available: 1);

        var firstBatch = await transport.ReceiveAsync(capacity);

        var firstDelivery = Assert.Single(firstBatch);
        AssertEnvelope(first, firstDelivery.Envelope);
        await firstDelivery.CompleteAsync();

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        AssertEnvelope(second, secondDelivery.Envelope);
        await secondDelivery.CompleteAsync();

        var storedCount = await ReadStoredCommandCountAsync();
        Assert.Equal(0, storedCount);
    }

    [Fact]
    public async Task Abandon_makes_the_delivery_available_again()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var envelope = new MessageEnvelope(Guid.NewGuid(), capture.Contract, "{}");
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);

        var firstBatch = await transport.ReceiveAsync(capacity);
        var firstDelivery = Assert.Single(firstBatch);
        await firstDelivery.AbandonAsync();

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        AssertEnvelope(envelope, secondDelivery.Envelope);
        await secondDelivery.CompleteAsync();
    }

    [Fact]
    public async Task Receive_waits_for_work_and_observes_cancellation()
    {
        await DropSchemaAsync();
        var topology = Topology("payments");
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var capacity = new ReceiveCapacity(maximum: 4, available: 4);
        using var cancellation = new CancellationTokenSource();

        var receiving = transport.ReceiveAsync(capacity, cancellation.Token).AsTask();

        Assert.False(receiving.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
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

    private async Task<StoredCommand> ReadStoredCommandAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                message_id,
                destination_service,
                contract_name,
                contract_version,
                payload,
                correlation_id,
                causation_id,
                headers::text
            FROM tinybus.command_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var contractName = reader.GetString(2);
        var contractVersion = reader.GetInt32(3);
        var contract = new ContractIdentity(contractName, contractVersion);
        var messageId = reader.GetGuid(0);
        var destinationService = reader.GetString(1);
        var payload = reader.GetString(4);
        var correlationId = reader.GetString(5);
        var causationId = reader.GetString(6);
        var headers = reader.GetString(7);
        var stored = new StoredCommand(
            messageId,
            destinationService,
            contract,
            payload,
            correlationId,
            causationId,
            headers);

        return stored;
    }

    private async Task<long> ReadStoredCommandCountAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM tinybus.command_messages;";
        var result = await command.ExecuteScalarAsync();
        var count = Assert.IsType<long>(result);

        return count;
    }

    private async Task<ServiceIdentity> ReadCommandOwnerAsync(ContractIdentity contract)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_name
            FROM tinybus.command_owners
            WHERE contract_name = @contractName
              AND contract_version = @contractVersion;
            """;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        var result = await command.ExecuteScalarAsync();
        var serviceName = Assert.IsType<string>(result);
        var service = new ServiceIdentity(serviceName);

        return service;
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

    private static void AssertEnvelope(
        MessageEnvelope expected,
        MessageEnvelope actual)
    {
        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.Contract, actual.Contract);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(expected.CausationId, actual.CausationId);

        if (expected.Headers is null)
        {
            Assert.Null(actual.Headers);
            return;
        }

        Assert.NotNull(actual.Headers);
        Assert.Equal(expected.Headers, actual.Headers);
    }

    private sealed record TestMessage;

    private sealed class TestHandler;

    private sealed record StoredCommand(
        Guid MessageId,
        string DestinationService,
        ContractIdentity Contract,
        string Payload,
        string CorrelationId,
        string CausationId,
        string Headers);
}
