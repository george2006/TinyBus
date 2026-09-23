using System.Collections.Immutable;
using System.Linq;

namespace TinyBus.SourceGen.Model;

internal sealed class MessageValidationResult
{
    public MessageValidationResult(
        MessageHandlerAnalysis candidate,
        MessageHandlerDefinition? definition,
        ImmutableArray<MessageIssue> issues)
    {
        Candidate = candidate;
        Definition = definition;
        Issues = issues;
    }

    public MessageHandlerAnalysis Candidate { get; }

    public MessageHandlerDefinition? Definition { get; }

    public ImmutableArray<MessageIssue> Issues { get; }

    public override bool Equals(object? obj)
    {
        return obj is MessageValidationResult other
            && Candidate.Equals(other.Candidate)
            && Equals(Definition, other.Definition)
            && Issues.SequenceEqual(other.Issues);
    }

    public override int GetHashCode()
    {
        var hash = Candidate.GetHashCode();
        hash = unchecked(hash * 31 + (Definition?.GetHashCode() ?? 0));

        foreach (var issue in Issues)
        {
            hash = unchecked(hash * 31 + issue.GetHashCode());
        }

        return hash;
    }
}
