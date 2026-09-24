using System.Collections.Immutable;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class ManifestPlan
{
    public ManifestPlan(
        string manifestTypeName,
        ImmutableArray<MessageHandlerDefinition> messages,
        ImmutableArray<string> referencedManifestTypeNames)
    {
        ManifestTypeName = manifestTypeName;
        Messages = messages;
        ReferencedManifestTypeNames = referencedManifestTypeNames;
    }

    public string ManifestTypeName { get; }

    public ImmutableArray<MessageHandlerDefinition> Messages { get; }

    public ImmutableArray<string> ReferencedManifestTypeNames { get; }
}
