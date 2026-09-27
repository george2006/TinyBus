using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;
using TinyBus;

namespace TinyBus.PostgreSql;

internal sealed class PostgreSqlCommandRouteSource
{
    private readonly string connectionString;

    internal PostgreSqlCommandRouteSource(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
    }

    internal async Task<IReadOnlyCollection<CommandRoute>> LoadAsync(
        IReadOnlyCollection<ContractIdentity> requiredContracts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requiredContracts);
        cancellationToken.ThrowIfCancellationRequested();

        var contracts = ReadDistinctContracts(requiredContracts);
        if (contracts.Count == 0)
        {
            var emptyRoutes = Array.Empty<CommandRoute>();

            return emptyRoutes;
        }

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var routes = await ReadRoutesAsync(connection, contracts, cancellationToken);

        return routes;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static IReadOnlyList<ContractIdentity> ReadDistinctContracts(
        IReadOnlyCollection<ContractIdentity> requiredContracts)
    {
        var uniqueContracts = new HashSet<ContractIdentity>();
        var contracts = new List<ContractIdentity>();

        foreach (var contract in requiredContracts)
        {
            if (uniqueContracts.Add(contract))
            {
                contracts.Add(contract);
            }
        }

        return contracts;
    }

    private static async Task<IReadOnlyCollection<CommandRoute>> ReadRoutesAsync(
        NpgsqlConnection connection,
        IReadOnlyList<ContractIdentity> contracts,
        CancellationToken cancellationToken)
    {
        var contractNames = new string[contracts.Count];
        var contractVersions = new int[contracts.Count];
        CopyContractValues(contracts, contractNames, contractVersions);

        var routes = new List<CommandRoute>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            WITH required_contracts (contract_name, contract_version) AS
            (
                SELECT *
                FROM unnest(@contractNames, @contractVersions)
            )
            SELECT owners.contract_name, owners.contract_version, owners.service_name
            FROM tinybus.command_owners AS owners
            INNER JOIN required_contracts AS required
                ON required.contract_name = owners.contract_name
               AND required.contract_version = owners.contract_version
            ORDER BY owners.contract_name, owners.contract_version;
            """;
        command.Parameters.AddWithValue("contractNames", contractNames);
        command.Parameters.AddWithValue("contractVersions", contractVersions);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var contractName = reader.GetString(0);
            var contractVersion = reader.GetInt32(1);
            var serviceName = reader.GetString(2);
            var contract = new ContractIdentity(contractName, contractVersion);
            var service = new ServiceIdentity(serviceName);
            var route = new CommandRoute(contract, service);
            routes.Add(route);
        }

        return routes;
    }

    private static void CopyContractValues(
        IReadOnlyList<ContractIdentity> contracts,
        string[] contractNames,
        int[] contractVersions)
    {
        for (var index = 0; index < contracts.Count; index++)
        {
            contractNames[index] = contracts[index].Name;
            contractVersions[index] = contracts[index].Version;
        }
    }
}
