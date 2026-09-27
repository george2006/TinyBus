using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlRegistrationTests : IClassFixture<PostgreSqlFixture>
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly PostgreSqlFixture postgreSql;

    public PostgreSqlRegistrationTests(PostgreSqlFixture postgreSql)
    {
        this.postgreSql = postgreSql;
    }

    [Fact]
    public async Task Sent_command_reaches_its_generated_handler_and_is_completed()
    {
        await DropSchemaAsync();
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<PostgreSqlCommandReceipt>();

        void Configure(TinyBusOptions options)
        {
            options.Service("payments-postgresql-e2e");
            options.UsePostgreSql(postgreSql.ConnectionString);
        }

        builder.Services.AddTinyBus(Configure);
        using var host = builder.Build();
        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IBus>();
        var receipt = host.Services.GetRequiredService<PostgreSqlCommandReceipt>();
        var command = new PostgreSqlCapturePayment(42);

        await bus.SendAsync(command);

        var handledCommand = await receipt.WaitAsync(TestTimeout);
        Assert.Equal(command, handledCommand);

        await host.StopAsync();

        var storedCount = await ReadStoredCommandCountAsync();
        Assert.Equal(0, storedCount);
    }

    private async Task<long> ReadStoredCommandCountAsync()
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM tinybus.command_messages;";
        var result = await command.ExecuteScalarAsync();
        var count = Assert.IsType<long>(result);

        return count;
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();
    }
}

[BusContract("tests.postgresql.capture-payment")]
internal sealed record PostgreSqlCapturePayment(int Amount);

internal sealed class PostgreSqlCapturePaymentHandler :
    ICommandHandler<PostgreSqlCapturePayment>
{
    private readonly PostgreSqlCommandReceipt receipt;

    public PostgreSqlCapturePaymentHandler(PostgreSqlCommandReceipt receipt)
    {
        this.receipt = receipt;
    }

    public ValueTask HandleAsync(
        PostgreSqlCapturePayment command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        receipt.Record(command);

        return ValueTask.CompletedTask;
    }
}

internal sealed class PostgreSqlCommandReceipt
{
    private readonly TaskCompletionSource<PostgreSqlCapturePayment> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Record(PostgreSqlCapturePayment command)
    {
        completion.TrySetResult(command);
    }

    internal async Task<PostgreSqlCapturePayment> WaitAsync(TimeSpan timeout)
    {
        var command = await completion.Task.WaitAsync(timeout);

        return command;
    }
}
