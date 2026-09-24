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
    private const string BusContractAttributeMetadataName = "TinyBus.BusContractAttribute";
    private const string ContractVersionPropertyName = "Version";

    public ImmutableArray<MessageHandlerAnalysis> Analyze(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(declaration, cancellationToken);

        if (declaredSymbol is not INamedTypeSymbol handler)
        {
            return ImmutableArray<MessageHandlerAnalysis>.Empty;
        }

        if (!IsConcreteHandler(handler))
        {
            return ImmutableArray<MessageHandlerAnalysis>.Empty;
        }

        if (!IsPrimaryDeclaration(handler, declaration, cancellationToken))
        {
            return ImmutableArray<MessageHandlerAnalysis>.Empty;
        }

        return AnalyzeContracts(context.SemanticModel.Compilation, handler, cancellationToken);
    }

    private static ImmutableArray<MessageHandlerAnalysis> AnalyzeContracts(
        Compilation compilation,
        INamedTypeSymbol handler,
        CancellationToken cancellationToken)
    {
        var commandHandler = compilation.GetTypeByMetadataName(CommandHandlerMetadataName);
        var eventHandler = compilation.GetTypeByMetadataName(EventHandlerMetadataName);
        var requestHandler = compilation.GetTypeByMetadataName(RequestHandlerMetadataName);
        var busContractAttribute = compilation.GetTypeByMetadataName(BusContractAttributeMetadataName);
        var candidates = ImmutableArray.CreateBuilder<MessageHandlerAnalysis>();

        foreach (var implementedInterface in handler.AllInterfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsHandlerContract(implementedInterface, commandHandler))
            {
                candidates.Add(CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Command,
                    busContractAttribute));
                continue;
            }

            if (IsHandlerContract(implementedInterface, eventHandler))
            {
                candidates.Add(CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Event,
                    busContractAttribute));
                continue;
            }

            if (IsHandlerContract(implementedInterface, requestHandler))
            {
                candidates.Add(CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Request,
                    busContractAttribute));
            }
        }

        return candidates.ToImmutable();
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

    private static MessageHandlerAnalysis CreateAnalysis(
        Compilation compilation,
        INamedTypeSymbol handler,
        INamedTypeSymbol implementedInterface,
        MessageHandlerKind kind,
        INamedTypeSymbol? busContractAttribute)
    {
        var message = implementedInterface.TypeArguments[0];
        var response = kind == MessageHandlerKind.Request
            ? implementedInterface.TypeArguments[1]
            : null;
        var contractAttribute = FindContractAttribute(message, busContractAttribute);
        var contractName = ReadContractName(message, contractAttribute);
        var contractVersion = ReadContractVersion(contractAttribute);

        return new MessageHandlerAnalysis(
            message.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            contractName,
            contractVersion,
            message.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ReadMessageTypeIdentity(message),
            handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            kind,
            response?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SourceLocationReader.Read(compilation, handler.Locations[0]),
            ReadContractNameLocation(compilation, message, contractAttribute, handler.Locations[0]),
            ReadContractVersionLocation(compilation, message, contractAttribute, handler.Locations[0]));
    }

    internal static string ReadMessageTypeIdentity(ITypeSymbol message)
    {
        if (message is IArrayTypeSymbol array)
        {
            return $"array:{array.Rank}:{ReadMessageTypeIdentity(array.ElementType)}";
        }

        var definition = message.OriginalDefinition;
        var identity = $"{definition.ContainingAssembly?.Identity}:{definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
        if (message is not INamedTypeSymbol named)
        {
            return identity;
        }

        // Closed generic arguments and enclosing types can come from different assemblies.
        var arguments = string.Join(";", named.TypeArguments.Select(ReadMessageTypeIdentity));
        var containingType = named.ContainingType is null
            ? string.Empty
            : ReadMessageTypeIdentity(named.ContainingType);

        return $"{identity}[{containingType}][{arguments}]";
    }

    private static AttributeData? FindContractAttribute(
        ITypeSymbol message,
        INamedTypeSymbol? busContractAttribute)
    {
        if (busContractAttribute is null)
        {
            return null;
        }

        return message.GetAttributes().FirstOrDefault(attribute =>
            SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, busContractAttribute));
    }

    private static string? ReadContractName(ITypeSymbol message, AttributeData? contractAttribute)
    {
        if (contractAttribute is not null)
        {
            return ReadExplicitContractName(contractAttribute);
        }

        // Convention-based identities follow CLR names. Use BusContractAttribute when an identity
        // must survive namespace or type-name refactoring.
        return message.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
    }

    private static string? ReadExplicitContractName(AttributeData? contractAttribute)
    {
        if (contractAttribute is null)
        {
            return null;
        }

        if (contractAttribute.ConstructorArguments.IsEmpty)
        {
            return null;
        }

        return contractAttribute.ConstructorArguments[0].Value as string;
    }

    private static SourceLocation ReadContractNameLocation(
        Compilation compilation,
        ITypeSymbol message,
        AttributeData? contractAttribute,
        Location handlerLocation)
    {
        var syntax = ReadAttributeSyntax(contractAttribute);
        var argument = syntax?.ArgumentList?.Arguments.FirstOrDefault(candidate =>
            candidate.NameEquals is null);
        var location = argument?.Expression.GetLocation()
            ?? message.Locations.FirstOrDefault(candidate => candidate.IsInSource)
            ?? handlerLocation;

        return SourceLocationReader.Read(compilation, location);
    }

    private static SourceLocation ReadContractVersionLocation(
        Compilation compilation,
        ITypeSymbol message,
        AttributeData? contractAttribute,
        Location handlerLocation)
    {
        var syntax = ReadAttributeSyntax(contractAttribute);
        var argument = syntax?.ArgumentList?.Arguments.FirstOrDefault(candidate =>
            candidate.NameEquals?.Name.Identifier.ValueText == ContractVersionPropertyName);
        var location = argument?.Expression.GetLocation()
            ?? message.Locations.FirstOrDefault(candidate => candidate.IsInSource)
            ?? handlerLocation;

        return SourceLocationReader.Read(compilation, location);
    }

    private static AttributeSyntax? ReadAttributeSyntax(AttributeData? contractAttribute)
    {
        return contractAttribute?.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
    }

    private static int ReadContractVersion(AttributeData? contractAttribute)
    {
        if (contractAttribute is null)
        {
            return 1;
        }

        foreach (var argument in contractAttribute.NamedArguments)
        {
            if (argument.Key == ContractVersionPropertyName
                && argument.Value.Value is int version)
            {
                return version;
            }
        }

        return 1;
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
