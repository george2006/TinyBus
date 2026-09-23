using System.Collections.Immutable;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class ManifestPlan
{
    public ManifestPlan(
        string manifestTypeName,
        ImmutableArray<MessageHandlerDefinition> messages)
    {
        ManifestTypeName = manifestTypeName;
        Messages = messages;
    }

    public string ManifestTypeName { get; }

    public ImmutableArray<MessageHandlerDefinition> Messages { get; }
}
