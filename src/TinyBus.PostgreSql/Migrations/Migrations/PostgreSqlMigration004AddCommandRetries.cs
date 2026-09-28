namespace TinyBus.PostgreSql.Migrations;

internal static class PostgreSqlMigration004AddCommandRetries
{
    internal static PostgreSqlMigration Create()
    {
        const string sql = """
            ALTER TABLE tinybus.command_messages
                ADD COLUMN failed_attempts integer NOT NULL DEFAULT 0,
                ADD COLUMN available_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                ADD CONSTRAINT ck_command_messages_failed_attempts
                    CHECK (failed_attempts >= 0);

            DROP INDEX tinybus.ix_command_messages_available;

            CREATE INDEX ix_command_messages_available
                ON tinybus.command_messages
                (destination_service, available_at_utc, claimed_until_utc, sequence_id);
            """;
        var migration = new PostgreSqlMigration(
            4,
            "004_AddCommandRetries",
            sql);

        return migration;
    }
}
