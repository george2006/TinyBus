using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using TinyBus;
using TinyBus.PostgreSql;
using TinyBus.RabbitMq;

await VerifyPostgreSqlAsync();
await VerifyRabbitMqAsync();

Console.WriteLine("TinyBus packaged runtime smoke passed.");

static async Task VerifyPostgreSqlAsync()
{
    var containerBuilder = new PostgreSqlBuilder("postgres:17-alpine");
    await using var container = containerBuilder.Build();
    await container.StartAsync();

    var connectionString = container.GetConnectionString();
    using var host = CreateHost(options =>
    {
        ConfigureBus(options, "package-smoke-postgresql");
        options.UsePostgreSql(connectionString);
    });
    await host.StartAsync();

    await VerifySuccessfulCommandAsync(host);
    await SendFailingCommandAsync(host);
    await WaitForPostgreSqlDeadLetterAsync(connectionString);

    await host.StopAsync();
}

static async Task VerifyRabbitMqAsync()
{
    var containerBuilder = new RabbitMqBuilder("rabbitmq:4.3-alpine");
    await using var container = containerBuilder.Build();
    await container.StartAsync();

    const string serviceName = "package-smoke-rabbitmq";
    var connectionString = container.GetConnectionString();
    using var host = CreateHost(options =>
    {
        ConfigureBus(options, serviceName);
        options.UseRabbitMq(connectionString);
    });
    await host.StartAsync();

    await VerifySuccessfulCommandAsync(host);
    await SendFailingCommandAsync(host);
    await WaitForRabbitMqDeadLetterAsync(connectionString, serviceName);

    await host.StopAsync();
}

static IHost CreateHost(Action<TinyBusOptions> configure)
{
    var settings = new HostApplicationBuilderSettings
    {
        DisableDefaults = true
    };
    var builder = new HostApplicationBuilder(settings);
    builder.Services.AddSingleton<CommandReceipt>();
    builder.Services.AddTinyBus(configure);
    var host = builder.Build();

    return host;
}

static void ConfigureBus(TinyBusOptions options, string serviceName)
{
    var retryDelay = TimeSpan.FromSeconds(1);
    options.Service(serviceName);
    options.RetryOptions.MaximumAttempts = 2;
    options.RetryOptions.MinimumDelay = retryDelay;
    options.RetryOptions.MaximumDelay = retryDelay;
}

static async Task VerifySuccessfulCommandAsync(IHost host)
{
    var bus = host.Services.GetRequiredService<IBus>();
    var receipt = host.Services.GetRequiredService<CommandReceipt>();
    var command = new PackageSmokeCommand(Guid.NewGuid());

    await bus.SendAsync(command);

    var received = await receipt.WaitAsync();
    Require(received == command, "The packaged command must reach its generated handler.");
}

static async Task SendFailingCommandAsync(IHost host)
{
    var bus = host.Services.GetRequiredService<IBus>();
    var command = new PackageSmokeFailingCommand(Guid.NewGuid());

    await bus.SendAsync(command);
}

static async Task WaitForPostgreSqlDeadLetterAsync(string connectionString)
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    while (true)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellation.Token);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT failed_attempts, error_message
            FROM tinybus.dead_lettered_command_messages;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellation.Token);

        if (await reader.ReadAsync(cancellation.Token))
        {
            var attempts = reader.GetInt32(0);
            var errorMessage = reader.GetString(1);
            Require(attempts == 2, "PostgreSQL must preserve the exhausted attempt count.");
            Require(errorMessage == PackageSmokeFailingHandler.ErrorMessage,
                "PostgreSQL must preserve the final handler error.");
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
    }
}

static async Task WaitForRabbitMqDeadLetterAsync(
    string connectionString,
    string serviceName)
{
    var connectionFactory = new ConnectionFactory
    {
        Uri = new Uri(connectionString)
    };
    await using var connection = await connectionFactory.CreateConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();
    var queueName = $"tinybus.{serviceName}.dead-letter";
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));

    while (true)
    {
        var delivery = await channel.BasicGetAsync(
            queueName,
            autoAck: true,
            cancellation.Token);

        if (delivery is not null)
        {
            var headers = delivery.BasicProperties.Headers;
            Require(headers is not null, "RabbitMQ must preserve dead-letter headers.");
            var attempts = Convert.ToInt32(headers!["tinybus-failed-attempt"]);
            var errorBytes = (byte[])headers["tinybus-exception-message"]!;
            var errorMessage = Encoding.UTF8.GetString(errorBytes);
            Require(attempts == 2, "RabbitMQ must preserve the exhausted attempt count.");
            Require(errorMessage == PackageSmokeFailingHandler.ErrorMessage,
                "RabbitMQ must preserve the final handler error.");
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation.Token);
    }
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

[BusContract("package-smoke.command")]
internal sealed record PackageSmokeCommand(Guid Id);

[BusContract("package-smoke.failing-command")]
internal sealed record PackageSmokeFailingCommand(Guid Id);

internal sealed class PackageSmokeCommandHandler : ICommandHandler<PackageSmokeCommand>
{
    private readonly CommandReceipt receipt;

    public PackageSmokeCommandHandler(CommandReceipt receipt)
    {
        this.receipt = receipt;
    }

    public ValueTask HandleAsync(
        PackageSmokeCommand command,
        CancellationToken cancellationToken)
    {
        receipt.Record(command);
        return ValueTask.CompletedTask;
    }
}

internal sealed class PackageSmokeFailingHandler : ICommandHandler<PackageSmokeFailingCommand>
{
    internal const string ErrorMessage = "Packaged handler failed.";

    public ValueTask HandleAsync(
        PackageSmokeFailingCommand command,
        CancellationToken cancellationToken)
    {
        throw new InvalidOperationException(ErrorMessage);
    }
}

internal sealed class CommandReceipt
{
    private readonly TaskCompletionSource<PackageSmokeCommand> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Record(PackageSmokeCommand command)
    {
        completion.TrySetResult(command);
    }

    internal async Task<PackageSmokeCommand> WaitAsync()
    {
        var timeout = TimeSpan.FromSeconds(15);
        var command = await completion.Task.WaitAsync(timeout);

        return command;
    }
}
