using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Analysis;

internal sealed class HandlerAnalyzer
{
    private const string CommandHandlerMetadataName = "TinyBus.ICommandHandler`1";

    public MessageHandlerDefinition? Analyze(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        var commandHandler = context.SemanticModel.Compilation.GetTypeByMetadataName(
            CommandHandlerMetadataName);

        if (declaredSymbol is not INamedTypeSymbol handler)
        {
            return null;
        }

        if (!IsConcreteHandler(handler))
        {
            return null;
        }

        if (commandHandler is null)
        {
            return null;
        }

        var implementedHandler = handler.AllInterfaces.FirstOrDefault(candidate =>
            SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, commandHandler));

        if (implementedHandler is null)
        {
            return null;
        }

        if (!IsPrimaryDeclaration(handler, declaration, cancellationToken))
        {
            return null;
        }

        var message = implementedHandler.TypeArguments[0];
        return new MessageHandlerDefinition(
            message.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            message.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static bool IsConcreteHandler(INamedTypeSymbol handler)
    {
        return !handler.IsAbstract;
    }

    private static bool IsPrimaryDeclaration(
        INamedTypeSymbol handler,
        ClassDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        foreach (var reference in handler.DeclaringSyntaxReferences)
        {
            var candidate = reference.GetSyntax(cancellationToken) as ClassDeclarationSyntax;
            if (candidate?.BaseList is null)
            {
                continue;
            }

            return IsSameDeclaration(candidate, declaration);
        }

        return false;
    }

    private static bool IsSameDeclaration(
        ClassDeclarationSyntax candidate,
        ClassDeclarationSyntax declaration)
    {
        return candidate.SyntaxTree == declaration.SyntaxTree
            && candidate.Span == declaration.Span;
    }
}
