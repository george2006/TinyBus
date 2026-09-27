using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace TinyBus.PostgreSql.Migrations;

internal sealed class PostgreSqlMigrationLock
{
    private const string LockDomain = "TinyBus.PostgreSql.Migrations.Lock.v1";
    private static readonly long LockKey = CreateLockKey();

    internal async Task AcquireAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_advisory_lock(@key);";
        command.Parameters.AddWithValue("key", LockKey);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    internal async Task ReleaseAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_catalog.pg_advisory_unlock(@key);";
        command.Parameters.AddWithValue("key", LockKey);
        var result = await command.ExecuteScalarAsync(CancellationToken.None);
        var released = Convert.ToBoolean(result);

        if (!released)
        {
            throw new InvalidOperationException(
                "PostgreSQL could not release the TinyBus migration lock.");
        }
    }

    private static long CreateLockKey()
    {
        var bytes = Encoding.UTF8.GetBytes(LockDomain);
        var checksum = SHA256.HashData(bytes);

        var key = BinaryPrimitives.ReadInt64BigEndian(checksum);

        return key;
    }
}
