using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class IncomingPipelinePlanner
{
    public IncomingPipelinePlan Create(
        ManifestPlan manifest,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localCommands = SelectLocalCommands(manifest.Messages);
        var localTargets = CreateLocalTargets(manifest.ManifestTypeName, localCommands);
        var referencedTargets = CreateReferencedTargets(contributions);
        var commandTargets = OrderTargets(localTargets.AddRange(referencedTargets));

        return new IncomingPipelinePlan(
            manifest.ManifestTypeName,
            localCommands,
            commandTargets);
    }

    private static ImmutableArray<MessageHandlerDefinition> SelectLocalCommands(
        ImmutableArray<MessageHandlerDefinition> messages)
    {
        return messages
            .Where(message => message.Kind == MessageHandlerKind.Command)
            .ToImmutableArray();
    }

    private static ImmutableArray<IncomingCommandTarget> CreateLocalTargets(
        string manifestTypeName,
        ImmutableArray<MessageHandlerDefinition> commands)
    {
        var qualifiedManifestTypeName = $"global::TinyBus.Generated.{manifestTypeName}";

        return commands.Select(command => new IncomingCommandTarget(
                command.ContractName,
                command.ContractVersion,
                qualifiedManifestTypeName))
            .ToImmutableArray();
    }

    private static ImmutableArray<IncomingCommandTarget> CreateReferencedTargets(
        ImmutableArray<ReferencedMessageContribution> contributions)
    {
        return contributions
            .Where(contribution => contribution.Kind == MessageHandlerKind.Command)
            .Select(contribution => new IncomingCommandTarget(
                contribution.ContractName,
                contribution.ContractVersion,
                contribution.ManifestTypeName))
            .ToImmutableArray();
    }

    private static ImmutableArray<IncomingCommandTarget> OrderTargets(
        ImmutableArray<IncomingCommandTarget> targets)
    {
        return targets
            .OrderBy(target => target.ContractName, StringComparer.Ordinal)
            .ThenBy(target => target.ContractVersion)
            .ToImmutableArray();
    }
}
