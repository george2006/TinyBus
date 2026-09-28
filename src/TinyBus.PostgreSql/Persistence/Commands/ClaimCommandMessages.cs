using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using NpgsqlTypes;
using TinyBus;

namespace TinyBus.PostgreSql.Persistence.Commands;

internal sealed class ClaimCommandMessages
{
    private const string Statement = """
        WITH available_messages AS
        (
            SELECT sequence_id
            FROM tinybus.command_messages
            WHERE destination_service = @serviceName
              AND available_at_utc <= CURRENT_TIMESTAMP
              AND
              (
                  claim_id IS NULL
                  OR claimed_until_utc <= CURRENT_TIMESTAMP
              )
            ORDER BY sequence_id
            LIMIT @maximumCount
            FOR UPDATE SKIP LOCKED
        )
        UPDATE tinybus.command_messages AS messages
        SET claim_id = @claimId,
            claimed_until_utc = CURRENT_TIMESTAMP + @leaseDuration
        FROM available_messages
        WHERE messages.sequence_id = available_messages.sequence_id
        RETURNING
            messages.sequence_id,
            messages.message_id,
            messages.contract_name,
            messages.contract_version,
            messages.payload,
            messages.correlation_id,
            messages.causation_id,
            messages.headers::text,
            messages.failed_attempts + 1;
        """;

    private readonly string connectionString;

    internal ClaimCommandMessages(string connectionString)
    {
        this.connectionString = connectionString;
    }

    internal async ValueTask<IReadOnlyList<ClaimedCommandMessage>> ExecuteAsync(
        string serviceName,
        int maximumCount,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var claimId = Guid.NewGuid();
        var claimedMessages = new List<ClaimedCommandMessage>(maximumCount);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = CreateCommand(
            connection,
            serviceName,
            maximumCount,
            leaseDuration,
            claimId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var claimedMessage = ReadMessage(reader, claimId);
            claimedMessages.Add(claimedMessage);
        }

        return claimedMessages;
    }

    private static NpgsqlCommand CreateCommand(
        NpgsqlConnection connection,
        string serviceName,
        int maximumCount,
        TimeSpan leaseDuration,
        Guid claimId)
    {
        var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.Parameters.AddWithValue("serviceName", serviceName);
        command.Parameters.AddWithValue("maximumCount", maximumCount);
        command.Parameters.AddWithValue("claimId", NpgsqlDbType.Uuid, claimId);
        command.Parameters.AddWithValue("leaseDuration", NpgsqlDbType.Interval, leaseDuration);

        return command;
    }

    private static ClaimedCommandMessage ReadMessage(
        NpgsqlDataReader reader,
        Guid claimId)
    {
        var sequenceId = reader.GetInt64(0);
        var messageId = reader.GetGuid(1);
        var contractName = reader.GetString(2);
        var contractVersion = reader.GetInt32(3);
        var payload = reader.GetString(4);
        var correlationId = ReadNullableString(reader, 5);
        var causationId = ReadNullableString(reader, 6);
        var headers = ReadHeaders(reader, 7);
        var attempt = reader.GetInt32(8);
        var contract = new ContractIdentity(contractName, contractVersion);
        var envelope = new MessageEnvelope(
            messageId,
            contract,
            payload,
            correlationId,
            causationId,
            headers);
        var claimedMessage = new ClaimedCommandMessage(
            sequenceId,
            claimId,
            attempt,
            envelope);

        return claimedMessage;
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var value = reader.GetString(ordinal);

        return value;
    }

    private static IReadOnlyDictionary<string, string>? ReadHeaders(
        NpgsqlDataReader reader,
        int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        var serializedHeaders = reader.GetString(ordinal);
        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);

        return headers;
    }
}
