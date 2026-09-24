using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Diagnostics;

internal static class MessageDiagnosticReporter
{
    public static Diagnostic Create(Compilation compilation, MessageIssue issue)
    {
        var descriptor = ReadDescriptor(issue.Kind);
        var location = ReadLocation(compilation, issue.Location);

        return Diagnostic.Create(
            descriptor,
            location,
            issue.MessageDisplayName,
            issue.HandlerDetails);
    }

    private static Location ReadLocation(Compilation compilation, SourceLocation? location)
    {
        if (location is null)
        {
            return Location.None;
        }

        var tree = compilation.SyntaxTrees.ElementAt(location.TreeIndex);
        var span = new TextSpan(location.Start, location.Length);
        return Location.Create(tree, span);
    }

    private static DiagnosticDescriptor ReadDescriptor(MessageIssueKind kind)
    {
        return kind switch
        {
            MessageIssueKind.InvalidContractName => MessageDiagnostics.InvalidContractName,
            MessageIssueKind.InvalidContractVersion => MessageDiagnostics.InvalidContractVersion,
            MessageIssueKind.DuplicateCommandHandler => MessageDiagnostics.DuplicateCommandHandler,
            MessageIssueKind.DuplicateRequestHandler => MessageDiagnostics.DuplicateRequestHandler,
            MessageIssueKind.ConflictingMessageSemantics => MessageDiagnostics.ConflictingMessageSemantics,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown TinyBus diagnostic.")
        };
    }
}
