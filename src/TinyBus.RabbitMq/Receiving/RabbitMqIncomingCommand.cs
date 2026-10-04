using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using TinyBus;

namespace TinyBus.RabbitMq.Receiving;

internal sealed record RabbitMqIncomingCommand(
    ulong DeliveryTag,
    int Attempt,
    BasicProperties Properties,
    ReadOnlyMemory<byte> Body)
{
    internal Guid? MessageId
    {
        get
        {
            var parsed = Guid.TryParse(Properties.MessageId, out var messageId);

            return parsed ? messageId : null;
        }
    }

    internal MessageEnvelope ReadEnvelope()
    {
        var messageId = ReadRequiredMessageId();
        var contractName = ReadContractName();
        var contractVersion = ReadContractVersion();
        var payload = Encoding.UTF8.GetString(Body.Span);
        var causationId = ReadTextHeader(RabbitMqHeaderNames.CausationId);
        var headers = ReadHeaders();
        var contract = new ContractIdentity(contractName, contractVersion);
        var envelope = new MessageEnvelope(
            messageId,
            contract,
            payload,
            Properties.CorrelationId,
            causationId,
            headers);

        return envelope;
    }

    private Guid ReadRequiredMessageId()
    {
        var messageId = MessageId;

        if (messageId is null)
        {
            throw new InvalidOperationException(
                "The RabbitMQ delivery does not contain a valid TinyBus message id.");
        }

        return messageId.Value;
    }

    private string ReadContractName()
    {
        if (string.IsNullOrWhiteSpace(Properties.Type))
        {
            throw new InvalidOperationException(
                "The RabbitMQ delivery does not contain a TinyBus contract name.");
        }

        return Properties.Type;
    }

    private int ReadContractVersion()
    {
        var value = ReadRequiredHeader(RabbitMqHeaderNames.ContractVersion);
        var version = Convert.ToInt32(value, CultureInfo.InvariantCulture);

        return version;
    }

    private IReadOnlyDictionary<string, string>? ReadHeaders()
    {
        var serializedHeaders = ReadTextHeader(RabbitMqHeaderNames.MessageHeaders);

        if (serializedHeaders is null)
        {
            return null;
        }

        var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedHeaders);

        return headers;
    }

    private string? ReadTextHeader(string name)
    {
        var headers = Properties.Headers;

        if (headers is null || !headers.TryGetValue(name, out var value))
        {
            return null;
        }

        if (value is byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes);
        }

        if (value is ReadOnlyMemory<byte> memory)
        {
            return Encoding.UTF8.GetString(memory.Span);
        }

        if (value is string textValue)
        {
            return textValue;
        }

        return Convert.ToString(value, CultureInfo.InvariantCulture);
    }

    private object ReadRequiredHeader(string name)
    {
        var headers = Properties.Headers;

        if (headers is null || !headers.TryGetValue(name, out var value) || value is null)
        {
            throw new InvalidOperationException(
                $"The RabbitMQ delivery does not contain the required '{name}' header.");
        }

        return value;
    }
}
