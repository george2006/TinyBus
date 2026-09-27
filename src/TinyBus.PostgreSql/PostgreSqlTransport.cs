using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using TinyBus;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTransport : ITransport
{
    private readonly string connectionString;
    private readonly PostgreSqlMigrator migrator;
    private readonly PostgreSqlTopologyReconciler topologyReconciler;
    private bool initialized;

    internal PostgreSqlTransport(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
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

    public async ValueTask SendAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ThrowIfNotInitialized();

        var contract = message.Contract;
        var headers = SerializeHeaders(message.Headers);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateSendCommand(connection, message, headers);
        var accepted = await command.ExecuteNonQueryAsync(cancellationToken);

        if (accepted == 0)
        {
            var error = $"Command '{contract.Name}' version {contract.Version} has no owner.";
            var exception = new InvalidOperationException(error);

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

    private void ThrowIfNotInitialized()
    {
        if (!initialized)
        {
            throw new InvalidOperationException("The PostgreSQL transport has not been initialized.");
        }
    }

    private static string? SerializeHeaders(
        IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
        {
            return null;
        }

        var serializedHeaders = JsonSerializer.Serialize(headers);

        return serializedHeaders;
    }

    private static NpgsqlCommand CreateSendCommand(
        NpgsqlConnection connection,
        MessageEnvelope message,
        string? headers)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO tinybus.command_messages
            (
                message_id,
                destination_service,
                contract_name,
                contract_version,
                payload,
                correlation_id,
                causation_id,
                headers
            )
            SELECT
                @messageId,
                owners.service_name,
                @contractName,
                @contractVersion,
                @payload,
                @correlationId,
                @causationId,
                @headers
            FROM tinybus.command_owners AS owners
            WHERE owners.contract_name = @contractName
              AND owners.contract_version = @contractVersion;
            """;
        command.Parameters.AddWithValue("messageId", message.MessageId);
        command.Parameters.AddWithValue("contractName", message.Contract.Name);
        command.Parameters.AddWithValue("contractVersion", message.Contract.Version);
        command.Parameters.AddWithValue("payload", message.Payload);
        AddNullableText(command, "correlationId", message.CorrelationId);
        AddNullableText(command, "causationId", message.CausationId);
        AddNullableJson(command, "headers", headers);

        return command;
    }

    private static void AddNullableText(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Text);
        parameter.Value = value is null ? DBNull.Value : value;
    }

    private static void AddNullableJson(
        NpgsqlCommand command,
        string name,
        string? value)
    {
        var parameter = command.Parameters.Add(name, NpgsqlDbType.Jsonb);
        parameter.Value = value is null ? DBNull.Value : value;
    }
}
