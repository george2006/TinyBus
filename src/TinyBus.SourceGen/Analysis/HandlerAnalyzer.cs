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
                var analysis = CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Command,
                    busContractAttribute);
                candidates.Add(analysis);
                continue;
            }

            if (IsHandlerContract(implementedInterface, eventHandler))
            {
                var analysis = CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Event,
                    busContractAttribute);
                candidates.Add(analysis);
                continue;
            }

            if (IsHandlerContract(implementedInterface, requestHandler))
            {
                var analysis = CreateAnalysis(
                    compilation,
                    handler,
                    implementedInterface,
                    MessageHandlerKind.Request,
                    busContractAttribute);
                candidates.Add(analysis);
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
        var handlerDeclarationLocation = handler.Locations[0];
        var contract = AnalyzeContract(
            compilation, message, busContractAttribute, handlerDeclarationLocation);

        var messageType = AnalyzeMessageType(message);
        var handlerTypeName = handler.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var responseTypeName = response?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var handlerLocation = SourceLocationReader.Read(compilation, handlerDeclarationLocation);

        return new MessageHandlerAnalysis(
            messageType,
            contract,
            handlerTypeName,
            kind,
            responseTypeName,
            handlerLocation);
    }

    private static MessageTypeAnalysis AnalyzeMessageType(ITypeSymbol message)
    {
        var displayName = message.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var typeName = message.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var identity = ReadMessageTypeIdentity(message);

        return new MessageTypeAnalysis(displayName, typeName, identity);
    }

    private static ContractAnalysis AnalyzeContract(
        Compilation compilation,
        ITypeSymbol message,
        INamedTypeSymbol? busContractAttribute,
        Location handlerLocation)
    {
        var attribute = FindContractAttribute(message, busContractAttribute);
        var name = ReadContractName(message, attribute);
        var version = ReadContractVersion(attribute);
        var nameLocation = ReadContractNameLocation(compilation, message, attribute, handlerLocation);
        var versionLocation = ReadContractVersionLocation(compilation, message, attribute, handlerLocation);

        return new ContractAnalysis(name, version, nameLocation, versionLocation);
    }

    internal static string ReadMessageTypeIdentity(ITypeSymbol message)
    {
        if (message is IArrayTypeSymbol array)
        {
            var elementIdentity = ReadMessageTypeIdentity(array.ElementType);
            return $"array:{array.Rank}:{elementIdentity}";
        }

        var definition = message.OriginalDefinition;
        var definitionTypeName = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var identity = $"{definition.ContainingAssembly?.Identity}:{definitionTypeName}";
        if (message is not INamedTypeSymbol named)
        {
            return identity;
        }

        // Closed generic arguments and enclosing types can come from different assemblies.
        var argumentIdentities = named.TypeArguments.Select(ReadMessageTypeIdentity);
        var arguments = string.Join(";", argumentIdentities);
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
