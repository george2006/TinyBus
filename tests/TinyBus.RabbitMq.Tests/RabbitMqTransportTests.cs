using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
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

        await using (var newerTransport = CreateTransport())
        {
            await newerTransport.InitializeAsync(newerTopology);
        }

        await using (var olderTransport = CreateTransport())
        {
            await olderTransport.InitializeAsync(olderTopology);
        }

        await rabbitMq.RestartAsync();

        await using var restartedTransport = CreateTransport();
        await restartedTransport.InitializeAsync(olderTopology);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: false,
            publisherConfirmationTrackingEnabled: false);
        await using var channel = await connection.CreateChannelAsync(channelOptions);
        var captureAddress = CommandAddress.From(capture);
        var refundAddress = CommandAddress.From(refund);
        var serviceAddress = ServiceAddress.From(service);
        var expectedQueueName = "tinybus." + service.Value;
        var expectedDeadLetterQueueName = expectedQueueName + ".dead-letter";

        Assert.Equal(expectedQueueName, serviceAddress.QueueName);
        Assert.Equal(expectedDeadLetterQueueName, serviceAddress.DeadLetterQueueName);

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

        await using var paymentsTransport = CreateTransport();
        await paymentsTransport.InitializeAsync(payments);

        await using var checkoutTransport = CreateTransport();
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
        await using var transport = CreateTransport();
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
        var causationId = ReadHeader(received, RabbitMqHeaderNames.CausationId);
        var serializedHeaders = ReadHeader(received, RabbitMqHeaderNames.MessageHeaders);
        var receivedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);

        Assert.Equal(envelope.Payload, payload);
        Assert.Equal(expectedMessageId, received.BasicProperties.MessageId);
        Assert.Equal(capture.Name, received.BasicProperties.Type);
        Assert.Equal(envelope.CorrelationId, received.BasicProperties.CorrelationId);
        Assert.True(received.BasicProperties.Persistent);
        Assert.Equal(
            1,
            received.BasicProperties.Headers![RabbitMqHeaderNames.ContractVersion]);
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
        await using var transport = CreateTransport();
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

    [Fact]
    public async Task Receive_honors_available_capacity_and_complete_acknowledges_the_delivery()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        await using var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var first = new MessageEnvelope(
            Guid.NewGuid(),
            capture,
            "{\"number\":1}",
            "correlation-1",
            "causation-1",
            headers);
        var second = new MessageEnvelope(Guid.NewGuid(), capture, "{\"number\":2}");
        await transport.SendAsync(first);
        await transport.SendAsync(second);
        var capacity = new ReceiveCapacity(maximum: 8, available: 1);

        var firstBatch = await transport.ReceiveAsync(capacity);

        var firstDelivery = Assert.Single(firstBatch);
        Assert.Equal(1, firstDelivery.Attempt);
        AssertEnvelope(first, firstDelivery.ReadEnvelope());
        await firstDelivery.CompleteAsync();

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        Assert.Equal(1, secondDelivery.Attempt);
        AssertEnvelope(second, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-number")]
    public async Task Invalid_contract_version_remains_settleable(string? contractVersion)
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        var retryDelay = TimeSpan.Zero;
        var retryPolicy = new MessageRetryPolicy(2, retryDelay, retryDelay);
        await using var transport = CreateTransport(retryPolicy);
        await transport.InitializeAsync(topology);
        var messageId = Guid.NewGuid();

        await using var connection = await rabbitMq.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var address = CommandAddress.From(capture);
        await PublishInvalidContractVersionAsync(
            channel,
            address,
            messageId,
            capture.Name,
            contractVersion);
        var capacity = new ReceiveCapacity(maximum: 1, available: 1);

        var firstBatch = await transport.ReceiveAsync(capacity);
        var firstDelivery = Assert.Single(firstBatch);
        var firstError = Assert.ThrowsAny<Exception>(firstDelivery.ReadEnvelope);
        Assert.Equal(messageId, firstDelivery.MessageId);
        Assert.Equal(1, firstDelivery.Attempt);
        await firstDelivery.ScheduleRetryAsync(firstError, retryDelay);

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        var secondError = Assert.ThrowsAny<Exception>(secondDelivery.ReadEnvelope);
        Assert.Equal(messageId, secondDelivery.MessageId);
        Assert.Equal(2, secondDelivery.Attempt);
        await secondDelivery.DeadLetterAsync(secondError);

        var serviceAddress = ServiceAddress.From(service);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var deadLetter = await ReadDeadLetterAsync(
            channel,
            serviceAddress,
            cancellation.Token);
        var failedAttempt = deadLetter.BasicProperties.Headers![
            RabbitMqHeaderNames.FailedAttempt];
        var expectedMessageId = messageId.ToString("D");

        Assert.Equal(expectedMessageId, deadLetter.BasicProperties.MessageId);
        Assert.Equal(2, failedAttempt);
    }

    [Fact]
    public async Task Abandon_requeues_the_delivery()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        await using var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var envelope = new MessageEnvelope(Guid.NewGuid(), capture, "{}");
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);

        var firstBatch = await transport.ReceiveAsync(capacity);
        var firstDelivery = Assert.Single(firstBatch);
        Assert.Equal(1, firstDelivery.Attempt);
        await firstDelivery.AbandonAsync();

        var secondBatch = await transport.ReceiveAsync(capacity);
        var secondDelivery = Assert.Single(secondBatch);
        Assert.Equal(1, secondDelivery.Attempt);
        AssertEnvelope(envelope, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();
    }

    [Fact]
    public async Task Scheduled_retry_becomes_available_as_the_next_attempt()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        var retryDelay = TimeSpan.FromSeconds(1);
        var retryPolicy = new MessageRetryPolicy(3, retryDelay, retryDelay);
        await using var transport = CreateTransport(retryPolicy);
        await transport.InitializeAsync(topology);
        var envelope = new MessageEnvelope(Guid.NewGuid(), capture, "{}");
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);
        var firstBatch = await transport.ReceiveAsync(capacity);
        var firstDelivery = Assert.Single(firstBatch);
        var processingError = new InvalidOperationException("Handler failed.");

        Assert.Equal(1, firstDelivery.Attempt);
        await firstDelivery.ScheduleRetryAsync(processingError, retryDelay);

        using var earlyCancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(200));
        var earlyReceive = transport.ReceiveAsync(capacity, earlyCancellation.Token).AsTask();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => earlyReceive);

        using var retryCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var secondBatch = await transport.ReceiveAsync(capacity, retryCancellation.Token);
        var secondDelivery = Assert.Single(secondBatch);
        Assert.Equal(2, secondDelivery.Attempt);
        AssertEnvelope(envelope, secondDelivery.ReadEnvelope());
        await secondDelivery.CompleteAsync();
    }

    [Fact]
    public async Task Dead_letter_routes_the_delivery_to_the_service_dead_letter_queue()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        await using var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var headers = new Dictionary<string, string> { ["tenant"] = "north" };
        var envelope = new MessageEnvelope(
            Guid.NewGuid(),
            capture,
            "{\"amount\":100}",
            "correlation-1",
            "causation-1",
            headers);
        await transport.SendAsync(envelope);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);
        var batch = await transport.ReceiveAsync(capacity);
        var delivery = Assert.Single(batch);
        var processingError = new InvalidOperationException("Handler failed.");

        await delivery.DeadLetterAsync(processingError);

        await using var connection = await rabbitMq.OpenConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var serviceAddress = ServiceAddress.From(service);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var deadLetter = await ReadDeadLetterAsync(
            channel,
            serviceAddress,
            cancellation.Token);
        var payload = Encoding.UTF8.GetString(deadLetter.Body.Span);
        var messageId = envelope.MessageId.ToString("D");
        var causationId = ReadHeader(deadLetter, RabbitMqHeaderNames.CausationId);
        var serializedHeaders = ReadHeader(deadLetter, RabbitMqHeaderNames.MessageHeaders);
        var receivedHeaders = JsonSerializer.Deserialize<Dictionary<string, string>>(
            serializedHeaders);
        var failedQueue = ReadHeader(deadLetter, RabbitMqHeaderNames.FailedQueue);
        var exceptionType = ReadHeader(deadLetter, RabbitMqHeaderNames.ExceptionType);
        var exceptionMessage = ReadHeader(deadLetter, RabbitMqHeaderNames.ExceptionMessage);
        var exceptionDetails = ReadHeader(deadLetter, RabbitMqHeaderNames.ExceptionDetails);
        var expectedExceptionType = typeof(InvalidOperationException).FullName;
        var failedAttempt = deadLetter.BasicProperties.Headers![
            RabbitMqHeaderNames.FailedAttempt];
        var activeDelivery = await channel.BasicGetAsync(
            serviceAddress.QueueName,
            autoAck: true);

        Assert.Null(activeDelivery);
        Assert.Equal(envelope.Payload, payload);
        Assert.Equal(messageId, deadLetter.BasicProperties.MessageId);
        Assert.Equal(capture.Name, deadLetter.BasicProperties.Type);
        Assert.Equal(envelope.CorrelationId, deadLetter.BasicProperties.CorrelationId);
        Assert.Equal(
            1,
            deadLetter.BasicProperties.Headers![RabbitMqHeaderNames.ContractVersion]);
        Assert.Equal(envelope.CausationId, causationId);
        Assert.NotNull(receivedHeaders);
        Assert.Equal("north", receivedHeaders["tenant"]);
        Assert.Equal(serviceAddress.QueueName, failedQueue);
        Assert.Equal(1, failedAttempt);
        Assert.Equal(expectedExceptionType, exceptionType);
        Assert.Equal(processingError.Message, exceptionMessage);
        Assert.Contains(processingError.Message, exceptionDetails);
    }

    [Fact]
    public async Task Receive_waits_for_work_and_observes_cancellation()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var topology = CreateTopology(service);
        await using var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var capacity = new ReceiveCapacity(maximum: 4, available: 4);
        using var cancellation = new CancellationTokenSource();

        var receiving = transport.ReceiveAsync(capacity, cancellation.Token).AsTask();

        Assert.False(receiving.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiving);
    }

    [Fact]
    public async Task Receive_resumes_after_a_transient_provider_outage()
    {
        var scenarioId = Guid.NewGuid();
        var scenario = scenarioId.ToString("N");
        var service = new ServiceIdentity($"payments-{scenario}");
        var capture = new ContractIdentity($"{scenario}.payments.capture", 1);
        var topology = CreateTopology(service, capture);
        await using var transport = CreateTransport();
        await transport.InitializeAsync(topology);
        var capacity = new ReceiveCapacity(maximum: 4, available: 1);
        var scenarioTimeout = TimeSpan.FromSeconds(45);
        using var cancellation = new CancellationTokenSource(scenarioTimeout);
        var receiving = transport.ReceiveAsync(capacity, cancellation.Token).AsTask();

        await rabbitMq.PauseProviderAsync();

        try
        {
            var outageObservationDelay = TimeSpan.FromSeconds(12);
            await Task.Delay(outageObservationDelay, cancellation.Token);

            Assert.False(receiving.IsCompleted);
        }
        finally
        {
            await rabbitMq.ResumeProviderAsync();
        }

        await WaitForProviderAsync(cancellation.Token);
        var messageId = Guid.NewGuid();
        var envelope = new MessageEnvelope(messageId, capture, "{}");
        await SendWhenRecoveredAsync(transport, envelope, cancellation.Token);

        var deliveries = await receiving;
        var delivery = Assert.Single(deliveries);
        AssertEnvelope(envelope, delivery.ReadEnvelope());
        await delivery.CompleteAsync(cancellation.Token);
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

    private static async Task PublishInvalidContractVersionAsync(
        IChannel channel,
        CommandAddress address,
        Guid messageId,
        string contractName,
        string? contractVersion)
    {
        var headers = new Dictionary<string, object?>();

        if (contractVersion is not null)
        {
            headers[RabbitMqHeaderNames.ContractVersion] = contractVersion;
        }

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString("D"),
            Type = contractName,
            Persistent = true,
            Headers = headers
        };
        var body = Encoding.UTF8.GetBytes("{}");

        await channel.BasicPublishAsync(
            address.Exchange,
            address.RoutingKey,
            mandatory: true,
            properties,
            body);
    }

    private async Task WaitForProviderAsync(CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(200);

        while (true)
        {
            try
            {
                await using var connection = await rabbitMq.OpenConnectionAsync();
                return;
            }
            catch (BrokerUnreachableException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private static async Task SendWhenRecoveredAsync(
        RabbitMqTransport transport,
        MessageEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var retryDelay = TimeSpan.FromMilliseconds(200);

        while (true)
        {
            try
            {
                await transport.SendAsync(envelope, cancellationToken);
                return;
            }
            catch (RabbitMQClientException) when (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(retryDelay, cancellationToken);
            }
        }
    }

    private static async Task<string> ReadAsync(
        IChannel channel,
        ServiceAddress serviceAddress)
    {
        var pollingInterval = TimeSpan.FromMilliseconds(50);
        var timeout = TimeSpan.FromSeconds(5);
        using var cancellation = new CancellationTokenSource(timeout);

        while (true)
        {
            var delivery = await channel.BasicGetAsync(
                serviceAddress.QueueName,
                autoAck: true,
                cancellation.Token);

            if (delivery is not null)
            {
                var value = Encoding.UTF8.GetString(delivery.Body.Span);

                return value;
            }

            await Task.Delay(pollingInterval, cancellation.Token);
        }
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

    private static Task InitializeAsync(
        RabbitMqTransport transport,
        ServiceTopology topology)
    {
        var initialization = transport.InitializeAsync(topology);
        var task = initialization.AsTask();

        return task;
    }

    private RabbitMqTransport CreateTransport()
    {
        var minimumDelay = TimeSpan.FromSeconds(1);
        var maximumDelay = TimeSpan.FromSeconds(30);
        var retryPolicy = new MessageRetryPolicy(
            5,
            minimumDelay,
            maximumDelay);
        var transport = CreateTransport(retryPolicy);

        return transport;
    }

    private RabbitMqTransport CreateTransport(MessageRetryPolicy retryPolicy)
    {
        var logger = NullLogger<RabbitMqTransport>.Instance;
        var transport = new RabbitMqTransport(
            rabbitMq.ConnectionString,
            retryPolicy,
            logger);

        return transport;
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

    private static void AssertEnvelope(
        MessageEnvelope expected,
        MessageEnvelope actual)
    {
        Assert.Equal(expected.MessageId, actual.MessageId);
        Assert.Equal(expected.Contract, actual.Contract);
        Assert.Equal(expected.Payload, actual.Payload);
        Assert.Equal(expected.CorrelationId, actual.CorrelationId);
        Assert.Equal(expected.CausationId, actual.CausationId);

        if (expected.Headers is null)
        {
            Assert.Null(actual.Headers);
            return;
        }

        Assert.NotNull(actual.Headers);
        Assert.Equal(expected.Headers, actual.Headers);
    }

    private sealed class TestCommand;

    private sealed class TestCommandHandler;
}
