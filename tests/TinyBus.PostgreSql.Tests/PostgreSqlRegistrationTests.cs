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

        await WaitForCommandCompletionAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Failed_command_exhausts_retries_and_reaches_dead_letter_storage()
    {
        await DropSchemaAsync();
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        void Configure(TinyBusOptions options)
        {
            var retryDelay = TimeSpan.FromSeconds(1);
            options.Service("payments-postgresql-e2e");
            options.RetryOptions.MaximumAttempts = 2;
            options.RetryOptions.MinimumDelay = retryDelay;
            options.RetryOptions.MaximumDelay = retryDelay;
            options.UsePostgreSql(postgreSql.ConnectionString);
        }

        builder.Services.AddTinyBus(Configure);
        using var host = builder.Build();
        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IBus>();
        var command = new PostgreSqlFailPayment(42);

        await bus.SendAsync(command);

        var failure = await WaitForDeadLetterAsync();
        await host.StopAsync();
        var activeCount = await ReadStoredCommandCountAsync();
        var expectedErrorType = typeof(InvalidOperationException).FullName;

        Assert.Equal(0, activeCount);
        Assert.Equal(2, failure.Attempts);
        Assert.Equal(expectedErrorType, failure.ErrorType);
        Assert.Equal(PostgreSqlFailPaymentHandler.FailureMessage, failure.ErrorMessage);
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

    private async Task WaitForCommandCompletionAsync()
    {
        var pollingInterval = TimeSpan.FromMilliseconds(50);
        using var cancellation = new CancellationTokenSource(TestTimeout);

        while (true)
        {
            var storedCount = await ReadStoredCommandCountAsync();

            if (storedCount == 0)
            {
                return;
            }

            await Task.Delay(pollingInterval, cancellation.Token);
        }
    }

    private async Task<StoredCommandFailure> WaitForDeadLetterAsync()
    {
        var pollingInterval = TimeSpan.FromMilliseconds(50);
        using var cancellation = new CancellationTokenSource(TestTimeout);

        while (true)
        {
            var failure = await ReadDeadLetterAsync(cancellation.Token);

            if (failure is not null)
            {
                return failure;
            }

            await Task.Delay(pollingInterval, cancellation.Token);
        }
    }

    private async Task<StoredCommandFailure?> ReadDeadLetterAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT failed_attempts, error_type, error_message
            FROM tinybus.dead_lettered_command_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var attempts = reader.GetInt32(0);
        var errorType = reader.GetString(1);
        var errorMessage = reader.GetString(2);
        var failure = new StoredCommandFailure(attempts, errorType, errorMessage);

        return failure;
    }

    private async Task DropSchemaAsync()
    {
        await using var connection = new NpgsqlConnection(postgreSql.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "DROP SCHEMA IF EXISTS tinybus CASCADE;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed record StoredCommandFailure(
        int Attempts,
        string ErrorType,
        string ErrorMessage);
}

[BusContract("tests.postgresql.capture-payment")]
internal sealed record PostgreSqlCapturePayment(int Amount);

[BusContract("tests.postgresql.fail-payment")]
internal sealed record PostgreSqlFailPayment(int Amount);

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

internal sealed class PostgreSqlFailPaymentHandler :
    ICommandHandler<PostgreSqlFailPayment>
{
    internal const string FailureMessage = "PostgreSQL handler failed.";

    public ValueTask HandleAsync(
        PostgreSqlFailPayment command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(FailureMessage);
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
