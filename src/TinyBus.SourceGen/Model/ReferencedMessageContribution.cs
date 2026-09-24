using System;

namespace TinyBus.SourceGen.Model;

internal sealed class ReferencedMessageContribution : IEquatable<ReferencedMessageContribution>
{
    public ReferencedMessageContribution(
        string assemblyName,
        string manifestTypeName,
        string contractName,
        int contractVersion,
        string messageTypeName,
        string messageTypeIdentity,
        string handlerTypeName,
        MessageHandlerKind kind,
        string? responseTypeName)
    {
        AssemblyName = assemblyName;
        ManifestTypeName = manifestTypeName;
        ContractName = contractName;
        ContractVersion = contractVersion;
        MessageTypeName = messageTypeName;
        MessageTypeIdentity = messageTypeIdentity;
        HandlerTypeName = handlerTypeName;
        Kind = kind;
        ResponseTypeName = responseTypeName;
    }

    public string AssemblyName { get; }

    public string ManifestTypeName { get; }

    public string ContractName { get; }

    public int ContractVersion { get; }

    public string MessageTypeName { get; }

    public string MessageTypeIdentity { get; }

    public string HandlerTypeName { get; }

    public MessageHandlerKind Kind { get; }

    public string? ResponseTypeName { get; }

    public bool Equals(ReferencedMessageContribution? other)
    {
        return other is not null
            && AssemblyName == other.AssemblyName
            && ManifestTypeName == other.ManifestTypeName
            && ContractName == other.ContractName
            && ContractVersion == other.ContractVersion
            && MessageTypeName == other.MessageTypeName
            && MessageTypeIdentity == other.MessageTypeIdentity
            && HandlerTypeName == other.HandlerTypeName
            && Kind == other.Kind
            && ResponseTypeName == other.ResponseTypeName;
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as ReferencedMessageContribution);
    }

    public override int GetHashCode()
    {
        var originHash = (AssemblyName, ManifestTypeName).GetHashCode();
        var contractHash = (ContractName, ContractVersion).GetHashCode();
        var typeHash = (MessageTypeName, HandlerTypeName, Kind, ResponseTypeName).GetHashCode();

        return (originHash, contractHash, typeHash, MessageTypeIdentity).GetHashCode();
    }
}
