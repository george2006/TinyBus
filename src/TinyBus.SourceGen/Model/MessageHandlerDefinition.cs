using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageHandlerDefinition : IEquatable<MessageHandlerDefinition>
{
    public MessageHandlerDefinition(
        string contractName,
        string messageTypeName,
        string handlerTypeName,
        MessageHandlerKind kind,
        string? responseTypeName)
    {
        ContractName = contractName;
        MessageTypeName = messageTypeName;
        HandlerTypeName = handlerTypeName;
        Kind = kind;
        ResponseTypeName = responseTypeName;
    }

    public string ContractName { get; }

    public string MessageTypeName { get; }

    public string HandlerTypeName { get; }

    public MessageHandlerKind Kind { get; }

    public string? ResponseTypeName { get; }

    public bool Equals(MessageHandlerDefinition? other)
    {
        return other is not null
            && ContractName == other.ContractName
            && MessageTypeName == other.MessageTypeName
            && HandlerTypeName == other.HandlerTypeName
            && Kind == other.Kind
            && ResponseTypeName == other.ResponseTypeName;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MessageHandlerDefinition);
    }

    public override int GetHashCode()
    {
        return (ContractName, MessageTypeName, HandlerTypeName, Kind, ResponseTypeName).GetHashCode();
    }
}
