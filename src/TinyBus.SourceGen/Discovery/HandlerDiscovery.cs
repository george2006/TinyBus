using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TinyBus.SourceGen.Discovery;

internal static class HandlerDiscovery
{
    public static bool IsCandidateDeclaration(SyntaxNode node, CancellationToken cancellationToken)
    {
        return node is ClassDeclarationSyntax { BaseList: not null };
    }
}
