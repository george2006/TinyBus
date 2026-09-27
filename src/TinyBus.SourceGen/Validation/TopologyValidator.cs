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

        AddDuplicateCommandHandlerIssues(issues, handlers, cancellationToken);
        AddDuplicateRequestHandlerIssues(issues, handlers, cancellationToken);
        AddConflictingSemanticsIssues(issues, handlers, cancellationToken);
        AddAmbiguousContractIdentityIssues(
            issues, assemblyName, handlers, contributions, cancellationToken);
        AddReferencedIssues(issues, assemblyName, handlers, contributions, cancellationToken);

        return issues.Distinct().ToImmutableArray();
    }

    private static void AddAmbiguousContractIdentityIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        var localContracts = handlers.Select(ReadContractKey);
        var referencedContracts = contributions.Select(ReadContractKey);
        var contracts = localContracts
            .Concat(referencedContracts)
            .Distinct()
            .OrderBy(contract => contract.Name, StringComparer.Ordinal)
            .ThenBy(contract => contract.Version);

        foreach (var contract in contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localMessages = handlers
                .Where(handler => ReadContractKey(handler) == contract)
                .ToImmutableArray();
            var referencedMessages = contributions
                .Where(contribution => ReadContractKey(contribution) == contract)
                .ToImmutableArray();
            var messageIdentities = localMessages.Select(message => message.MessageType.Identity)
                .Concat(referencedMessages.Select(message => message.MessageTypeIdentity))
                .Distinct(StringComparer.Ordinal)
                .ToImmutableArray();

            if (messageIdentities.Length < 2)
            {
                continue;
            }

            AddLocalContractIdentityIssues(
                issues, assemblyName, contract, localMessages, referencedMessages);
            AddReferencedContractIdentityIssue(
                issues, assemblyName, contract, localMessages, referencedMessages);
        }
    }

    private static void AddLocalContractIdentityIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ContractKey contract,
        ImmutableArray<MessageHandlerAnalysis> localMessages,
        ImmutableArray<ReferencedMessageContribution> referencedMessages)
    {
        var details = ReadContractMessageDetails(
            assemblyName, localMessages, referencedMessages);
        var contractDisplayName = contract.DisplayName;
        var distinctMessages = localMessages
            .GroupBy(message => message.MessageType.Identity, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(message => message.MessageType.DisplayName, StringComparer.Ordinal);

        foreach (var message in distinctMessages)
        {
            var issue = new MessageIssue(
                MessageIssueKind.AmbiguousContractIdentity,
                contractDisplayName,
                message.Contract.NameLocation,
                details);
            issues.Add(issue);
        }
    }

    private static void AddReferencedContractIdentityIssue(
        ImmutableArray<MessageIssue>.Builder issues,
        string assemblyName,
        ContractKey contract,
        ImmutableArray<MessageHandlerAnalysis> localMessages,
        ImmutableArray<ReferencedMessageContribution> referencedMessages)
    {
        if (referencedMessages.IsEmpty)
        {
            return;
        }

        var details = ReadContractMessageDetails(
            assemblyName, localMessages, referencedMessages);
        var issue = new MessageIssue(
            MessageIssueKind.AmbiguousContractIdentity,
            contract.DisplayName,
            location: null,
            handlerDetails: details);
        issues.Add(issue);
    }

    private static string ReadContractMessageDetails(
        string assemblyName,
        ImmutableArray<MessageHandlerAnalysis> localMessages,
        ImmutableArray<ReferencedMessageContribution> referencedMessages)
    {
        var localTypes = localMessages.Select(message =>
            $"{assemblyName}::{message.MessageType.TypeName}");
        var referencedTypes = referencedMessages.Select(message =>
            $"{message.AssemblyName}::{message.MessageTypeName}");
        var messageTypes = localTypes
            .Concat(referencedTypes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(messageType => messageType, StringComparer.Ordinal);
        var participants = string.Join(", ", messageTypes);

        return $". Message types: {participants}";
    }

    private static ContractKey ReadContractKey(MessageHandlerAnalysis handler)
    {
        return new ContractKey(handler.Contract.Name!, handler.Contract.Version);
    }

    private static ContractKey ReadContractKey(ReferencedMessageContribution contribution)
    {
        return new ContractKey(contribution.ContractName, contribution.ContractVersion);
    }

    private static void AddDuplicateCommandHandlerIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        CancellationToken cancellationToken)
    {
        AddDuplicateHandlerIssues(
            issues,
            handlers,
            MessageHandlerKind.Command,
            MessageIssueKind.DuplicateCommandHandler,
            cancellationToken);
    }

    private static void AddDuplicateRequestHandlerIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        CancellationToken cancellationToken)
    {
        AddDuplicateHandlerIssues(
            issues,
            handlers,
            MessageHandlerKind.Request,
            MessageIssueKind.DuplicateRequestHandler,
            cancellationToken);
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
            .GroupBy(handler => handler.MessageType.Identity, StringComparer.Ordinal)
            .Where(HasMultipleHandlers)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var message in duplicateMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in DistinctHandlers(message))
            {
                var issue = CreateIssue(issueKind, handler);
                issues.Add(issue);
            }
        }
    }

    private static void AddConflictingSemanticsIssues(
        ImmutableArray<MessageIssue>.Builder issues,
        ImmutableArray<MessageHandlerAnalysis> handlers,
        CancellationToken cancellationToken)
    {
        var conflictingMessages = handlers
            .GroupBy(handler => handler.MessageType.Identity, StringComparer.Ordinal)
            .Where(HasConflictingSemantics)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (var message in conflictingMessages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var handler in DistinctHandlers(message))
            {
                var issue = CreateIssue(MessageIssueKind.ConflictingMessageSemantics, handler);
                issues.Add(issue);
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
        return new MessageIssue(kind, handler.MessageType.DisplayName, handler.HandlerLocation);
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
                .Where(handler => handler.MessageType.Identity == message.Key)
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
        var localKinds = localHandlers.Select(handler => handler.Kind);
        var referencedKinds = referencedHandlers.Select(handler => handler.Kind);
        var kinds = localKinds.Concat(referencedKinds);
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
        var localNames = localHandlers.Select(handler => $"{assemblyName}::{handler.HandlerTypeName}");
        var referencedNames = referencedHandlers.Select(handler => $"{handler.AssemblyName}::{handler.HandlerTypeName}");

        return localNames.Concat(referencedNames)
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
            var issue = CreateIssue(kind, handler);
            issues.Add(issue);
        }

        // Metadata has no source span in the root; report the participating assemblies explicitly.
        var participants = string.Join(", ", handlerNames);
        var handlerDetails = $". Handlers: {participants}";
        var referencedIssue = new MessageIssue(
            kind,
            referencedHandlers[0].MessageTypeName,
            location: null,
            handlerDetails: handlerDetails);
        issues.Add(referencedIssue);
    }

    private readonly struct ContractKey : IEquatable<ContractKey>
    {
        internal ContractKey(string name, int version)
        {
            Name = name;
            Version = version;
        }

        internal string Name { get; }

        internal int Version { get; }

        internal string DisplayName => $"'{Name}' version {Version}";

        public bool Equals(ContractKey other)
        {
            return string.Equals(Name, other.Name, StringComparison.Ordinal)
                && Version == other.Version;
        }

        public override bool Equals(object? obj)
        {
            return obj is ContractKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Name, Version).GetHashCode();
        }

        public static bool operator ==(ContractKey left, ContractKey right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(ContractKey left, ContractKey right)
        {
            return !left.Equals(right);
        }
    }
}
