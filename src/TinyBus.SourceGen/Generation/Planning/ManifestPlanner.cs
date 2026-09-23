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
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var messages = definitions
            .OrderBy(definition => definition.ContractName, StringComparer.Ordinal)
            .ThenBy(definition => definition.ContractVersion)
            .ThenBy(definition => definition.Kind)
            .ThenBy(definition => definition.HandlerTypeName, StringComparer.Ordinal)
            .ToImmutableArray();

        return new ManifestPlan(CreateManifestTypeName(assemblyName), messages);
    }

    private static string CreateManifestTypeName(string assemblyName)
    {
        var readableName = new string(assemblyName
            .Select(character => char.IsLetterOrDigit(character) ? character : '_')
            .ToArray());

        return $"TinyBusManifest_{readableName}_{CreateStableSuffix(assemblyName)}";
    }

    private static string CreateStableSuffix(string assemblyName)
    {
        using var algorithm = SHA256.Create();
        var hash = algorithm.ComputeHash(Encoding.UTF8.GetBytes(assemblyName));

        return string.Concat(hash.Take(4).Select(value => value.ToString("x2")));
    }
}
