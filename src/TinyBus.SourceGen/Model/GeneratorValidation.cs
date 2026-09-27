using System.Collections.Immutable;

namespace TinyBus.SourceGen.Model;

internal sealed class GeneratorValidation
{
    public GeneratorValidation(
        ImmutableArray<MessageHandlerDefinition> messages,
        ImmutableArray<MiddlewareDefinition> middleware,
        ImmutableArray<MessageIssue> messageIssues,
        ImmutableArray<MiddlewareIssue> middlewareIssues)
    {
        Messages = messages;
        Middleware = middleware;
        MessageIssues = messageIssues;
        MiddlewareIssues = middlewareIssues;
    }

    public ImmutableArray<MessageHandlerDefinition> Messages { get; }

    public ImmutableArray<MiddlewareDefinition> Middleware { get; }

    public ImmutableArray<MessageIssue> MessageIssues { get; }

    public ImmutableArray<MiddlewareIssue> MiddlewareIssues { get; }
}
