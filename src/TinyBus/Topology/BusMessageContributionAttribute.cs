using System;
using System.ComponentModel;

namespace TinyBus;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class BusMessageContributionAttribute : Attribute
{
    public BusMessageContributionAttribute(
        Type manifestType,
        string contractName,
        int contractVersion,
        Type messageType,
        Type handlerType,
        MessageKind kind,
        Type? responseType = null)
    {
        ManifestType = manifestType;
        ContractName = contractName;
        ContractVersion = contractVersion;
        MessageType = messageType;
        HandlerType = handlerType;
        Kind = kind;
        ResponseType = responseType;
    }

    public Type ManifestType { get; }

    public string ContractName { get; }

    public int ContractVersion { get; }

    public Type MessageType { get; }

    public Type HandlerType { get; }

    public MessageKind Kind { get; }

    public Type? ResponseType { get; }
}
