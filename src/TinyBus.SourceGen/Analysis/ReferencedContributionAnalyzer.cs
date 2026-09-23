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
        if (arguments.Length < 6)
        {
            return null;
        }

        var manifest = arguments[0].Value as ITypeSymbol;
        var contractName = arguments[1].Value as string;
        var contractVersion = arguments[2].Value as int?;
        var message = arguments[3].Value as ITypeSymbol;
        var handler = arguments[4].Value as ITypeSymbol;
        var kind = ReadKind(arguments[5]);

        if (!HasRequiredMetadata(
                manifest,
                contractName,
                contractVersion,
                message,
                handler,
                kind))
        {
            return null;
        }

        return new ReferencedMessageContribution(
            assemblyName,
            Display(manifest!),
            contractName!,
            contractVersion.GetValueOrDefault(),
            Display(message!),
            Display(handler!),
            kind.GetValueOrDefault(),
            ReadResponseType(arguments));
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
        if (arguments.Length < 7)
        {
            return null;
        }

        return arguments[6].Value is ITypeSymbol response
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
