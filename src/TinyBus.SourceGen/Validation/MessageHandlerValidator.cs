using System.Collections.Immutable;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Validation;

internal sealed class MessageHandlerValidator
{
    public MessageValidationResult Validate(MessageHandlerAnalysis candidate)
    {
        var issues = ReadIssues(candidate);
        var definition = issues.IsEmpty ? CreateDefinition(candidate) : null;

        return new MessageValidationResult(candidate, definition, issues);
    }

    private static ImmutableArray<MessageIssue> ReadIssues(MessageHandlerAnalysis candidate)
    {
        var contract = candidate.Contract;
        var issues = ImmutableArray.CreateBuilder<MessageIssue>();

        if (string.IsNullOrWhiteSpace(contract.Name))
        {
            var issue = new MessageIssue(
                MessageIssueKind.InvalidContractName,
                candidate.MessageType.DisplayName,
                contract.NameLocation);
            issues.Add(issue);
        }

        if (contract.Version < 1)
        {
            var issue = new MessageIssue(
                MessageIssueKind.InvalidContractVersion,
                candidate.MessageType.DisplayName,
                contract.VersionLocation);
            issues.Add(issue);
        }

        return issues.ToImmutable();
    }

    private static MessageHandlerDefinition CreateDefinition(MessageHandlerAnalysis candidate)
    {
        return new MessageHandlerDefinition(
            candidate.Contract.Name!,
            candidate.Contract.Version,
            candidate.MessageType.TypeName,
            candidate.HandlerTypeName,
            candidate.Kind,
            candidate.ResponseTypeName);
    }
}
