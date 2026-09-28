using Microsoft.Extensions.Logging.Abstractions;

namespace TinyBus.Tests;

public sealed class DeliveryFailureProcessorTests
{
    [Fact]
    public async Task Schedules_the_next_attempt_with_the_policy_delay()
    {
        var minimumDelay = TimeSpan.FromSeconds(2);
        var maximumDelay = TimeSpan.FromSeconds(3);
        var policy = new MessageRetryPolicy(
            3,
            minimumDelay,
            maximumDelay);
        var logger = NullLogger<DeliveryFailureProcessor>.Instance;
        var processor = new DeliveryFailureProcessor(policy, logger);
        var envelope = CreateEnvelope();
        var delivery = new NativeTestDelivery(envelope, attempt: 2);
        var processingError = new InvalidOperationException("Handler failed.");

        await processor.ProcessAsync(delivery, processingError, CancellationToken.None);

        var retry = await delivery.RetryScheduled;
        Assert.Same(processingError, retry.Error);
        Assert.Equal(maximumDelay, retry.Delay);
        Assert.False(delivery.DeadLettered.IsCompleted);
    }

    [Fact]
    public async Task Dead_letters_the_last_allowed_attempt()
    {
        var minimumDelay = TimeSpan.FromSeconds(2);
        var maximumDelay = TimeSpan.FromSeconds(3);
        var policy = new MessageRetryPolicy(
            3,
            minimumDelay,
            maximumDelay);
        var logger = NullLogger<DeliveryFailureProcessor>.Instance;
        var processor = new DeliveryFailureProcessor(policy, logger);
        var envelope = CreateEnvelope();
        var delivery = new NativeTestDelivery(envelope, attempt: 3);
        var processingError = new InvalidOperationException("Handler failed.");

        await processor.ProcessAsync(delivery, processingError, CancellationToken.None);

        var deadLetterError = await delivery.DeadLettered;
        Assert.Same(processingError, deadLetterError);
        Assert.False(delivery.RetryScheduled.IsCompleted);
    }

    private static MessageEnvelope CreateEnvelope()
    {
        var messageId = Guid.NewGuid();
        var contract = new ContractIdentity("payments.capture", 1);
        var envelope = new MessageEnvelope(messageId, contract, "{}");

        return envelope;
    }
}
