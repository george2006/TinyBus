using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace TinyBus.PostgreSql.Persistence.Commands;

internal sealed class CompleteCommandMessage
{
    private const string Statement = """
        DELETE FROM tinybus.command_messages
        WHERE sequence_id = @sequenceId
          AND claim_id = @claimId;
        """;

    private readonly string connectionString;

    internal CompleteCommandMessage(string connectionString)
    {
        this.connectionString = connectionString;
    }

    internal async ValueTask<bool> ExecuteAsync(
        long sequenceId,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Statement;
        command.Parameters.AddWithValue("sequenceId", sequenceId);
        command.Parameters.AddWithValue("claimId", claimId);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }
}
