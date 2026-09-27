using System.Collections.Immutable;
using System.Threading;
using TinyBus.SourceGen.Generation.Emission;
using TinyBus.SourceGen.Generation.Planning;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation;

internal sealed class SourceGeneration
{
    public ImmutableArray<(string HintName, string Source)> Generate(
        string assemblyName,
        ImmutableArray<MessageHandlerDefinition> definitions,
        ImmutableArray<MiddlewareDefinition> middleware,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var manifestPlanner = new ManifestPlanner();
        var manifestPlan = manifestPlanner.Create(
            assemblyName, definitions, contributions, cancellationToken);

        var pipelinePlanner = new IncomingPipelinePlanner();
        var pipelinePlan = pipelinePlanner.Create(
            manifestPlan, middleware, contributions, cancellationToken);

        var manifestEmitter = new ManifestEmitter();
        var manifestSource = manifestEmitter.Emit(manifestPlan, cancellationToken);

        var pipelineEmitter = new IncomingPipelineEmitter();
        var pipelineSource = pipelineEmitter.Emit(pipelinePlan, cancellationToken);

        return ImmutableArray.Create(
            ("TinyBus.Generated.Manifest.g.cs", manifestSource),
            ("TinyBus.Generated.IncomingPipeline.g.cs", pipelineSource));
    }
}
