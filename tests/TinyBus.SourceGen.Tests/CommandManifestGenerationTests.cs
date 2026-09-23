using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class CommandManifestGenerationTests
{
    private const string Consumer = """
        using System.Threading;
        using System.Threading.Tasks;
        using TinyBus;

        namespace Payments
        {
            public sealed record CapturePayment(System.Guid PaymentId);

            public sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
            {
                public ValueTask HandleAsync(
                    CapturePayment command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
        }

        public static class Scenario
        {
            public static string Run()
            {
                var descriptor = new TinyBus.Generated.GeneratedTinyBusManifest().Messages[0];
                return $"{descriptor.Contract.Name}|{descriptor.Contract.Version}|{descriptor.MessageType.FullName}|{descriptor.HandlerType.FullName}|{descriptor.Kind}";
            }
        }
        """;

    [Fact]
    public void Generates_a_runtime_manifest_for_a_command_handler()
    {
        var result = SourceGeneratorTestHost.Execute<string>(Consumer);

        Assert.Equal(
            "Payments.CapturePayment|1|Payments.CapturePayment|Payments.CapturePaymentHandler|Command",
            result);
    }

    [Fact]
    public void Emits_one_manifest_that_compiles()
    {
        var run = SourceGeneratorTestHost.Run(Consumer);
        var generated = Assert.Single(Assert.Single(run.Results).GeneratedSources);

        Assert.Equal("TinyBus.Generated.Manifest.g.cs", generated.HintName);
        Assert.Contains(
            "typeof(global::Payments.CapturePayment)",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "typeof(global::Payments.CapturePaymentHandler)",
            generated.SourceText.ToString(),
            StringComparison.Ordinal);
        Assert.Empty(run.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
}
