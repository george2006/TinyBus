using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TinyBus;

namespace TinyBus.PostgreSql.Persistence.Queries;

internal sealed class ReadCommandOwner
{
    private const string Statement = """
        SELECT service_name
        FROM tinybus.command_owners
        WHERE contract_name = @contractName
          AND contract_version = @contractVersion;
        """;

    internal async ValueTask<ServiceIdentity?> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = Statement;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var serviceName = result as string;

        if (string.IsNullOrEmpty(serviceName))
        {
            return null;
        }

        var owner = new ServiceIdentity(serviceName);

        return owner;
    }
}
