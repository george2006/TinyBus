using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace TinyBus.RabbitMq.Tests;

public sealed class RabbitMqTransportTests : IClassFixture<RabbitMqFixture>
{
    private readonly RabbitMqFixture rabbitMq;

    public RabbitMqTransportTests(RabbitMqFixture rabbitMq)
    {
        this.rabbitMq = rabbitMq;
    }

    [Fact]
    public async Task Service_queue_preserves_newer_command_bindings_across_restart()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var refund = new ContractIdentity($"{scenario}.payments.refund", 1);
        var newerTopology = CreateTopology(service, capture, refund);
        var olderTopology = CreateTopology(service, capture);

        await using (var newerTransport = new RabbitMqTransport(rabbitMq.ConnectionString))
        {
            await newerTransport.InitializeAsync(newerTopology);
        }

        await using (var olderTransport = new RabbitMqTransport(rabbitMq.ConnectionString))
        {
            await olderTransport.InitializeAsync(olderTopology);
        }

        await rabbitMq.RestartAsync();

        await using var connection = await rabbitMq.OpenConnectionAsync();
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection.CreateChannelAsync(channelOptions);
        var captureAddress = CommandAddress.From(capture);
        var refundAddress = CommandAddress.From(refund);
        var serviceAddress = ServiceAddress.From(service);
        var expectedQueueName = "tinybus." + service.Value;

        Assert.Equal(expectedQueueName, serviceAddress.QueueName);

        await PublishAsync(channel, captureAddress, "capture");
        await PublishAsync(channel, refundAddress, "refund");

        var firstMessage = await ReadAsync(channel, serviceAddress);
        var secondMessage = await ReadAsync(channel, serviceAddress);
        var messages = new[] { firstMessage, secondMessage };

        Assert.Contains("capture", messages);
        Assert.Contains("refund", messages);
    }

    [Fact]
    public async Task Conflicting_owner_fails_before_its_service_queue_is_declared()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var paymentsService = new ServiceIdentity($"payments-{scenario}");
        var checkoutService = new ServiceIdentity($"checkout-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var ship = new ContractIdentity($"{scenario}.shipping.ship", 1);
        var payments = CreateTopology(paymentsService, capture);
        var checkout = CreateTopology(checkoutService, capture, ship);

        await using var paymentsTransport = new RabbitMqTransport(rabbitMq.ConnectionString);
        await paymentsTransport.InitializeAsync(payments);

        await using var checkoutTransport = new RabbitMqTransport(rabbitMq.ConnectionString);
        Func<Task> initializeCheckout = () => InitializeAsync(checkoutTransport, checkout);
        await Assert.ThrowsAsync<InvalidOperationException>(initializeCheckout);

        await using var verificationConnection = await rabbitMq.OpenConnectionAsync();
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var verificationChannel = await verificationConnection.CreateChannelAsync(channelOptions);
        var checkoutAddress = ServiceAddress.From(checkoutService);
        Func<Task> findCheckoutQueue = () => DeclarePassiveAsync(verificationChannel, checkoutAddress);

        await Assert.ThrowsAsync<OperationInterruptedException>(findCheckoutQueue);
    }

    private static async Task PublishAsync(
        IChannel channel,
        CommandAddress address,
        string value)
    {
        var body = Encoding.UTF8.GetBytes(value);

        await channel.BasicPublishAsync(
            address.Exchange,
            address.RoutingKey,
            mandatory: true,
            body);
    }

    private static async Task<string> ReadAsync(
        IChannel channel,
        ServiceAddress serviceAddress)
    {
        var delivery = await channel.BasicGetAsync(serviceAddress.QueueName, autoAck: true);
        var received = Assert.IsType<BasicGetResult>(delivery);
        var value = Encoding.UTF8.GetString(received.Body.Span);

        return value;
    }

    private static Task InitializeAsync(
        RabbitMqTransport transport,
        ServiceTopology topology)
    {
        var initialization = transport.InitializeAsync(topology);
        var task = initialization.AsTask();

        return task;
    }

    private static async Task DeclarePassiveAsync(
        IChannel channel,
        ServiceAddress serviceAddress)
    {
        await channel.QueueDeclarePassiveAsync(serviceAddress.QueueName);
    }

    private static ServiceTopology CreateTopology(
        ServiceIdentity service,
        params ContractIdentity[] commands)
    {
        var messages = new List<MessageDescriptor>();

        foreach (var contract in commands)
        {
            var descriptor = new MessageDescriptor(
                contract,
                typeof(TestCommand),
                typeof(TestCommandHandler),
                MessageKind.Command);
            messages.Add(descriptor);
        }

        var topology = new ServiceTopology(service, messages);
        return topology;
    }

    private sealed class TestCommand;

    private sealed class TestCommandHandler;
}
