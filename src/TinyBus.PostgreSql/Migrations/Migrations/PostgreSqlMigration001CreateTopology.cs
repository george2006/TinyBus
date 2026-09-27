namespace TinyBus.PostgreSql.Migrations;

internal static class PostgreSqlMigration001CreateTopology
{
    internal static PostgreSqlMigration Create()
    {
        const string sql = """
            CREATE TABLE tinybus.command_owners
            (
                contract_name text NOT NULL,
                contract_version integer NOT NULL,
                service_name text NOT NULL,
                CONSTRAINT pk_command_owners
                    PRIMARY KEY (contract_name, contract_version),
                CONSTRAINT ck_command_owners_contract_name
                    CHECK (length(trim(contract_name)) > 0),
                CONSTRAINT ck_command_owners_contract_version
                    CHECK (contract_version > 0),
                CONSTRAINT ck_command_owners_service_name
                    CHECK (length(trim(service_name)) > 0)
            );
            """;

        var migration = new PostgreSqlMigration(1, "001_CreateTopology", sql);

        return migration;
    }
}
