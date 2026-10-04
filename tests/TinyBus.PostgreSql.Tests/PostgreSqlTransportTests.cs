using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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
        Assert.Equal(1, firstDelivery.Attempt);
        AssertEnvelope(first, firstDelivery.ReadEnvelope());
        await firstDelivery.CompleteAsync();

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        Assert.Equal(1, secondDelivery.Attempt);
        AssertEnvelope(second, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();

        var storedCount = await ReadStoredCommandCountAsync();
        Assert.Equal(0, storedCount);
    }

    [Fact]
    public async Task Receive_uses_the_configured_command_lease_duration()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var commandLeaseDuration = TimeSpan.FromMinutes(2);
        var transport = CreateTransport(commandLeaseDuration: commandLeaseDuration);
        await transport.InitializeAsync(topology);
        var envelope = new MessageEnvelope(Guid.NewGuid(), capture.Contract, "{}");
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 1, available: 1);

        var batch = await transport.ReceiveAsync(capacity);

        var delivery = Assert.Single(batch);
        var remainingLeaseDuration = await ReadRemainingLeaseDurationAsync();
        var minimumExpectedDuration = TimeSpan.FromMinutes(1);
        Assert.InRange(
            remainingLeaseDuration,
            minimumExpectedDuration,
            commandLeaseDuration);
        await delivery.CompleteAsync();
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
        Assert.Equal(1, secondDelivery.Attempt);
        AssertEnvelope(envelope, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();
    }

    [Fact]
    public async Task Scheduled_retry_becomes_available_as_the_next_attempt()
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
        Assert.Equal(1, firstDelivery.Attempt);
        var processingError = new InvalidOperationException("Handler failed.");
        var retryDelay = TimeSpan.FromSeconds(1);

        await firstDelivery.ScheduleRetryAsync(processingError, retryDelay);

        var storedRetry = await ReadStoredRetryAsync();
        Assert.Equal(1, storedRetry.FailedAttempts);
        Assert.True(storedRetry.AvailableAtUtc > DateTimeOffset.UtcNow);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var secondBatch = await transport.ReceiveAsync(capacity, cancellation.Token);
        var secondDelivery = Assert.Single(secondBatch);
        Assert.Equal(2, secondDelivery.Attempt);
        AssertEnvelope(envelope, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();
    }

    [Fact]
    public async Task Dead_letter_moves_the_delivery_and_preserves_its_failure()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var envelope = new MessageEnvelope(
            Guid.NewGuid(),
            capture.Contract,
            "{\"amount\":100}",
            "correlation-1",
            "causation-1",
            headers);
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);
        var firstBatch = await transport.ReceiveAsync(capacity);
        var firstDelivery = Assert.Single(firstBatch);
        var firstError = new InvalidOperationException("First failure.");

        await firstDelivery.ScheduleRetryAsync(firstError, TimeSpan.Zero);

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        var finalError = new InvalidOperationException("Handler kept failing.");

        await secondDelivery.DeadLetterAsync(finalError);

        var activeCount = await ReadStoredCommandCountAsync();
        var deadLetter = await ReadDeadLetterAsync();
        var expectedErrorType = typeof(InvalidOperationException).FullName;

        Assert.Equal(0, activeCount);
        Assert.Equal("payments", deadLetter.DestinationService);
        AssertEnvelope(envelope, deadLetter.Envelope);
        Assert.Equal(2, deadLetter.FailedAttempts);
        Assert.True(deadLetter.DeadLetteredAtUtc >= deadLetter.EnqueuedAtUtc);
        Assert.Equal(expectedErrorType, deadLetter.Failure.Type);
        Assert.Equal(finalError.Message, deadLetter.Failure.Message);
        Assert.Contains(finalError.Message, deadLetter.Failure.Details);
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

    [Fact]
    public async Task Receive_resumes_after_a_transient_provider_outage()
    {
        await DropSchemaAsync();
        var capture = Command("payments.capture");
        var topology = Topology("payments", capture);
        var connectionString = ReadFastFailingConnectionString();
        var transport = CreateTransport(connectionString);
        await transport.InitializeAsync(topology);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);
        var scenarioTimeout = TimeSpan.FromSeconds(30);
        using var cancellation = new CancellationTokenSource(scenarioTimeout);
        var receiving = transport.ReceiveAsync(capacity, cancellation.Token).AsTask();

        await postgreSql.PauseProviderAsync();

        try
        {
            var outageObservationDelay = TimeSpan.FromSeconds(3);
            await Task.Delay(outageObservationDelay, cancellation.Token);

            Assert.False(receiving.IsCompleted);
        }
        finally
        {
            await postgreSql.ResumeProviderAsync();
        }

        await WaitForProviderAsync(connectionString, cancellation.Token);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(messageId, capture.Contract, "{}");
        await transport.SendAsync(envelope, cancellation.Token);

        var deliveries = await receiving;
        var delivery = Assert.Single(deliveries);
        AssertEnvelope(envelope, delivery.ReadEnvelope());
        await delivery.CompleteAsync(cancellation.Token);
    }

    [Fact]
    public async Task Receive_propagates_a_nontransient_provider_failure()
    {
        await DropSchemaAsync();
        var topology = Topology("payments");
        var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        await DropCommandMessagesTableAsync();
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);

        Task Receive()
        {
            var receiving = transport.ReceiveAsync(capacity);
            var task = receiving.AsTask();

            return task;
        }

        var exception = await Assert.ThrowsAsync<PostgresException>(Receive);

        Assert.Equal(PostgresErrorCodes.UndefinedTable, exception.SqlState);
    }

    private PostgreSqlTransport CreateTransport(
        string? connectionString = null,
        TimeSpan? commandLeaseDuration = null)
    {
        var selectedConnectionString = connectionString ?? postgreSql.ConnectionString;
        var postgreSqlOptions = new PostgreSqlOptions();
        var selectedLeaseDuration =
            commandLeaseDuration ?? postgreSqlOptions.CommandLeaseDuration;
        var logger = NullLogger<PostgreSqlTransport>.Instance;
        var transport = new PostgreSqlTransport(
            selectedConnectionString,
            selectedLeaseDuration,
            logger);

        return transport;
    }

    private string ReadFastFailingConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(postgreSql.ConnectionString)
        {
            Timeout = 1
        };
        var connectionString = builder.ConnectionString;

        return connectionString;
    }

    private static async Task WaitForProviderAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(200);

        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                return;
            }
            catch (NpgsqlException exception) when (
                exception.IsTransient && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
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

    private async Task DropCommandMessagesTableAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP TABLE tinybus.command_messages;";
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

    private async Task<TimeSpan> ReadRemainingLeaseDurationAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT claimed_until_utc - CURRENT_TIMESTAMP FROM tinybus.command_messages;";
        var result = await command.ExecuteScalarAsync();
        var remainingLeaseDuration = Assert.IsType<TimeSpan>(result);

        return remainingLeaseDuration;
    }

    private async Task<StoredRetry> ReadStoredRetryAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT failed_attempts, available_at_utc
            FROM tinybus.command_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var failedAttempts = reader.GetInt32(0);
        var availableAtUtc = reader.GetFieldValue<DateTimeOffset>(1);
        var retry = new StoredRetry(failedAttempts, availableAtUtc);

        return retry;
    }

    private async Task<StoredDeadLetter> ReadDeadLetterAsync()
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
                headers::text,
                enqueued_at_utc,
                failed_attempts,
                dead_lettered_at_utc,
                error_type,
                error_message,
                error_details
            FROM tinybus.dead_lettered_command_messages;
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
        var serializedHeaders = reader.GetString(7);
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);
        var enqueuedAtUtc = reader.GetFieldValue<DateTimeOffset>(8);
        var failedAttempts = reader.GetInt32(9);
        var deadLetteredAtUtc = reader.GetFieldValue<DateTimeOffset>(10);
        var errorType = reader.GetString(11);
        var errorMessage = reader.GetString(12);
        var errorDetails = reader.GetString(13);
        var envelope = new MessageEnvelope(
            messageId,
            contract,
            payload,
            correlationId,
            causationId,
            headers);
        var failure = new StoredFailure(errorType, errorMessage, errorDetails);
        var deadLetter = new StoredDeadLetter(
            envelope,
            destinationService,
            enqueuedAtUtc,
            failedAttempts,
            deadLetteredAtUtc,
            failure);

        return deadLetter;
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

    private sealed record StoredRetry(
        int FailedAttempts,
        DateTimeOffset AvailableAtUtc);

    private sealed record StoredDeadLetter(
        MessageEnvelope Envelope,
        string DestinationService,
        DateTimeOffset EnqueuedAtUtc,
        int FailedAttempts,
        DateTimeOffset DeadLetteredAtUtc,
        StoredFailure Failure);

    private sealed record StoredFailure(
        string Type,
        string Message,
        string Details);
}
