using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace TinyBus.PostgreSql.Persistence.Commands;

internal sealed class DeadLetterCommandMessage
{
    private const string Statement = """
        WITH failed_message AS
        (
            DELETE FROM tinybus.command_messages
            WHERE sequence_id = @sequenceId
              AND claim_id = @claimId
            RETURNING
                sequence_id,
                message_id,
                destination_service,
                contract_name,
                contract_version,
                payload,
                correlation_id,
                causation_id,
                headers,
                enqueued_at_utc,
                failed_attempts + 1 AS failed_attempts
        )
        INSERT INTO tinybus.dead_lettered_command_messages
        (
            original_sequence_id,
            message_id,
            destination_service,
            contract_name,
            contract_version,
            payload,
            correlation_id,
            causation_id,
            headers,
            enqueued_at_utc,
            failed_attempts,
            error_type,
            error_message,
            error_details
        )
        SELECT
            sequence_id,
            message_id,
            destination_service,
            contract_name,
            contract_version,
            payload,
            correlation_id,
            causation_id,
            headers,
            enqueued_at_utc,
            failed_attempts,
            @errorType,
            @errorMessage,
            @errorDetails
        FROM failed_message
        RETURNING TRUE;
        """;

    private readonly string connectionString;

    internal DeadLetterCommandMessage(string connectionString)
    {
        this.connectionString = connectionString;
    }

    internal async ValueTask<bool> ExecuteAsync(
        long sequenceId,
        Guid claimId,
        Exception error,
        CancellationToken cancellationToken)
    {
        var errorType = error.GetType();
        var errorTypeName = errorType.FullName ?? errorType.Name;
        var errorMessage = error.Message;
        var errorDetails = error.ToString();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.Parameters.AddWithValue("sequenceId", sequenceId);
        command.Parameters.AddWithValue("claimId", claimId);
        command.Parameters.AddWithValue("errorType", errorTypeName);
        command.Parameters.AddWithValue("errorMessage", errorMessage);
        command.Parameters.AddWithValue("errorDetails", errorDetails);
        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is true;
    }
}
