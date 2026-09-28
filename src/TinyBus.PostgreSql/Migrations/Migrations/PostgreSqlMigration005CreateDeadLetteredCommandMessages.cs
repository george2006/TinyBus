namespace TinyBus.PostgreSql.Migrations;

internal static class PostgreSqlMigration005CreateDeadLetteredCommandMessages
{
    internal static PostgreSqlMigration Create()
    {
        const string sql = """
            CREATE TABLE tinybus.dead_lettered_command_messages
            (
                dead_letter_id bigint GENERATED ALWAYS AS IDENTITY,
                original_sequence_id bigint NOT NULL,
                message_id uuid NOT NULL,
                destination_service text NOT NULL,
                contract_name text NOT NULL,
                contract_version integer NOT NULL,
                payload text NOT NULL,
                correlation_id text NULL,
                causation_id text NULL,
                headers jsonb NULL,
                enqueued_at_utc timestamp with time zone NOT NULL,
                failed_attempts integer NOT NULL,
                dead_lettered_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                error_type text NOT NULL,
                error_message text NOT NULL,
                error_details text NOT NULL,
                CONSTRAINT pk_dead_lettered_command_messages PRIMARY KEY (dead_letter_id),
                CONSTRAINT ck_dead_lettered_command_messages_attempts
                    CHECK (failed_attempts > 0)
            );

            CREATE INDEX ix_dead_lettered_command_messages_destination_time
                ON tinybus.dead_lettered_command_messages
                (destination_service, dead_lettered_at_utc, dead_letter_id);
            """;
        var migration = new PostgreSqlMigration(
            5,
            "005_CreateDeadLetteredCommandMessages",
            sql);

        return migration;
    }
}
