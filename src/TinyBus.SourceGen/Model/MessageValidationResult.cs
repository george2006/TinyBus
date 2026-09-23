using System.Collections.Immutable;
using System.Linq;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageValidationResult
{
    public MessageValidationResult(
        MessageHandlerDefinition? definition,
        ImmutableArray<MessageIssue> issues)
    {
        Definition = definition;
        Issues = issues;
    }

    public MessageHandlerDefinition? Definition { get; }

    public ImmutableArray<MessageIssue> Issues { get; }

    public override bool Equals(object? obj)
    {
        return obj is MessageValidationResult other
            && Equals(Definition, other.Definition)
            && Issues.SequenceEqual(other.Issues);
    }

    public override int GetHashCode()
    {
        var hash = Definition?.GetHashCode() ?? 0;

        foreach (var issue in Issues)
        {
            hash = unchecked(hash * 31 + issue.GetHashCode());
        }

        return hash;
    }
}
