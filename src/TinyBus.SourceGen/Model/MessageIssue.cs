namespace TinyBus.SourceGen.Model;

internal sealed class MessageIssue
{
    public MessageIssue(
        MessageIssueKind kind,
        string messageDisplayName,
        SourceLocation? location,
        string handlerDetails = "")
    {
        Kind = kind;
        MessageDisplayName = messageDisplayName;
        Location = location;
        HandlerDetails = handlerDetails;
    }

    public MessageIssueKind Kind { get; }

    public string MessageDisplayName { get; }

    public SourceLocation? Location { get; }

    public string HandlerDetails { get; }

    public override bool Equals(object? obj)
    {
        return obj is MessageIssue other
            && Kind == other.Kind
            && MessageDisplayName == other.MessageDisplayName
            && Equals(Location, other.Location)
            && HandlerDetails == other.HandlerDetails;
    }

    public override int GetHashCode()
    {
        return (Kind, MessageDisplayName, Location, HandlerDetails).GetHashCode();
    }
}
