namespace TinyBus.PostgreSql.Migrations;

internal static class PostgreSqlMigration003AddCommandClaims
{
    internal static PostgreSqlMigration Create()
    {
        const string sql = """
            ALTER TABLE tinybus.command_messages
                ADD COLUMN claim_id uuid NULL,
                ADD COLUMN claimed_until_utc timestamp with time zone NULL;

            CREATE INDEX ix_command_messages_available
                ON tinybus.command_messages
                (destination_service, claimed_until_utc, sequence_id);
            """;
        var migration = new PostgreSqlMigration(
            3,
            "003_AddCommandClaims",
            sql);

        return migration;
    }
}
