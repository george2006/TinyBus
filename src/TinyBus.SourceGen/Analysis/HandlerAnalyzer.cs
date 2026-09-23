using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Analysis;

internal sealed class HandlerAnalyzer
{
    private const string CommandHandlerMetadataName = "TinyBus.ICommandHandler`1";
    private const string EventHandlerMetadataName = "TinyBus.IEventHandler`1";
    private const string RequestHandlerMetadataName = "TinyBus.IRequestHandler`2";

    public ImmutableArray<MessageHandlerDefinition> Analyze(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken);

        if (declaredSymbol is not INamedTypeSymbol handler)
        {
            return ImmutableArray<MessageHandlerDefinition>.Empty;
        }

        if (!IsConcreteHandler(handler))
        {
            return ImmutableArray<MessageHandlerDefinition>.Empty;
        }

        if (!IsPrimaryDeclaration(handler, declaration, cancellationToken))
        {
            return ImmutableArray<MessageHandlerDefinition>.Empty;
        }

        return AnalyzeContracts(context.SemanticModel.Compilation, handler, cancellationToken);
    }

    private static ImmutableArray<MessageHandlerDefinition> AnalyzeContracts(
        Compilation compilation,
        INamedTypeSymbol handler,
        CancellationToken cancellationToken)
    {
        var commandHandler = compilation.GetTypeByMetadataName(CommandHandlerMetadataName);
        var eventHandler = compilation.GetTypeByMetadataName(EventHandlerMetadataName);
        var requestHandler = compilation.GetTypeByMetadataName(RequestHandlerMetadataName);
        var definitions = ImmutableArray.CreateBuilder<MessageHandlerDefinition>();

        foreach (var implementedInterface in handler.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsHandlerContract(implementedInterface, commandHandler))
            {
                definitions.Add(CreateDefinition(handler, implementedInterface, MessageHandlerKind.Command));
                continue;
            }

            if (IsHandlerContract(implementedInterface, eventHandler))
            {
                definitions.Add(CreateDefinition(handler, implementedInterface, MessageHandlerKind.Event));
                continue;
            }

            if (IsHandlerContract(implementedInterface, requestHandler))
            {
                definitions.Add(CreateDefinition(handler, implementedInterface, MessageHandlerKind.Request));
            }
        }

        return definitions.ToImmutable();
    }

    private static bool IsHandlerContract(
        INamedTypeSymbol implementedInterface,
        INamedTypeSymbol? handlerContract)
    {
        if (handlerContract is null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(
            implementedInterface.OriginalDefinition,
            handlerContract);
    }

    private static MessageHandlerDefinition CreateDefinition(
        INamedTypeSymbol handler,
        INamedTypeSymbol implementedInterface,
        MessageHandlerKind kind)
    {
        var message = implementedInterface.TypeArguments[0];
        var response = kind == MessageHandlerKind.Request
            ? implementedInterface.TypeArguments[1]
            : null;

        return new MessageHandlerDefinition(
            message.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            message.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            kind,
            response?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
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
