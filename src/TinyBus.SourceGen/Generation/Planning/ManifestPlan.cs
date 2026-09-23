using System.Collections.Immutable;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class ManifestPlan
{
    public ManifestPlan(ImmutableArray<MessageHandlerDefinition> messages)
    {
        Messages = messages;
    }

    public ImmutableArray<MessageHandlerDefinition> Messages { get; }
}
