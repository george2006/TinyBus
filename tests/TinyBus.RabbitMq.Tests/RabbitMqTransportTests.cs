using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task Send_publishes_a_persistent_envelope_to_the_command_owner()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        await using var transport = new RabbitMqTransport(rabbitMq.ConnectionString);
        await transport.InitializeAsync(topology);
        var messageId = Guid.NewGuid();
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var envelope = new MessageEnvelope(
            messageId,
            capture,
            "{\"amount\":100}",
            "correlation-1",
            "causation-1",
            headers);

        await transport.SendAsync(envelope);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var serviceAddress = ServiceAddress.From(service);
        var delivery = await channel.BasicGetAsync(serviceAddress.QueueName, autoAck: true);
        var received = Assert.IsType<BasicGetResult>(delivery);
        var payload = Encoding.UTF8.GetString(received.Body.Span);
        var expectedMessageId = messageId.ToString("D");
        var causationId = ReadHeader(received, "tinybus-causation-id");
        var serializedHeaders = ReadHeader(received, "tinybus-headers");
        var receivedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);

        Assert.Equal(envelope.Payload, payload);
        Assert.Equal(expectedMessageId, received.BasicProperties.MessageId);
        Assert.Equal(capture.Name, received.BasicProperties.Type);
        Assert.Equal(envelope.CorrelationId, received.BasicProperties.CorrelationId);
        Assert.True(received.BasicProperties.Persistent);
        Assert.Equal(1, received.BasicProperties.Headers!["tinybus-contract-version"]);
        Assert.Equal(envelope.CausationId, causationId);
        Assert.NotNull(receivedHeaders);
        Assert.Equal("north", receivedHeaders["tenant"]);
    }

    [Fact]
    public async Task Send_rejects_an_unroutable_command()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var topology = CreateTopology(service);
        await using var transport = new RabbitMqTransport(rabbitMq.ConnectionString);
        await transport.InitializeAsync(topology);
        var contract = new ContractIdentity($"{scenario}.payments.capture", 1);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(messageId, contract, "{}");

        Task Send()
        {
            var sending = transport.SendAsync(envelope);
            var task = sending.AsTask();

            return task;
        }

        await Assert.ThrowsAnyAsync<PublishException>(Send);
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

    private static string ReadHeader(BasicGetResult delivery, string name)
    {
        var headers = delivery.BasicProperties.Headers;
        var value = Assert.IsType<byte[]>(headers![name]);
        var text = Encoding.UTF8.GetString(value);

        return text;
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
