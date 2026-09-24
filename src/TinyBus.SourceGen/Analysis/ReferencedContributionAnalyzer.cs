using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Analysis;

internal sealed class ReferencedContributionAnalyzer
{
    private const string ContributionAttributeMetadataName =
        "TinyBus.BusMessageContributionAttribute";

    // Constructor positions in BusMessageContributionAttribute.
    private const int ManifestTypeArgument = 0;
    private const int ContractNameArgument = 1;
    private const int ContractVersionArgument = 2;
    private const int MessageTypeArgument = 3;
    private const int HandlerTypeArgument = 4;
    private const int KindArgument = 5;
    private const int ResponseTypeArgument = 6;

    public ImmutableArray<ReferencedMessageContribution> Analyze(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        var contributionAttribute = compilation.GetTypeByMetadataName(
            ContributionAttributeMetadataName);

        if (contributionAttribute is null)
        {
            return ImmutableArray<ReferencedMessageContribution>.Empty;
        }

        var contributions = ReadContributions(
            compilation,
            contributionAttribute,
            cancellationToken);

        return Order(contributions);
    }

    private static ImmutableArray<ReferencedMessageContribution> ReadContributions(
        Compilation compilation,
        INamedTypeSymbol contributionAttribute,
        CancellationToken cancellationToken)
    {
        var contributions = ImmutableArray.CreateBuilder<ReferencedMessageContribution>();

        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadAssemblyContributions(assembly, contributionAttribute, contributions);
        }

        return contributions.ToImmutable();
    }

    private static void ReadAssemblyContributions(
        IAssemblySymbol assembly,
        INamedTypeSymbol contributionAttribute,
        ImmutableArray<ReferencedMessageContribution>.Builder contributions)
    {
        foreach (var attribute in assembly.GetAttributes())
        {
            if (!IsContribution(attribute, contributionAttribute))
            {
                continue;
            }

            var contribution = ReadContribution(assembly.Name, attribute);
            if (contribution is not null)
            {
                contributions.Add(contribution);
            }
        }
    }

    private static bool IsContribution(
        AttributeData attribute,
        INamedTypeSymbol contributionAttribute)
    {
        return SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass,
            contributionAttribute);
    }

    private static ReferencedMessageContribution? ReadContribution(
        string assemblyName,
        AttributeData attribute)
    {
        var arguments = attribute.ConstructorArguments;
        if (arguments.Length <= KindArgument)
        {
            return null;
        }

        var manifestType = arguments[ManifestTypeArgument].Value as ITypeSymbol;
        var contractName = arguments[ContractNameArgument].Value as string;
        var contractVersion = arguments[ContractVersionArgument].Value as int?;
        var messageType = arguments[MessageTypeArgument].Value as ITypeSymbol;
        var handlerType = arguments[HandlerTypeArgument].Value as ITypeSymbol;
        var kindArgument = arguments[KindArgument];
        var kind = ReadKind(kindArgument);

        if (!HasRequiredMetadata(
                manifestType,
                contractName,
                contractVersion,
                messageType,
                handlerType,
                kind))
        {
            return null;
        }

        var manifestTypeName = Display(manifestType!);
        var version = contractVersion.GetValueOrDefault();
        var messageTypeName = Display(messageType!);
        var messageTypeIdentity = HandlerAnalyzer.ReadMessageTypeIdentity(messageType!);
        var handlerTypeName = Display(handlerType!);
        var messageKind = kind.GetValueOrDefault();
        var responseTypeName = ReadResponseType(arguments);

        return new ReferencedMessageContribution(
            assemblyName,
            manifestTypeName,
            contractName!,
            version,
            messageTypeName,
            messageTypeIdentity,
            handlerTypeName,
            messageKind,
            responseTypeName);
    }

    private static bool HasRequiredMetadata(
        ITypeSymbol? manifest,
        string? contractName,
        int? contractVersion,
        ITypeSymbol? message,
        ITypeSymbol? handler,
        MessageHandlerKind? kind)
    {
        return manifest is not null
            && contractName is not null
            && contractVersion is not null
            && message is not null
            && handler is not null
            && kind is not null;
    }

    private static MessageHandlerKind? ReadKind(TypedConstant argument)
    {
        return argument.Value switch
        {
            0 => MessageHandlerKind.Command,
            1 => MessageHandlerKind.Event,
            2 => MessageHandlerKind.Request,
            _ => null
        };
    }

    private static string? ReadResponseType(ImmutableArray<TypedConstant> arguments)
    {
        if (arguments.Length <= ResponseTypeArgument)
        {
            return null;
        }

        return arguments[ResponseTypeArgument].Value is ITypeSymbol response
            ? Display(response)
            : null;
    }

    private static string Display(ITypeSymbol type)
    {
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static ImmutableArray<ReferencedMessageContribution> Order(
        ImmutableArray<ReferencedMessageContribution> contributions)
    {
        return contributions
            .OrderBy(value => value.AssemblyName, StringComparer.Ordinal)
            .ThenBy(value => value.ContractName, StringComparer.Ordinal)
            .ThenBy(value => value.ContractVersion)
            .ThenBy(value => value.Kind)
            .ThenBy(value => value.HandlerTypeName, StringComparer.Ordinal)
            .ToImmutableArray();
    }
}
