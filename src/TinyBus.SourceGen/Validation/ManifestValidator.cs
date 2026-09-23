using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Validation;

internal sealed class ManifestValidator
{
    public ImmutableArray<MessageIssue> Validate(
        ImmutableArray<MessageValidationResult> validation,
        CancellationToken cancellationToken)
    {
        var handlers = validation
            .Where(result => result.Definition is not null)
            .Select(result => result.Candidate)
            .ToImmutableArray();
        var issues = ImmutableArray.CreateBuilder<MessageIssue>();

        AddDuplicateHandlerIssues(
            issues,
            handlers,
            MessageHandlerKind.Command,
            MessageIssueKind.DuplicateCommandHandler,
            cancellationToken);
        AddDuplicateHandlerIssues(
            issues,
            handlers,
            MessageHandlerKind.Request,
            MessageIssueKind.DuplicateRequestHandler,
            cancellationToken);
        AddConflictingSemanticsIssues(issues, handlers, cancellationToken);

        return issues.ToImmutable();
    }

    private static void AddDuplicateHandlerIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        MessageHandlerKind kind,
        MessageIssueKind issueKind,
        CancellationToken cancellationToken)
    {
        var duplicateMessages = handlers
            .Where(handler => handler.Kind == kind)
            .GroupBy(handler => handler.MessageTypeName, StringComparer.Ordinal)
            .Where(HasMultipleHandlers)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var message in duplicateMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in DistinctHandlers(message))
            {
                issues.Add(CreateIssue(issueKind, handler));
            }
        }
    }

    private static void AddConflictingSemanticsIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        CancellationToken cancellationToken)
    {
        var conflictingMessages = handlers
            .GroupBy(handler => handler.MessageTypeName, StringComparer.Ordinal)
            .Where(HasConflictingSemantics)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var message in conflictingMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in DistinctHandlers(message))
            {
                issues.Add(CreateIssue(MessageIssueKind.ConflictingMessageSemantics, handler));
            }
        }
    }

    private static bool HasMultipleHandlers(IGrouping<string, MessageHandlerAnalysis> message)
    {
        return message.Select(handler => handler.HandlerTypeName).Distinct().Skip(1).Any();
    }

    private static bool HasConflictingSemantics(IGrouping<string, MessageHandlerAnalysis> message)
    {
        return message.Select(handler => handler.Kind).Distinct().Skip(1).Any();
    }

    private static IEnumerable<MessageHandlerAnalysis> DistinctHandlers(
        IEnumerable<MessageHandlerAnalysis> handlers)
    {
        return handlers
            .GroupBy(handler => handler.HandlerTypeName, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(handler => handler.HandlerTypeName, StringComparer.Ordinal);
    }

    private static MessageIssue CreateIssue(
        MessageIssueKind kind,
        MessageHandlerAnalysis handler)
    {
        return new MessageIssue(kind, handler.MessageDisplayName, handler.HandlerLocation);
    }
}
