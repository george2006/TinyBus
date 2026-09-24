using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Tests;

public sealed class ServiceTopologyGenerationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Creates_topologies_with_explicit_identities_and_complete_composed_metadata(bool includeLocalHandler)
    {
        var library = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Payments.Handlers", Array.Empty<MetadataReference>(), """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments;

            [BusContract("payments.status", Version = 3)]
            internal sealed record GetStatus;
            internal sealed record Status;

            internal sealed class GetStatusHandler : IRequestHandler<GetStatus, Status>
            {
                public ValueTask<Status> HandleAsync(GetStatus request, CancellationToken cancellationToken)
                    => ValueTask.FromResult(new Status());
            }
            """);
        var libraryImage = SourceGeneratorTestHost.CompileImage(library);
        var localHandler = includeLocalHandler ? """
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            [BusContract("root.command")]
            internal sealed record RootCommand;
            internal sealed class RootHandler : ICommandHandler<RootCommand>
            {
                public ValueTask HandleAsync(RootCommand command, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """ : string.Empty;
        var root = SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Root.Application", new[] { MetadataReference.CreateFromImage(libraryImage) },
            localHandler, """
            using System.Linq;
            using TinyBus;

            public static class Scenario
            {
                public static string[] Run()
                {
                    var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
                    var primary = manifest.CreateTopology(new ServiceIdentity("payments.primary"));
                    var recovery = manifest.CreateTopology(new ServiceIdentity("payments.recovery"));

                    return new[]
                    {
                        primary.Service.Value,
                        recovery.Service.Value,
                        DescribeMessages(primary),
                        DescribeMessages(recovery)
                    };
                }

                private static string DescribeMessages(ServiceTopology topology)
                {
                    return string.Join(";", topology.Messages.Select(message => string.Join("|",
                        message.Contract.Name,
                        message.Contract.Version,
                        message.Kind,
                        message.MessageType.FullName,
                        message.HandlerType.FullName,
                        message.ResponseType?.FullName)));
                }
            }
            """);

        Assert.Empty(SourceGeneratorTestHost.Run(root).Diagnostics);
        var result = SourceGeneratorTestHost.Execute<string[]>(root, libraryImage);

        var expectedMessages = includeLocalHandler
            ? "root.command|1|Command|RootCommand|RootHandler|;"
            : string.Empty;
        expectedMessages += "payments.status|3|Request|Payments.GetStatus|Payments.GetStatusHandler|Payments.Status";
        Assert.Equal(
            new[] { "payments.primary", "payments.recovery", expectedMessages, expectedMessages },
            result);
    }

    [Fact]
    public void Creates_an_empty_topology_with_the_supplied_service_identity()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using TinyBus;

            public static class Scenario
            {
                public static string Run()
                {
                    var topology = new TinyBus.Generated.GeneratedTinyBusManifest()
                        .CreateTopology(new ServiceIdentity("outbound-only"));

                    return $"{topology.Service.Value}|{topology.Messages.Count}";
                }
            }
            """);

        Assert.Equal("outbound-only|0", result);
    }
}
