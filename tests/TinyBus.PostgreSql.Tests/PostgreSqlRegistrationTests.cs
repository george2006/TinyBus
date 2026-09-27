using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlRegistrationTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlRegistrationTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Host_starts_after_schema_and_topology_are_ready()
    {
        await DropSchemaAsync();
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var serviceName = "payments-" + scenario;
        const string contractName = "payments.capture";
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        void Configure(TinyBusOptions options)
        {
            options.Service(serviceName);
            options.UsePostgreSql(postgreSql.ConnectionString);
        }

        builder.Services.AddTinyBus<TestManifest>(Configure);
        using var host = builder.Build();

        await host.StartAsync();

        var contract = new ContractIdentity(contractName, 1);
        var owner = await ReadCommandOwnerAsync(contract);

        Assert.Equal(serviceName, owner);

        await host.StopAsync();
    }

    private async Task<string> ReadCommandOwnerAsync(ContractIdentity contract)
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT service_name
            FROM tinybus.command_owners
            WHERE contract_name = @contractName
              AND contract_version = @contractVersion;
            """;
        command.Parameters.AddWithValue("contractName", contract.Name);
        command.Parameters.AddWithValue("contractVersion", contract.Version);
        var result = await command.ExecuteScalarAsync();
        var serviceName = Assert.IsType<string>(result);

        return serviceName;
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestManifest : IBusManifest
    {
        private readonly IReadOnlyList<MessageDescriptor> messages;

        public TestManifest()
        {
            const string contractName = "payments.capture";
            var contract = new ContractIdentity(contractName, 1);
            var descriptor = new MessageDescriptor(
                contract,
                typeof(TestCommand),
                typeof(TestCommandHandler),
                MessageKind.Command);
            messages = new[] { descriptor };
        }

        public IReadOnlyList<MessageDescriptor> Messages => messages;
    }

    private sealed record TestCommand;

    private sealed class TestCommandHandler;
}
