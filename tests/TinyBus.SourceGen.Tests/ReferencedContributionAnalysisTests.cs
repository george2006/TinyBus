using TinyBus.SourceGen.Analysis;
using TinyBus.SourceGen.Model;

namespace TinyBus.SourceGen.Tests;

public sealed class ReferencedContributionAnalysisTests
{
    [Fact]
    public void Reads_generated_contribution_metadata_without_retaining_roslyn_symbols()
    {
        var payments = SourceGeneratorTestHost.CompileReference(
            "Payments.Handlers",
            """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments;

            [BusContract("payments.status", Version = 4)]
            public sealed record GetPaymentStatus;

            public sealed record PaymentStatus;

            public sealed class GetPaymentStatusHandler
                : IRequestHandler<GetPaymentStatus, PaymentStatus>
            {
                public ValueTask<PaymentStatus> HandleAsync(
                    GetPaymentStatus request,
                    CancellationToken cancellationToken) =>
                    ValueTask.FromResult(new PaymentStatus());
            }
            """);
        var compilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Payments.Api",
            new[] { payments },
            "public sealed class Api;");

        var contribution = Assert.Single(
            new ReferencedContributionAnalyzer().Analyze(compilation, CancellationToken.None));

        Assert.Equal("Payments.Handlers", contribution.AssemblyName);
        Assert.StartsWith(
            "global::TinyBus.Generated.TinyBusManifest_Payments_Handlers_",
            contribution.ManifestTypeName);
        Assert.Equal("payments.status", contribution.ContractName);
        Assert.Equal(4, contribution.ContractVersion);
        Assert.Equal("global::Payments.GetPaymentStatus", contribution.MessageTypeName);
        Assert.Equal("global::Payments.GetPaymentStatusHandler", contribution.HandlerTypeName);
        Assert.Equal(MessageHandlerKind.Request, contribution.Kind);
        Assert.Equal("global::Payments.PaymentStatus", contribution.ResponseTypeName);
        Assert.DoesNotContain(
            typeof(ReferencedMessageContribution).GetProperties(),
            property => property.PropertyType.Namespace == "Microsoft.CodeAnalysis");
    }

    [Fact]
    public void Orders_contributions_from_multiple_referenced_assemblies_deterministically()
    {
        var zebra = CompileCommand("Zebra.Handlers", "zebra.command", "ZebraCommand");
        var alpha = CompileCommand("Alpha.Handlers", "alpha.command", "AlphaCommand");
        var compilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Root.Service",
            new[] { zebra, alpha },
            "public sealed class Root;");

        var contributions = new ReferencedContributionAnalyzer()
            .Analyze(compilation, CancellationToken.None);

        Assert.Equal(
            new[] { "Alpha.Handlers", "Zebra.Handlers" },
            contributions.Select(value => value.AssemblyName));
    }

    private static Microsoft.CodeAnalysis.MetadataReference CompileCommand(
        string assemblyName,
        string contractName,
        string messageName)
    {
        return SourceGeneratorTestHost.CompileReference(
            assemblyName,
            $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            [BusContract("{{contractName}}")] public sealed record {{messageName}};

            public sealed class Handler : ICommandHandler<{{messageName}}>
            {
                public ValueTask HandleAsync(
                    {{messageName}} command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """);
    }
}
