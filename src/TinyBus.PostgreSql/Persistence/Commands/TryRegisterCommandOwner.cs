using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TinyBus;

namespace TinyBus.PostgreSql.Persistence.Commands;

internal sealed class TryRegisterCommandOwner
{
    private const string Statement = """
        INSERT INTO tinybus.command_owners
            (contract_name, contract_version, service_name)
        VALUES
            (@contractName, @contractVersion, @serviceName)
        ON CONFLICT (contract_name, contract_version) DO NOTHING;
        """;

    internal async ValueTask<bool> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        ServiceIdentity owner,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Statement;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        command.Parameters.AddWithValue("serviceName", owner.Value);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }
}
