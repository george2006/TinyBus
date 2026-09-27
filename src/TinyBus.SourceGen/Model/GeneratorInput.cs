using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Model;

internal sealed class GeneratorInput
{
    public GeneratorInput(
        Compilation compilation,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        ImmutableArray<MiddlewareAnalysis> middleware)
    {
        Compilation = compilation;
        Handlers = handlers;
        Middleware = middleware;
    }

    public Compilation Compilation { get; }

    public ImmutableArray<MessageHandlerAnalysis> Handlers { get; }

    public ImmutableArray<MiddlewareAnalysis> Middleware { get; }
}
