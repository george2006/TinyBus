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
        var issues = ImmutableArray.CreateBuilder<MessageIssue>();

        if (string.IsNullOrWhiteSpace(candidate.ContractName))
        {
            issues.Add(new MessageIssue(
                MessageIssueKind.InvalidContractName,
                candidate.MessageDisplayName,
                candidate.ContractNameLocation));
        }

        if (candidate.ContractVersion < 1)
        {
            issues.Add(new MessageIssue(
                MessageIssueKind.InvalidContractVersion,
                candidate.MessageDisplayName,
                candidate.ContractVersionLocation));
        }

        return issues.ToImmutable();
    }

    private static MessageHandlerDefinition CreateDefinition(MessageHandlerAnalysis candidate)
    {
        return new MessageHandlerDefinition(
            candidate.ContractName!,
            candidate.ContractVersion,
            candidate.MessageTypeName,
            candidate.HandlerTypeName,
            candidate.Kind,
            candidate.ResponseTypeName);
    }
}
