namespace TinyBus.SourceGen.Model;

internal sealed class MiddlewareIssue
{
    public MiddlewareIssue(
        MiddlewareIssueKind kind,
        string subject,
        SourceLocation location,
        string details = "")
    {
        Kind = kind;
        Subject = subject;
        Location = location;
        Details = details;
    }

    public MiddlewareIssueKind Kind { get; }

    public string Subject { get; }

    public SourceLocation Location { get; }

    public string Details { get; }
}
