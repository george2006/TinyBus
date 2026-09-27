using System.Text.Json;
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
    public async Task Host_is_ready_for_typed_command_sending()
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
        var bus = host.Services.GetRequiredService<IBus>();
        var command = new TestCommand(42);
        await bus.SendAsync(command);
        var owner = await ReadCommandOwnerAsync(contract);
        var stored = await ReadStoredCommandAsync();
        var received = JsonSerializer.Deserialize<TestCommand>(stored.Payload);

        Assert.Equal(serviceName, owner);
        Assert.Equal(serviceName, stored.DestinationService);
        Assert.Equal(command, received);

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

    private async Task<StoredCommand> ReadStoredCommandAsync()
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT destination_service, payload
            FROM tinybus.command_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var destinationService = reader.GetString(0);
        var payload = reader.GetString(1);
        var stored = new StoredCommand(destinationService, payload);

        return stored;
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

    [BusContract("payments.capture")]
    private sealed record TestCommand(int Amount);

    private sealed class TestCommandHandler;

    private sealed record StoredCommand(
        string DestinationService,
        string Payload);
}
