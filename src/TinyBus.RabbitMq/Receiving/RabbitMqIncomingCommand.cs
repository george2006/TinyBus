using System;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed record RabbitMqIncomingCommand(
    ulong DeliveryTag,
    int Attempt,
    MessageEnvelope Envelope,
    BasicProperties Properties,
    ReadOnlyMemory<byte> Body);
