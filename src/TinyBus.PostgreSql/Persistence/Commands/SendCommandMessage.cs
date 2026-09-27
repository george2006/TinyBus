using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using TinyBus;

namespace TinyBus.PostgreSql.Persistence.Commands;

internal sealed class SendCommandMessage
{
    private const string Statement = """
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

    private readonly string connectionString;

    internal SendCommandMessage(string connectionString)
    {
        this.connectionString = connectionString;
    }

    internal async ValueTask<bool> ExecuteAsync(
        MessageEnvelope message,
        CancellationToken cancellationToken)
    {
        var headers = SerializeHeaders(message.Headers);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(connection, message, headers);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        MessageEnvelope message,
        string? headers)
    {
        var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.Parameters.AddWithValue("messageId", message.MessageId);
        command.Parameters.AddWithValue("contractName", message.Contract.Name);
        command.Parameters.AddWithValue("contractVersion", message.Contract.Version);
        command.Parameters.AddWithValue("payload", message.Payload);
        AddNullableText(command, "correlationId", message.CorrelationId);
        AddNullableText(command, "causationId", message.CausationId);
        AddNullableJson(command, "headers", headers);

        return command;
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
