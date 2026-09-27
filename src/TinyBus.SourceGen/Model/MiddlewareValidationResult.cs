using System.Collections.Immutable;

namespace TinyBus.SourceGen.Model;

internal sealed class MiddlewareValidationResult
{
    public MiddlewareValidationResult(
        ImmutableArray<MiddlewareDefinition> definitions,
        ImmutableArray<MiddlewareIssue> issues)
    {
        Definitions = definitions;
        Issues = issues;
    }

    public ImmutableArray<MiddlewareDefinition> Definitions { get; }

    public ImmutableArray<MiddlewareIssue> Issues { get; }
}
