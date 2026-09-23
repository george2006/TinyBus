using System.Collections.Immutable;
using System.Threading;
using TinyBus.SourceGen.Generation.Emission;
using TinyBus.SourceGen.Generation.Planning;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation;

internal sealed class ManifestGeneration
{
    public (string HintName, string Source) Generate(
        ImmutableArray<MessageHandlerDefinition> definitions,
        CancellationToken cancellationToken)
    {
        var plan = new ManifestPlanner().Create(definitions, cancellationToken);
        var source = new ManifestEmitter().Emit(plan, cancellationToken);

        return ("TinyBus.Generated.Manifest.g.cs", source);
    }
}
