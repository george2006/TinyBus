namespace TinyBus.PostgreSql.Migrations;

internal static class PostgreSqlMigration002CreateCommandMessages
{
    internal static PostgreSqlMigration Create()
    {
        const string sql = """
            CREATE TABLE tinybus.command_messages
            (
                sequence_id bigint GENERATED ALWAYS AS IDENTITY,
                message_id uuid NOT NULL,
                destination_service text NOT NULL,
                contract_name text NOT NULL,
                contract_version integer NOT NULL,
                payload text NOT NULL,
                correlation_id text NULL,
                causation_id text NULL,
                headers jsonb NULL,
                enqueued_at_utc timestamp with time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
                CONSTRAINT pk_command_messages PRIMARY KEY (sequence_id),
                CONSTRAINT ck_command_messages_destination
                    CHECK (length(trim(destination_service)) > 0),
                CONSTRAINT ck_command_messages_contract
                    CHECK (length(trim(contract_name)) > 0),
                CONSTRAINT ck_command_messages_version
                    CHECK (contract_version > 0)
            );

            CREATE INDEX ix_command_messages_destination_sequence
                ON tinybus.command_messages (destination_service, sequence_id);
            """;
        var migration = new PostgreSqlMigration(
            2,
            "002_CreateCommandMessages",
            sql);

        return migration;
    }
}
