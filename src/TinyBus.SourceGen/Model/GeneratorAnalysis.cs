using System.Collections.Immutable;

namespace TinyBus.SourceGen.Model;

internal sealed class GeneratorAnalysis
{
    public GeneratorAnalysis(
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        ImmutableArray<MiddlewareAnalysis> middleware,
        ImmutableArray<ReferencedMessageContribution> contributions)
    {
        AssemblyName = assemblyName;
        Handlers = handlers;
        Middleware = middleware;
        Contributions = contributions;
    }

    public string AssemblyName { get; }

    public ImmutableArray<MessageHandlerAnalysis> Handlers { get; }

    public ImmutableArray<MiddlewareAnalysis> Middleware { get; }

    public ImmutableArray<ReferencedMessageContribution> Contributions { get; }
}
