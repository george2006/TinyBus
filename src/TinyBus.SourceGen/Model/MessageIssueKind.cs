namespace TinyBus.SourceGen.Model;

internal enum MessageIssueKind
{
    InvalidContractName,
    InvalidContractVersion,
    DuplicateCommandHandler,
    DuplicateRequestHandler,
    ConflictingMessageSemantics,
    AmbiguousContractIdentity
}
