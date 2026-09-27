using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace TinyBus.PostgreSql.Migrations;

internal sealed class PostgreSqlMigrationHistory
{
    internal async Task EnsureExistsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE SCHEMA IF NOT EXISTS tinybus;

            CREATE TABLE IF NOT EXISTS tinybus.schema_migrations
            (
                "Version" bigint NOT NULL,
                "Name" text NOT NULL,
                "Checksum" character(64) NOT NULL,
                "AppliedAtUtc" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_schema_migrations" PRIMARY KEY ("Version")
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal async Task<IReadOnlyList<AppliedPostgreSqlMigration>> ReadAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var migrations = new List<AppliedPostgreSqlMigration>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT "Version", "Name", "Checksum"
            FROM tinybus.schema_migrations
            ORDER BY "Version";
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var version = reader.GetInt64(0);
            var name = reader.GetString(1);
            var checksum = reader.GetString(2);
            var migration = new AppliedPostgreSqlMigration(version, name, checksum);
            migrations.Add(migration);
        }

        return migrations;
    }

    internal async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PostgreSqlMigration migration,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO tinybus.schema_migrations
                ("Version", "Name", "Checksum", "AppliedAtUtc")
            VALUES
                (@version, @name, @checksum, CURRENT_TIMESTAMP);
            """;
        command.Parameters.AddWithValue("version", migration.Version);
        command.Parameters.AddWithValue("name", migration.Name);
        command.Parameters.AddWithValue("checksum", migration.Checksum);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

internal sealed record AppliedPostgreSqlMigration(
    long Version,
    string Name,
    string Checksum);
