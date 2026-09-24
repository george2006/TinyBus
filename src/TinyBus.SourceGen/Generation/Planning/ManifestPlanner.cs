using System;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Generation.Planning;

internal sealed class ManifestPlanner
{
    public ManifestPlan Create(
        string assemblyName,
        ImmutableArray<MessageHandlerDefinition> definitions,
        ImmutableArray<ReferencedMessageContribution> contributions,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messages = OrderMessages(definitions);
        var referencedManifests = SelectReferencedManifests(contributions);
        var manifestTypeName = CreateManifestTypeName(assemblyName);

        return new ManifestPlan(manifestTypeName, messages, referencedManifests);
    }

    private static ImmutableArray<MessageHandlerDefinition> OrderMessages(
        ImmutableArray<MessageHandlerDefinition> definitions)
    {
        return definitions
            .OrderBy(definition => definition.ContractName, StringComparer.Ordinal)
            .ThenBy(definition => definition.ContractVersion)
            .ThenBy(definition => definition.Kind)
            .ThenBy(definition => definition.HandlerTypeName, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static ImmutableArray<string> SelectReferencedManifests(
        ImmutableArray<ReferencedMessageContribution> contributions)
    {
        return contributions
            .OrderBy(contribution => contribution.AssemblyName, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ManifestTypeName, StringComparer.Ordinal)
            .Select(contribution => contribution.ManifestTypeName)
            .Distinct(StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static string CreateManifestTypeName(string assemblyName)
    {
        var nameCharacters = assemblyName
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray();
        var readableName = new string(nameCharacters);
        var suffix = CreateStableSuffix(assemblyName);

        return $"TinyBusManifest_{readableName}_{suffix}";
    }

    private static string CreateStableSuffix(string assemblyName)
    {
        using var algorithm = SHA256.Create();
        var assemblyNameBytes = Encoding.UTF8.GetBytes(assemblyName);
        var hash = algorithm.ComputeHash(assemblyNameBytes);
        var suffixBytes = hash.Take(4).Select(value => value.ToString("x2"));

        return string.Concat(suffixBytes);
    }
}
