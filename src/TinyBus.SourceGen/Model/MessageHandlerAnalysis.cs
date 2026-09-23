using System;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageHandlerAnalysis : IEquatable<MessageHandlerAnalysis>
{
    public MessageHandlerAnalysis(
        string messageDisplayName,
        string? contractName,
        int contractVersion,
        string messageTypeName,
        string handlerTypeName,
        MessageHandlerKind kind,
        string? responseTypeName,
        SourceLocation contractNameLocation,
        SourceLocation contractVersionLocation)
    {
        MessageDisplayName = messageDisplayName;
        ContractName = contractName;
        ContractVersion = contractVersion;
        MessageTypeName = messageTypeName;
        HandlerTypeName = handlerTypeName;
        Kind = kind;
        ResponseTypeName = responseTypeName;
        ContractNameLocation = contractNameLocation;
        ContractVersionLocation = contractVersionLocation;
    }

    public string MessageDisplayName { get; }

    public string? ContractName { get; }

    public int ContractVersion { get; }

    public string MessageTypeName { get; }

    public string HandlerTypeName { get; }

    public MessageHandlerKind Kind { get; }

    public string? ResponseTypeName { get; }

    public SourceLocation ContractNameLocation { get; }

    public SourceLocation ContractVersionLocation { get; }

    public bool Equals(MessageHandlerAnalysis? other)
    {
        return other is not null
            && MessageDisplayName == other.MessageDisplayName
            && ContractName == other.ContractName
            && ContractVersion == other.ContractVersion
            && MessageTypeName == other.MessageTypeName
            && HandlerTypeName == other.HandlerTypeName
            && Kind == other.Kind
            && ResponseTypeName == other.ResponseTypeName
            && ContractNameLocation.Equals(other.ContractNameLocation)
            && ContractVersionLocation.Equals(other.ContractVersionLocation);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as MessageHandlerAnalysis);
    }

    public override int GetHashCode()
    {
        var contractHash = (MessageDisplayName, ContractName, ContractVersion).GetHashCode();
        var typeHash = (MessageTypeName, HandlerTypeName, Kind, ResponseTypeName).GetHashCode();
        return (contractHash, typeHash, ContractNameLocation, ContractVersionLocation).GetHashCode();
    }
}
