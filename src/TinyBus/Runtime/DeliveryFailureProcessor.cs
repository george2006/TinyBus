using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TinyBus;

internal sealed class DeliveryFailureProcessor
{
    private readonly MessageRetryPolicy retryPolicy;
    private readonly ILogger<DeliveryFailureProcessor> logger;

    public DeliveryFailureProcessor(
        MessageRetryPolicy retryPolicy,
        ILogger<DeliveryFailureProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(retryPolicy);
        ArgumentNullException.ThrowIfNull(logger);

        this.retryPolicy = retryPolicy;
        this.logger = logger;
    }

    internal async ValueTask ProcessAsync(
        ITransportDelivery delivery,
        Exception error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(error);

        var attempt = delivery.Attempt;
        EnsureValidAttempt(attempt);

        if (HasReachedMaximumAttempts(attempt))
        {
            await DeadLetterAsync(delivery, error, cancellationToken);
            return;
        }

        var delay = retryPolicy.CalculateDelay(attempt);
        await ScheduleRetryAsync(delivery, error, delay, cancellationToken);
    }

    private bool HasReachedMaximumAttempts(int attempt)
    {
        return attempt >= retryPolicy.MaximumAttempts;
    }

    private async ValueTask ScheduleRetryAsync(
        ITransportDelivery delivery,
        Exception error,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        try
        {
            await delivery.ScheduleRetryAsync(error, delay, cancellationToken);
        }
        catch (Exception exception)
        {
            LogSettlementFailure(delivery, "schedule retry", exception);
        }
    }

    private async ValueTask DeadLetterAsync(
        ITransportDelivery delivery,
        Exception error,
        CancellationToken cancellationToken)
    {
        try
        {
            await delivery.DeadLetterAsync(error, cancellationToken);
        }
        catch (Exception exception)
        {
            LogSettlementFailure(delivery, "dead letter", exception);
        }
    }

    private static void EnsureValidAttempt(int attempt)
    {
        if (attempt <= 0)
        {
            throw new InvalidOperationException(
                "A transport delivery attempt must be greater than zero.");
        }
    }

    private void LogSettlementFailure(
        ITransportDelivery delivery,
        string operation,
        Exception exception)
    {
        var messageId = delivery.MessageId;

        logger.LogError(
            exception,
            "TinyBus message {MessageId} transport settlement '{SettlementOperation}' failed.",
            messageId,
            operation);
    }
}
