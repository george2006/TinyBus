using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TinyBus.PostgreSql.Migrations;

internal sealed record PostgreSqlMigration
{
    private const string ChecksumDomain = "TinyBus.PostgreSql.Migrations.Checksum.v1";

    internal PostgreSqlMigration(long version, string name, string sql)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                "A migration version must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);

        Version = version;
        Name = name;
        Sql = sql;
        Checksum = CalculateChecksum();
    }

    internal long Version { get; }

    internal string Name { get; }

    internal string Sql { get; }

    internal string Checksum { get; }

    private string CalculateChecksum()
    {
        var version = Version.ToString(CultureInfo.InvariantCulture);
        var checksumInput = string.Concat(
            ChecksumDomain,
            "\0",
            version,
            "\0",
            Name,
            "\0",
            Sql);
        var checksumBytes = Encoding.UTF8.GetBytes(checksumInput);
        var checksum = SHA256.HashData(checksumBytes);

        var checksumText = Convert.ToHexString(checksum);

        return checksumText;
    }
}
