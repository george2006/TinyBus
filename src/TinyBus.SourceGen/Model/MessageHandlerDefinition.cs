using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageHandlerDefinition : IEquatable<MessageHandlerDefinition>
{
    public MessageHandlerDefinition(
        string contractName,
        string messageTypeName,
        string handlerTypeName)
    {
        ContractName = contractName;
        MessageTypeName = messageTypeName;
        HandlerTypeName = handlerTypeName;
    }

    public string ContractName { get; }

    public string MessageTypeName { get; }

    public string HandlerTypeName { get; }

    public bool Equals(MessageHandlerDefinition? other)
    {
        return other is not null
            && ContractName == other.ContractName
            && MessageTypeName == other.MessageTypeName
            && HandlerTypeName == other.HandlerTypeName;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MessageHandlerDefinition);
    }

    public override int GetHashCode()
    {
        return (ContractName, MessageTypeName, HandlerTypeName).GetHashCode();
    }
}
