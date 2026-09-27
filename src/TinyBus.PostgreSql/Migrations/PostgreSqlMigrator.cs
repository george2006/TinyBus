using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace TinyBus.PostgreSql.Migrations;

internal sealed class PostgreSqlMigrator
{
    private readonly string connectionString;
    private readonly PostgreSqlMigrationCatalog catalog;

    internal PostgreSqlMigrator(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        this.connectionString = connectionString;
        catalog = CreateCatalog();
    }

    internal PostgreSqlMigrator(
        string connectionString,
        PostgreSqlMigrationCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(catalog);

        this.connectionString = connectionString;
        this.catalog = catalog;
    }

    internal async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var migrationLock = new PostgreSqlMigrationLock();
        await migrationLock.AcquireAsync(connection, cancellationToken);

        try
        {
            var history = new PostgreSqlMigrationHistory();
            await history.EnsureExistsAsync(connection, cancellationToken);

            var appliedMigrations = await history.ReadAsync(connection, cancellationToken);
            var pendingMigrations = CreatePlan(appliedMigrations);

            foreach (var migration in pendingMigrations)
            {
                await ApplyAsync(connection, history, migration, cancellationToken);
            }
        }
        finally
        {
            await migrationLock.ReleaseAsync(connection);
        }
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        return connection;
    }

    private static PostgreSqlMigrationCatalog CreateCatalog()
    {
        var createTopology = PostgreSqlMigration001CreateTopology.Create();
        var catalog = new PostgreSqlMigrationCatalog([createTopology]);

        return catalog;
    }

    private IReadOnlyList<PostgreSqlMigration> CreatePlan(
        IReadOnlyList<AppliedPostgreSqlMigration> appliedMigrations)
    {
        EnsureHistoryIsContiguous(appliedMigrations);
        EnsureHistoryMatchesCatalog(appliedMigrations);

        if (appliedMigrations.Count > catalog.Migrations.Count)
        {
            throw new InvalidOperationException(
                "The database migration version is newer than this TinyBus provider.");
        }

        var pendingMigrations = catalog.Migrations
            .Skip(appliedMigrations.Count)
            .ToArray();

        return pendingMigrations;
    }

    private static void EnsureHistoryIsContiguous(
        IReadOnlyList<AppliedPostgreSqlMigration> appliedMigrations)
    {
        for (var index = 0; index < appliedMigrations.Count; index++)
        {
            var expectedVersion = index + 1L;
            var actualVersion = appliedMigrations[index].Version;

            if (actualVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Expected applied migration {expectedVersion}, but found {actualVersion}.");
            }
        }
    }

    private void EnsureHistoryMatchesCatalog(
        IReadOnlyList<AppliedPostgreSqlMigration> appliedMigrations)
    {
        var comparableCount = Math.Min(appliedMigrations.Count, catalog.Migrations.Count);

        for (var index = 0; index < comparableCount; index++)
        {
            var appliedMigration = appliedMigrations[index];
            var catalogMigration = catalog.Migrations[index];

            EnsureNameMatches(appliedMigration, catalogMigration);
            EnsureChecksumMatches(appliedMigration, catalogMigration);
        }
    }

    private static void EnsureNameMatches(
        AppliedPostgreSqlMigration appliedMigration,
        PostgreSqlMigration catalogMigration)
    {
        var namesMatch = string.Equals(
            appliedMigration.Name,
            catalogMigration.Name,
            StringComparison.Ordinal);

        if (!namesMatch)
        {
            throw new InvalidOperationException(
                $"Applied migration {appliedMigration.Version} is named " +
                $"'{appliedMigration.Name}', but TinyBus expects '{catalogMigration.Name}'.");
        }
    }

    private static void EnsureChecksumMatches(
        AppliedPostgreSqlMigration appliedMigration,
        PostgreSqlMigration catalogMigration)
    {
        var checksumsMatch = string.Equals(
            appliedMigration.Checksum,
            catalogMigration.Checksum,
            StringComparison.Ordinal);

        if (!checksumsMatch)
        {
            throw new InvalidOperationException(
                $"Applied migration {appliedMigration.Version} has an unexpected checksum.");
        }
    }

    private static async Task ApplyAsync(
        NpgsqlConnection connection,
        PostgreSqlMigrationHistory history,
        PostgreSqlMigration migration,
        CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await history.AppendAsync(connection, transaction, migration, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
