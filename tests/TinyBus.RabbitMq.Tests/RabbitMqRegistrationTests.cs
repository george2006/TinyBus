using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqRegistrationTests : IClassFixture<RabbitMqFixture>
{
    private const string ServiceName = "payments-rabbitmq-e2e";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly RabbitMqFixture rabbitMq;

    public RabbitMqRegistrationTests(RabbitMqFixture rabbitMq)
    {
        this.rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task Sent_command_reaches_its_generated_handler_and_is_acknowledged()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<RabbitMqCommandReceipt>();

        builder.Services.AddTinyBus(ConfigureBus);
        using var host = builder.Build();
        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IBus>();
        var receipt = host.Services.GetRequiredService<RabbitMqCommandReceipt>();
        var command = new RabbitMqCapturePayment(42);

        await bus.SendAsync(command);

        var handledCommand = await receipt.WaitAsync(TestTimeout);
        Assert.Equal(command, handledCommand);

        await host.StopAsync();

        await using var connection = await rabbitMq.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var queueName = "tinybus." + ServiceName;
        var remainingDelivery = await channel.BasicGetAsync(queueName, autoAck: true);
        Assert.Null(remainingDelivery);
    }

    [Fact]
    public async Task Failed_command_exhausts_retries_and_reaches_the_dead_letter_queue()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();

        builder.Services.AddTinyBus(ConfigureBus);
        using var host = builder.Build();
        await host.StartAsync();
        var bus = host.Services.GetRequiredService<IBus>();
        var command = new RabbitMqFailPayment(42);

        await bus.SendAsync(command);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var service = new ServiceIdentity(ServiceName);
        var serviceAddress = ServiceAddress.From(service);
        using var cancellation = new CancellationTokenSource(TestTimeout);
        var failure = await ReadDeadLetterAsync(
            channel,
            serviceAddress,
            cancellation.Token);
        await host.StopAsync();
        var activeDelivery = await channel.BasicGetAsync(
            serviceAddress.QueueName,
            autoAck: true);
        var attempts = failure.BasicProperties.Headers![RabbitMqHeaderNames.FailedAttempt];
        var errorType = ReadHeader(failure, RabbitMqHeaderNames.ExceptionType);
        var errorMessage = ReadHeader(failure, RabbitMqHeaderNames.ExceptionMessage);
        var expectedErrorType = typeof(InvalidOperationException).FullName;

        Assert.Null(activeDelivery);
        Assert.Equal(2, attempts);
        Assert.Equal(expectedErrorType, errorType);
        Assert.Equal(RabbitMqFailPaymentHandler.FailureMessage, errorMessage);
    }

    private void ConfigureBus(TinyBusOptions options)
    {
        options.Service(ServiceName);
        options.RetryOptions.MaximumAttempts = 2;
        options.RetryOptions.MinimumDelay = RetryDelay;
        options.RetryOptions.MaximumDelay = RetryDelay;
        options.UseRabbitMq(rabbitMq.ConnectionString);
    }

    private static async Task<BasicGetResult> ReadDeadLetterAsync(
        IChannel channel,
        ServiceAddress serviceAddress,
        CancellationToken cancellationToken)
    {
        var pollingInterval = TimeSpan.FromMilliseconds(50);

        while (true)
        {
            var delivery = await channel.BasicGetAsync(
                serviceAddress.DeadLetterQueueName,
                autoAck: true,
                cancellationToken);

            if (delivery is not null)
            {
                return delivery;
            }

            await Task.Delay(pollingInterval, cancellationToken);
        }
    }

    private static string ReadHeader(BasicGetResult delivery, string name)
    {
        var headers = delivery.BasicProperties.Headers;
        var value = Assert.IsType<byte[]>(headers![name]);
        var text = Encoding.UTF8.GetString(value);

        return text;
    }
}

[BusContract("tests.rabbitmq.capture-payment")]
internal sealed record RabbitMqCapturePayment(int Amount);

[BusContract("tests.rabbitmq.fail-payment")]
internal sealed record RabbitMqFailPayment(int Amount);

internal sealed class RabbitMqCapturePaymentHandler :
    ICommandHandler<RabbitMqCapturePayment>
{
    private readonly RabbitMqCommandReceipt receipt;

    public RabbitMqCapturePaymentHandler(RabbitMqCommandReceipt receipt)
    {
        this.receipt = receipt;
    }

    public ValueTask HandleAsync(
        RabbitMqCapturePayment command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        receipt.Record(command);

        return ValueTask.CompletedTask;
    }
}

internal sealed class RabbitMqFailPaymentHandler :
    ICommandHandler<RabbitMqFailPayment>
{
    internal const string FailureMessage = "RabbitMQ handler failed.";

    public ValueTask HandleAsync(
        RabbitMqFailPayment command,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException(FailureMessage);
    }
}

internal sealed class RabbitMqCommandReceipt
{
    private readonly TaskCompletionSource<RabbitMqCapturePayment> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Record(RabbitMqCapturePayment command)
    {
        completion.TrySetResult(command);
    }

    internal async Task<RabbitMqCapturePayment> WaitAsync(TimeSpan timeout)
    {
        var command = await completion.Task.WaitAsync(timeout);

        return command;
    }
}
