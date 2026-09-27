using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Diagnostics;

internal static class MiddlewareDiagnosticReporter
{
    public static Diagnostic Create(
        Compilation compilation,
        MiddlewareIssue issue)
    {
        var descriptor = ReadDescriptor(issue.Kind);
        var tree = compilation.SyntaxTrees.ElementAt(issue.Location.TreeIndex);
        var span = new TextSpan(issue.Location.Start, issue.Location.Length);
        var location = Location.Create(tree, span);

        var diagnostic = Diagnostic.Create(
            descriptor,
            location,
            issue.Subject,
            issue.Details);

        return diagnostic;
    }

    private static DiagnosticDescriptor ReadDescriptor(MiddlewareIssueKind kind)
    {
        return kind switch
        {
            MiddlewareIssueKind.InvalidMiddleware => MiddlewareDiagnostics.InvalidMiddleware,
            MiddlewareIssueKind.DuplicateOrder => MiddlewareDiagnostics.DuplicateOrder,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown TinyBus middleware diagnostic.")
        };
    }
}
