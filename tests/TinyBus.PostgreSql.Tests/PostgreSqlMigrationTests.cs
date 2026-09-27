using Npgsql;
using TinyBus.PostgreSql.Migrations;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlMigrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlMigrationTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Fresh_migration_applies_once_and_creates_command_ownership()
    {
        await ResetSchemaAsync();
        var migrator = CreateMigrator();

        await migrator.MigrateAsync(CancellationToken.None);
        await migrator.MigrateAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync();
        var appliedMigrations = await ReadAppliedMigrationsAsync(connection);
        var commandOwnersExists = await CommandOwnersTableExistsAsync(connection);

        Assert.Equal([1L, 2L, 3L], appliedMigrations);
        Assert.True(commandOwnersExists);
    }

    [Fact]
    public async Task Concurrent_migrators_serialize_and_observe_committed_history()
    {
        await ResetSchemaAsync();
        var firstMigrator = CreateMigrator();
        var secondMigrator = CreateMigrator();

        var firstMigration = firstMigrator.MigrateAsync(CancellationToken.None);
        var secondMigration = secondMigrator.MigrateAsync(CancellationToken.None);
        await Task.WhenAll(firstMigration, secondMigration);

        await using var connection = await OpenConnectionAsync();
        var appliedMigrations = await ReadAppliedMigrationsAsync(connection);

        Assert.Equal([1L, 2L, 3L], appliedMigrations);
    }

    [Fact]
    public async Task Changed_applied_migration_is_rejected()
    {
        await ResetSchemaAsync();
        var migrator = CreateMigrator();
        await migrator.MigrateAsync(CancellationToken.None);
        await ChangeAppliedChecksumAsync();

        Task MigrateAgain()
        {
            var migration = migrator.MigrateAsync(CancellationToken.None);

            return migration;
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(MigrateAgain);

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Failed_migration_rolls_back_without_losing_prior_history()
    {
        await ResetSchemaAsync();
        var createTopology = PostgreSqlMigration001CreateTopology.Create();
        var failingMigration = new PostgreSqlMigration(
            2,
            "002_FailingMigration",
            """
            CREATE TABLE tinybus.failed_step (id integer NOT NULL);
            SELECT 1 / 0;
            """);
        var catalog = new PostgreSqlMigrationCatalog(
            [createTopology, failingMigration]);
        var migrator = new PostgreSqlMigrator(postgreSql.ConnectionString, catalog);

        Task Migrate()
        {
            var migration = migrator.MigrateAsync(CancellationToken.None);

            return migration;
        }

        await Assert.ThrowsAsync<PostgresException>(Migrate);

        await using var connection = await OpenConnectionAsync();
        var appliedMigrations = await ReadAppliedMigrationsAsync(connection);
        var failedTableExists = await TableExistsAsync(connection, "failed_step");

        Assert.Equal([1L], appliedMigrations);
        Assert.False(failedTableExists);
    }

    private PostgreSqlMigrator CreateMigrator()
    {
        var migrator = new PostgreSqlMigrator(postgreSql.ConnectionString);

        return migrator;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();

        return connection;
    }

    private async Task<IReadOnlyList<long>> ReadAppliedMigrationsAsync(
        NpgsqlConnection connection)
    {
        var versions = new List<long>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Version"
            FROM tinybus.schema_migrations
            ORDER BY "Version";
            """;
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            versions.Add(reader.GetInt64(0));
        }

        return versions;
    }

    private async Task<bool> CommandOwnersTableExistsAsync(
        NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT to_regclass('tinybus.command_owners') IS NOT NULL;
            """;
        var result = await command.ExecuteScalarAsync();
        var exists = Convert.ToBoolean(result);

        return exists;
    }

    private static async Task<bool> TableExistsAsync(
        NpgsqlConnection connection,
        string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS
            (
                SELECT 1
                FROM pg_catalog.pg_class AS tables
                INNER JOIN pg_catalog.pg_namespace AS schemas
                    ON schemas.oid = tables.relnamespace
                WHERE schemas.nspname = 'tinybus'
                  AND tables.relname = @table
                  AND tables.relkind IN ('r', 'p')
            );
            """;
        command.Parameters.AddWithValue("table", table);
        var result = await command.ExecuteScalarAsync();
        var exists = Convert.ToBoolean(result);

        return exists;
    }

    private async Task ChangeAppliedChecksumAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE tinybus.schema_migrations
            SET "Checksum" = repeat('0', 64)
            WHERE "Version" = 1;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private async Task ResetSchemaAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();
    }
}
