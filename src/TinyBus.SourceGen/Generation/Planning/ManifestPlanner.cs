using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class ManifestPlanner
{
    public ManifestPlan Create(
        ImmutableArray<MessageHandlerDefinition> definitions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messages = definitions
            .OrderBy(definition => definition.ContractName, StringComparer.Ordinal)
            .ThenBy(definition => definition.Kind)
            .ThenBy(definition => definition.HandlerTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new ManifestPlan(messages);
    }
}
