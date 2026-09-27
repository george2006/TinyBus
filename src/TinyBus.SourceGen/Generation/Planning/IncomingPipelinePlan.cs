using System.Collections.Immutable;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class IncomingPipelinePlan
{
    public IncomingPipelinePlan(
        string localManifestTypeName,
        ImmutableArray<MessageHandlerDefinition> localCommands,
        ImmutableArray<MiddlewareDefinition> middleware,
        ImmutableArray<IncomingCommandTarget> commandTargets)
    {
        LocalManifestTypeName = localManifestTypeName;
        LocalCommands = localCommands;
        Middleware = middleware;
        CommandTargets = commandTargets;
    }

    public string LocalManifestTypeName { get; }

    public ImmutableArray<MessageHandlerDefinition> LocalCommands { get; }

    public ImmutableArray<MiddlewareDefinition> Middleware { get; }

    public ImmutableArray<IncomingCommandTarget> CommandTargets { get; }
}

internal sealed class IncomingCommandTarget
{
    public IncomingCommandTarget(
        string contractName,
        int contractVersion,
        string manifestTypeName)
    {
        ContractName = contractName;
        ContractVersion = contractVersion;
        ManifestTypeName = manifestTypeName;
    }

    public string ContractName { get; }

    public int ContractVersion { get; }

    public string ManifestTypeName { get; }
}
