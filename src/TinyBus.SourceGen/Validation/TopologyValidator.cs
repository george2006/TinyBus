using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Validation;

internal sealed class TopologyValidator
{
    public ImmutableArray<MessageIssue> Validate(
        string assemblyName,
        ImmutableArray<MessageValidationResult> validation,
        ImmutableArray<ReferencedMessageContribution> contributions,
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
        AddReferencedIssues(issues, assemblyName, handlers, contributions, cancellationToken);

        return issues.Distinct().ToImmutableArray();
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
            .GroupBy(handler => handler.MessageTypeIdentity, StringComparer.Ordinal)
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
            .GroupBy(handler => handler.MessageTypeIdentity, StringComparer.Ordinal)
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

    private static void AddReferencedIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var referencedMessages = contributions
            .GroupBy(contribution => contribution.MessageTypeIdentity, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var message in referencedMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localHandlers = handlers
                .Where(handler => handler.MessageTypeIdentity == message.Key)
                .ToImmutableArray();
            var referencedHandlers = message.ToImmutableArray();

            AddReferencedDuplicateIssues(
                issues, assemblyName, localHandlers, referencedHandlers,
                MessageHandlerKind.Command, MessageIssueKind.DuplicateCommandHandler);
            AddReferencedDuplicateIssues(
                issues, assemblyName, localHandlers, referencedHandlers,
                MessageHandlerKind.Request, MessageIssueKind.DuplicateRequestHandler);
            AddReferencedSemanticsIssues(issues, assemblyName, localHandlers, referencedHandlers);
        }
    }

    private static void AddReferencedDuplicateIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> localHandlers,
        ImmutableArray<ReferencedMessageContribution> referencedHandlers,
        MessageHandlerKind kind,
        MessageIssueKind issueKind)
    {
        var local = localHandlers.Where(handler => handler.Kind == kind).ToImmutableArray();
        var referenced = referencedHandlers.Where(handler => handler.Kind == kind).ToImmutableArray();
        if (referenced.IsEmpty)
        {
            return;
        }

        var handlerNames = ReadHandlerNames(assemblyName, local, referenced);
        if (handlerNames.Length < 2)
        {
            return;
        }

        AddReferencedConflict(issues, issueKind, local, referenced, handlerNames);
    }

    private static void AddReferencedSemanticsIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> localHandlers,
        ImmutableArray<ReferencedMessageContribution> referencedHandlers)
    {
        var kinds = localHandlers.Select(handler => handler.Kind)
            .Concat(referencedHandlers.Select(handler => handler.Kind));
        var hasConflictingSemantics = kinds.Distinct().Skip(1).Any();
        if (!hasConflictingSemantics)
        {
            return;
        }

        var handlerNames = ReadHandlerNames(assemblyName, localHandlers, referencedHandlers);
        AddReferencedConflict(
            issues, MessageIssueKind.ConflictingMessageSemantics,
            localHandlers, referencedHandlers, handlerNames);
    }

    private static ImmutableArray<string> ReadHandlerNames(
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> localHandlers,
        ImmutableArray<ReferencedMessageContribution> referencedHandlers)
    {
        return localHandlers.Select(handler => $"{assemblyName}::{handler.HandlerTypeName}")
            .Concat(referencedHandlers.Select(handler => $"{handler.AssemblyName}::{handler.HandlerTypeName}"))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void AddReferencedConflict(
        ImmutableArray<MessageIssue>.Builder issues,
        MessageIssueKind kind,
        ImmutableArray<MessageHandlerAnalysis> localHandlers,
        ImmutableArray<ReferencedMessageContribution> referencedHandlers,
        ImmutableArray<string> handlerNames)
    {
        foreach (var handler in DistinctHandlers(localHandlers))
        {
            issues.Add(CreateIssue(kind, handler));
        }

        // Metadata has no source span in the root; report the participating assemblies explicitly.
        issues.Add(new MessageIssue(
            kind,
            referencedHandlers[0].MessageTypeName,
            location: null,
            handlerDetails: $". Handlers: {string.Join(", ", handlerNames)}"));
    }
}
