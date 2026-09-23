namespace TinyBus.SourceGen.Model;

internal sealed class MessageIssue
{
    public MessageIssue(
        MessageIssueKind kind,
        string messageDisplayName,
        SourceLocation location)
    {
        Kind = kind;
        MessageDisplayName = messageDisplayName;
        Location = location;
    }

    public MessageIssueKind Kind { get; }

    public string MessageDisplayName { get; }

    public SourceLocation Location { get; }

    public override bool Equals(object? obj)
    {
        return obj is MessageIssue other
            && Kind == other.Kind
            && MessageDisplayName == other.MessageDisplayName
            && Location.Equals(other.Location);
    }

    public override int GetHashCode()
    {
        return (Kind, MessageDisplayName, Location).GetHashCode();
    }
}
