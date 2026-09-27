using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TinyBus;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTopologyReconciler
{
    private readonly string connectionString;

    internal PostgreSqlTopologyReconciler(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
    }

    internal async Task ReconcileAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var commands = ReadCommands(topology);
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var command in commands)
            {
                await ConfirmOwnerAsync(
                    connection,
                    transaction,
                    command,
                    topology.Service,
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static IReadOnlyList<ContractIdentity> ReadCommands(
        ServiceTopology topology)
    {
        var uniqueCommands = new HashSet<ContractIdentity>();
        var commands = new List<ContractIdentity>();

        foreach (var message in topology.Messages)
        {
            if (message.Kind != MessageKind.Command)
            {
                continue;
            }

            if (uniqueCommands.Add(message.Contract))
            {
                commands.Add(message.Contract);
            }
        }

        return commands;
    }

    private static async Task ConfirmOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        ServiceIdentity candidateOwner,
        CancellationToken cancellationToken)
    {
        var inserted = await TryInsertOwnerAsync(
            connection,
            transaction,
            contract,
            candidateOwner,
            cancellationToken);

        if (inserted)
        {
            return;
        }

        var existingOwner = await ReadOwnerAsync(
            connection,
            transaction,
            contract,
            cancellationToken);

        if (existingOwner == candidateOwner)
        {
            return;
        }

        ThrowOwnershipConflict(contract, existingOwner, candidateOwner);
    }

    private static async Task<bool> TryInsertOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        ServiceIdentity owner,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tinybus.command_owners
                (contract_name, contract_version, service_name)
            VALUES
                (@contractName, @contractVersion, @serviceName)
            ON CONFLICT (contract_name, contract_version) DO NOTHING;
            """;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        command.Parameters.AddWithValue("serviceName", owner.Value);
        var affectedRows = await command.ExecuteNonQueryAsync(cancellationToken);

        return affectedRows == 1;
    }

    private static async Task<ServiceIdentity> ReadOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT service_name
            FROM tinybus.command_owners
            WHERE contract_name = @contractName
              AND contract_version = @contractVersion;
            """;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var serviceName = result as string;

        if (string.IsNullOrEmpty(serviceName))
        {
            throw new InvalidOperationException(
                $"Command '{contract.Name}' version {contract.Version} has no PostgreSQL owner.");
        }

        var owner = new ServiceIdentity(serviceName);

        return owner;
    }

    private static void ThrowOwnershipConflict(
        ContractIdentity contract,
        ServiceIdentity existingOwner,
        ServiceIdentity candidateOwner)
    {
        var owners = new[] { existingOwner.Value, candidateOwner.Value };
        Array.Sort(owners, StringComparer.Ordinal);
        var message = $"Command '{contract.Name}' version {contract.Version} "
            + $"has conflicting owners '{owners[0]}' and '{owners[1]}'.";
        var exception = new InvalidOperationException(message);

        throw exception;
    }
}
