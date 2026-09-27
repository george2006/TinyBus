using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class IncomingMessagePipelineTests
{
    [Fact]
    public void Emits_the_incoming_pipeline_separately_from_the_manifest()
    {
        var run = SourceGeneratorTestHost.Run("public sealed class EmptyConsumer;");
        var result = Assert.Single(run.Results);
        var hintNames = result.GeneratedSources.Select(source => source.HintName);

        var expected = new[]
        {
            "TinyBus.Generated.Manifest.g.cs",
            "TinyBus.Generated.IncomingPipeline.g.cs"
        };
        Assert.Equal(expected, hintNames);
    }

    [Fact]
    public void Executes_a_local_command_from_its_envelope()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            internal sealed record Capture(int Amount);

            internal sealed class CapturedPayments
            {
                public int Total { get; set; }
            }

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                private readonly CapturedPayments payments;

                public CaptureHandler(CapturedPayments payments)
                {
                    this.payments = payments;
                }

                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    payments.Total += command.Amount;
                    return ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static int Run()
                {
                    var services = new ServiceCollection();
                    services.AddSingleton<CapturedPayments>();
                    services.AddTinyBus(bus => bus.Service("payments"));

                    using var provider = services.BuildServiceProvider();
                    var pipeline = provider.GetRequiredService<IIncomingMessagePipeline>();
                    var messageId = Guid.NewGuid();
                    var contract = new ContractIdentity("Capture", 1);
                    var envelope = new MessageEnvelope(messageId, contract, "{\"Amount\":25}");

                    var execution = pipeline.ExecuteAsync(envelope);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    var payments = provider.GetRequiredService<CapturedPayments>();
                    return payments.Total;
                }
            }
            """;

        var total = SourceGeneratorTestHost.Execute<int>(source);

        Assert.Equal(25, total);
    }

    [Fact]
    public void Executes_an_internal_command_from_a_referenced_assembly()
    {
        const string librarySource = """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments;

            internal sealed record Capture(int Amount);

            public static class CapturedPayments
            {
                public static int Total { get; set; }
            }

            internal sealed class CaptureHandler : ICommandHandler<Capture>
            {
                public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                {
                    CapturedPayments.Total += command.Amount;
                    return ValueTask.CompletedTask;
                }
            }
            """;
        var noReferences = Array.Empty<MetadataReference>();
        var library = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Payments", noReferences, librarySource);
        var libraryImage = SourceGeneratorTestHost.CompileImage(library);
        var libraryReference = MetadataReference.CreateFromImage(libraryImage);
        var references = new[] { libraryReference };

        const string hostSource = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public static class Scenario
            {
                public static int Run()
                {
                    var services = new ServiceCollection();
                    services.AddTinyBus(bus => bus.Service("payments"));

                    using var provider = services.BuildServiceProvider();
                    var pipeline = provider.GetRequiredService<IIncomingMessagePipeline>();
                    var messageId = Guid.NewGuid();
                    var contract = new ContractIdentity("Payments.Capture", 1);
                    var envelope = new MessageEnvelope(messageId, contract, "{\"Amount\":40}");

                    var execution = pipeline.ExecuteAsync(envelope);
                    var completion = execution.GetAwaiter();
                    completion.GetResult();

                    return Payments.CapturedPayments.Total;
                }
            }
            """;
        var host = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Host", references, hostSource);

        var total = SourceGeneratorTestHost.Execute<int>(host, libraryImage);

        Assert.Equal(40, total);
    }

    [Fact]
    public void Rejects_an_envelope_without_a_command_handler()
    {
        const string source = """
            using System;
            using Microsoft.Extensions.DependencyInjection;
            using TinyBus;

            public static class Scenario
            {
                public static string Run()
                {
                    var services = new ServiceCollection();
                    services.AddTinyBus(bus => bus.Service("payments"));

                    using var provider = services.BuildServiceProvider();
                    var pipeline = provider.GetRequiredService<IIncomingMessagePipeline>();
                    var messageId = Guid.NewGuid();
                    var contract = new ContractIdentity("Unknown.Command", 3);
                    var envelope = new MessageEnvelope(messageId, contract, "{}");

                    try
                    {
                        var execution = pipeline.ExecuteAsync(envelope);
                        var completion = execution.GetAwaiter();
                        completion.GetResult();
                    }
                    catch (InvalidOperationException error)
                    {
                        return error.Message;
                    }

                    return "No exception";
                }
            }
            """;

        var error = SourceGeneratorTestHost.Execute<string>(source);

        Assert.Contains("Unknown.Command", error);
        Assert.Contains("version 3", error);
    }
}
