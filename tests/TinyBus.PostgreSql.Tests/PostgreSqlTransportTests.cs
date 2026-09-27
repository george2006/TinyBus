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
