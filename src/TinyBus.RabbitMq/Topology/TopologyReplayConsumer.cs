using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TinyBus.RabbitMq;

internal sealed class TopologyReplayConsumer : AsyncDefaultBasicConsumer
{
    private readonly TopologyAccumulator accumulator = new();
    private readonly TaskCompletionSource<TopologyDeclarationResult> completion;
    private readonly Guid targetDeclarationId;

    public TopologyReplayConsumer(IChannel channel, Guid targetDeclarationId)
        : base(channel)
    {
        this.targetDeclarationId = targetDeclarationId;
        completion = new TaskCompletionSource<TopologyDeclarationResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public Task<TopologyDeclarationResult> Completion => completion.Task;

    public override async Task HandleBasicDeliverAsync(
        string consumerTag,
        ulong deliveryTag,
        bool redelivered,
        string exchange,
        string routingKey,
        IReadOnlyBasicProperties properties,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var declaration = TopologyDeclarationSerializer.Deserialize(body.Span);
            var result = accumulator.Apply(declaration);

            await Channel.BasicAckAsync(deliveryTag, false, cancellationToken).ConfigureAwait(false);

            if (declaration.Id == targetDeclarationId)
            {
                completion.TrySetResult(result);
            }
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    public override Task HandleChannelShutdownAsync(object channel, ShutdownEventArgs reason)
    {
        var message = $"RabbitMQ topology replay stopped: {reason.ReplyText}";
        var error = new InvalidOperationException(message);
        completion.TrySetException(error);

        return Task.CompletedTask;
    }
}
