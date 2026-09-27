using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TinyBus;
using TinyBus.PostgreSql.Persistence.Commands;
using TinyBus.PostgreSql.Persistence.Queries;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlTopologyReconciler
{
    private readonly string connectionString;
    private readonly TryRegisterCommandOwner tryRegisterCommandOwner;
    private readonly ReadCommandOwner readCommandOwner;

    internal PostgreSqlTopologyReconciler(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
        tryRegisterCommandOwner = new TryRegisterCommandOwner();
        readCommandOwner = new ReadCommandOwner();
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

    private async Task ConfirmOwnerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ContractIdentity contract,
        ServiceIdentity candidateOwner,
        CancellationToken cancellationToken)
    {
        var inserted = await tryRegisterCommandOwner.ExecuteAsync(
            connection,
            transaction,
            contract,
            candidateOwner,
            cancellationToken);

        if (inserted)
        {
            return;
        }

        var existingOwner = await readCommandOwner.ExecuteAsync(
            connection,
            transaction,
            contract,
            cancellationToken);

        if (existingOwner is null)
        {
            throw new InvalidOperationException(
                $"Command '{contract.Name}' version {contract.Version} has no PostgreSQL owner.");
        }

        if (existingOwner.Value == candidateOwner)
        {
            return;
        }

        ThrowOwnershipConflict(contract, existingOwner.Value, candidateOwner);
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
