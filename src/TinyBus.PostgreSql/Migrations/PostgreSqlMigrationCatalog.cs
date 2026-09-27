using System;
using System.Collections.Generic;
using System.Linq;

namespace TinyBus.PostgreSql.Migrations;

internal sealed class PostgreSqlMigrationCatalog
{
    private readonly IReadOnlyList<PostgreSqlMigration> migrations;

    internal PostgreSqlMigrationCatalog(IEnumerable<PostgreSqlMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);

        var orderedMigrations = migrations
            .OrderBy(migration => migration.Version)
            .ToArray();

        EnsureCatalogIsNotEmpty(orderedMigrations);
        EnsureVersionsAreContiguous(orderedMigrations);
        EnsureNamesAreUnique(orderedMigrations);

        this.migrations = Array.AsReadOnly(orderedMigrations);
    }

    internal IReadOnlyList<PostgreSqlMigration> Migrations => migrations;

    private static void EnsureCatalogIsNotEmpty(
        IReadOnlyCollection<PostgreSqlMigration> migrations)
    {
        if (migrations.Count == 0)
        {
            throw new ArgumentException("A migration catalog cannot be empty.");
        }
    }

    private static void EnsureVersionsAreContiguous(
        IReadOnlyList<PostgreSqlMigration> migrations)
    {
        for (var index = 0; index < migrations.Count; index++)
        {
            var expectedVersion = index + 1L;
            var actualVersion = migrations[index].Version;

            if (actualVersion != expectedVersion)
            {
                throw new ArgumentException(
                    $"Expected migration version {expectedVersion}, but found {actualVersion}.");
            }
        }
    }

    private static void EnsureNamesAreUnique(
        IEnumerable<PostgreSqlMigration> migrations)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var migration in migrations)
        {
            if (!names.Add(migration.Name))
            {
                throw new ArgumentException(
                    $"Migration name '{migration.Name}' appears more than once.");
            }
        }
    }
}
