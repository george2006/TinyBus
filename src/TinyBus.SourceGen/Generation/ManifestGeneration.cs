using System.Collections.Immutable;
using System.Threading;
using TinyBus.SourceGen.Generation.Emission;
using TinyBus.SourceGen.Generation.Planning;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation;

internal sealed class ManifestGeneration
{
    public (string HintName, string Source) Generate(
        string assemblyName,
        ImmutableArray<MessageHandlerDefinition> definitions,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var planner = new ManifestPlanner();
        var plan = planner.Create(assemblyName, definitions, contributions, cancellationToken);

        var emitter = new ManifestEmitter();
        var source = emitter.Emit(plan, cancellationToken);

        return ("TinyBus.Generated.Manifest.g.cs", source);
    }
}
