using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed class RabbitMqTopologyJournal
{
    private const string JournalName = "tinybus.topology";
    private const string StreamOffsetArgument = "x-stream-offset";
    private const string StreamTypeArgument = "x-queue-type";

    private readonly IConnection connection;

    public RabbitMqTopologyJournal(IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        this.connection = connection;
    }

    public async ValueTask ReconcileAsync(
        ServiceTopology topology,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(topology);

        var declaration = TopologyDeclaration.Create(topology);
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection
            .CreateChannelAsync(channelOptions, cancellationToken)
            .ConfigureAwait(false);

        await DeclareJournalAsync(channel, cancellationToken).ConfigureAwait(false);
        await channel.BasicQosAsync(0, 100, false, cancellationToken).ConfigureAwait(false);

        var consumer = new TopologyReplayConsumer(channel, declaration.Id);
        var consumerTag = await StartReplayAsync(channel, consumer, cancellationToken)
            .ConfigureAwait(false);

        await PublishAsync(channel, declaration, cancellationToken).ConfigureAwait(false);
        var result = await consumer.Completion.WaitAsync(cancellationToken).ConfigureAwait(false);

        await channel.BasicCancelAsync(consumerTag, false, cancellationToken).ConfigureAwait(false);
        ThrowIfRejected(result);
    }

    private static async ValueTask DeclareJournalAsync(
        IChannel channel,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            [StreamTypeArgument] = "stream"
        };

        await channel.QueueDeclareAsync(
            JournalName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments,
            noWait: false,
            cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<string> StartReplayAsync(
        IChannel channel,
        TopologyReplayConsumer consumer,
        CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>
        {
            [StreamOffsetArgument] = "first"
        };

        var consumerTag = await channel.BasicConsumeAsync(
            JournalName,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments,
            consumer,
            cancellationToken).ConfigureAwait(false);

        return consumerTag;
    }

    private static async ValueTask PublishAsync(
        IChannel channel,
        TopologyDeclaration declaration,
        CancellationToken cancellationToken)
    {
        var body = TopologyDeclarationSerializer.Serialize(declaration);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: JournalName,
            mandatory: true,
            body,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ThrowIfRejected(TopologyDeclarationResult result)
    {
        if (result.IsAccepted)
        {
            return;
        }

        var conflict = result.Conflict!;
        var owners = new[] { conflict.ExistingOwner.Value, conflict.CandidateOwner.Value };
        Array.Sort(owners, StringComparer.Ordinal);

        var message = $"Command '{conflict.Contract.Name}' version {conflict.Contract.Version} "
            + $"has conflicting owners '{owners[0]}' and '{owners[1]}'.";
        var error = new InvalidOperationException(message);
        throw error;
    }
}
