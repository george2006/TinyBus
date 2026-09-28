using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace TinyBus.RabbitMq.Receiving;

internal sealed class RabbitMqDeadLetterPublisher
{
    private readonly IConnection connection;
    private readonly ServiceAddress serviceAddress;

    internal RabbitMqDeadLetterPublisher(
        IConnection connection,
        ServiceAddress serviceAddress)
    {
        this.connection = connection;
        this.serviceAddress = serviceAddress;
    }

    internal async ValueTask PublishAsync(
        RabbitMqIncomingCommand message,
        Exception error,
        CancellationToken cancellationToken)
    {
        var properties = CreateProperties(message, error);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);

        await channel.BasicPublishAsync(
            ServiceAddress.DeadLetterExchangeName,
            serviceAddress.QueueName,
            mandatory: true,
            properties,
            message.Body,
            cancellationToken).ConfigureAwait(false);
    }

    private BasicProperties CreateProperties(
        RabbitMqIncomingCommand message,
        Exception error)
    {
        var properties = new BasicProperties(message.Properties);
        var headers = CopyHeaders(message.Properties.Headers);
        var errorType = error.GetType();
        var errorTypeName = errorType.FullName ?? errorType.Name;
        var errorMessage = error.Message;
        var errorDetails = error.ToString();

        headers[RabbitMqHeaderNames.FailedQueue] = serviceAddress.QueueName;
        headers[RabbitMqHeaderNames.FailedAttempt] = message.Attempt;
        headers[RabbitMqHeaderNames.ExceptionType] = errorTypeName;
        headers[RabbitMqHeaderNames.ExceptionMessage] = errorMessage;
        headers[RabbitMqHeaderNames.ExceptionDetails] = errorDetails;
        properties.Headers = headers;
        properties.Persistent = true;

        return properties;
    }

    private static Dictionary<string, object?> CopyHeaders(
        IDictionary<string, object?>? source)
    {
        if (source is null)
        {
            return [];
        }

        var headers = new Dictionary<string, object?>(source);

        return headers;
    }
}
