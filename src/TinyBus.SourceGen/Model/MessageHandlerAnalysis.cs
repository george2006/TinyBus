using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageHandlerAnalysis : IEquatable<MessageHandlerAnalysis>
{
    public MessageHandlerAnalysis(
        MessageTypeAnalysis messageType,
        ContractAnalysis contract,
        string handlerTypeName,
        MessageHandlerKind kind,
        string? responseTypeName,
        SourceLocation handlerLocation)
    {
        MessageType = messageType;
        Contract = contract;
        HandlerTypeName = handlerTypeName;
        Kind = kind;
        ResponseTypeName = responseTypeName;
        HandlerLocation = handlerLocation;
    }

    public MessageTypeAnalysis MessageType { get; }

    public ContractAnalysis Contract { get; }

    public string HandlerTypeName { get; }

    public MessageHandlerKind Kind { get; }

    public string? ResponseTypeName { get; }

    public SourceLocation HandlerLocation { get; }

    public bool Equals(MessageHandlerAnalysis? other)
    {
        return other is not null
            && MessageType.Equals(other.MessageType)
            && Contract.Equals(other.Contract)
            && HandlerTypeName == other.HandlerTypeName
            && Kind == other.Kind
            && ResponseTypeName == other.ResponseTypeName
            && HandlerLocation.Equals(other.HandlerLocation);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MessageHandlerAnalysis);
    }

    public override int GetHashCode()
    {
        return (MessageType, Contract, HandlerTypeName, Kind, ResponseTypeName, HandlerLocation).GetHashCode();
    }
}
