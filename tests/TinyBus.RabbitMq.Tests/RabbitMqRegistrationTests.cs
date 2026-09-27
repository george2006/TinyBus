using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqRegistrationTests : IClassFixture<RabbitMqFixture>
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly RabbitMqFixture rabbitMq;

    public RabbitMqRegistrationTests(RabbitMqFixture rabbitMq)
    {
        this.rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task Sent_command_reaches_its_generated_handler_and_is_acknowledged()
    {
        const string serviceName = "payments-rabbitmq-e2e";
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<RabbitMqCommandReceipt>();

        void Configure(TinyBusOptions options)
        {
            options.Service(serviceName);
            options.UseRabbitMq(rabbitMq.ConnectionString);
        }

        builder.Services.AddTinyBus(Configure);
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
        var queueName = "tinybus." + serviceName;
        var remainingDelivery = await channel.BasicGetAsync(queueName, autoAck: true);
        Assert.Null(remainingDelivery);
    }
}

[BusContract("tests.rabbitmq.capture-payment")]
internal sealed record RabbitMqCapturePayment(int Amount);

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
