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
        var tree = compilation.SyntaxTrees.ElementAt(issue.Location.TreeIndex);
        var span = new TextSpan(issue.Location.Start, issue.Location.Length);

        return Diagnostic.Create(
            descriptor,
            Location.Create(tree, span),
            issue.MessageDisplayName);
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
