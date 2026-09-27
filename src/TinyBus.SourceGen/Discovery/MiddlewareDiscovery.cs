using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyBus.SourceGen.Discovery;

internal static class MiddlewareDiscovery
{
    public static bool IsCandidateDeclaration(
        SyntaxNode node,
        CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { AttributeLists.Count: > 0 };
    }
}
