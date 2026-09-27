using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Analysis;

internal sealed class MiddlewareAnalyzer
{
    private const string MiddlewareAttributeMetadataName =
        "TinyBus.IncomingMiddlewareAttribute";
    private const string MiddlewareContractMetadataName =
        "TinyBus.IIncomingMessageMiddleware";

    public MiddlewareAnalysis? Analyze(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(
            declaration,
            cancellationToken);

        if (declaredSymbol is not INamedTypeSymbol middleware)
        {
            return null;
        }

        var compilation = context.SemanticModel.Compilation;
        var attributeType = compilation.GetTypeByMetadataName(
            MiddlewareAttributeMetadataName);
        var middlewareContract = compilation.GetTypeByMetadataName(
            MiddlewareContractMetadataName);
        var attribute = FindAttribute(middleware, attributeType);

        if (attribute is null)
        {
            return null;
        }

        var typeName = middleware.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var order = ReadOrder(attribute);
        var isConcrete = IsConcrete(middleware);
        var implementsMiddleware = ImplementsMiddleware(middleware, middlewareContract);
        var location = ReadLocation(compilation, attribute, declaration, cancellationToken);

        var analysis = new MiddlewareAnalysis(
            typeName,
            order,
            isConcrete,
            implementsMiddleware,
            location);

        return analysis;
    }

    private static AttributeData? FindAttribute(
        INamedTypeSymbol middleware,
        INamedTypeSymbol? attributeType)
    {
        return middleware.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, attributeType));
    }

    private static int ReadOrder(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return 0;
        }

        return attribute.ConstructorArguments[0].Value is int order ? order : 0;
    }

    private static bool IsConcrete(INamedTypeSymbol middleware)
    {
        var isConcreteClass = middleware.TypeKind == TypeKind.Class
            && !middleware.IsAbstract
            && !middleware.IsStatic;

        return isConcreteClass && IsAccessibleFromGeneratedCode(middleware);
    }

    private static bool IsAccessibleFromGeneratedCode(INamedTypeSymbol middleware)
    {
        for (var type = middleware; type is not null; type = type.ContainingType)
        {
            var hasSupportedAccessibility = type.DeclaredAccessibility is
                Accessibility.Public or Accessibility.Internal;
            var containsTypeParameters = type.Arity > 0;

            if (!hasSupportedAccessibility || containsTypeParameters)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ImplementsMiddleware(
        INamedTypeSymbol middleware,
        INamedTypeSymbol? middlewareContract)
    {
        return middleware.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(implemented, middlewareContract));
    }

    private static SourceLocation ReadLocation(
        Compilation compilation,
        AttributeData attribute,
        ClassDeclarationSyntax declaration,
        CancellationToken cancellationToken)
    {
        var syntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken);
        var location = syntax?.GetLocation() ?? declaration.Identifier.GetLocation();

        return SourceLocationReader.Read(compilation, location);
    }
}
