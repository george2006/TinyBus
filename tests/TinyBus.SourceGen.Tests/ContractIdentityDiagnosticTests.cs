using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class ContractIdentityDiagnosticTests
{
    [Theory]
    [InlineData("null", "null")]
    [InlineData("\"\"", "\"\"")]
    [InlineData("\" \"", "\" \"")]
    public void Rejects_invalid_explicit_contract_names(
        string contractName,
        string expectedLocationText)
    {
        var run = SourceGeneratorTestHost.RunWithDiagnostics(CreateConsumer(
            contractName,
            versionAssignment: null));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("TBUS001", diagnostic.Id);
        Assert.Equal(expectedLocationText, ReadLocationText(diagnostic));
        Assert.DoesNotContain(
            "typeof(global::OrderPlaced)",
            ReadGeneratedManifest(run),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_contract_versions_below_one(int version)
    {
        var run = SourceGeneratorTestHost.RunWithDiagnostics(CreateConsumer(
            "\"orders.order-placed\"",
            $", Version = {version}"));

        var diagnostic = Assert.Single(run.Diagnostics);
        Assert.Equal("TBUS002", diagnostic.Id);
        Assert.Equal(version.ToString(), ReadLocationText(diagnostic));
        Assert.DoesNotContain(
            "typeof(global::OrderPlaced)",
            ReadGeneratedManifest(run),
            StringComparison.Ordinal);
    }

    private static string CreateConsumer(string contractName, string? versionAssignment)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            [BusContract({{contractName}}{{versionAssignment}})]
            public sealed record OrderPlaced;

            public sealed class Handler : IEventHandler<OrderPlaced>
            {
                public ValueTask HandleAsync(
                    OrderPlaced @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """;
    }

    private static string ReadLocationText(Diagnostic diagnostic)
    {
        var tree = Assert.IsAssignableFrom<SyntaxTree>(diagnostic.Location.SourceTree);
        return tree.GetText().ToString(diagnostic.Location.SourceSpan);
    }

    private static string ReadGeneratedManifest(GeneratorDriverRunResult run)
    {
        return Assert.Single(Assert.Single(run.Results).GeneratedSources).SourceText.ToString();
    }
}
