using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TinyBus.SourceGen.Tests;

public sealed class ComposedManifestGenerationTests
{
    private const string Payments = """
        using System.Threading;
        using System.Threading.Tasks;
        using TinyBus;

        namespace Payments;

        [BusContract("payments.status", Version = 4)]
        internal sealed record GetStatus;
        internal sealed record Status;

        internal sealed class GetStatusHandler : IRequestHandler<GetStatus, Status>
        {
            public ValueTask<Status> HandleAsync(GetStatus request, CancellationToken cancellationToken)
                => ValueTask.FromResult(new Status());
        }

        [BusContract("payments.capture")]
        internal sealed record Capture;

        internal sealed class CaptureHandler : ICommandHandler<Capture>
        {
            public ValueTask HandleAsync(Capture command, CancellationToken cancellationToken)
                => ValueTask.CompletedTask;
        }
        """;

    private const string LocalHandler = """
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
        """;

    private const string DescribeManifest = """
        using System.Linq;

        public static class Scenario
        {
            public static string Run()
            {
                var messages = new TinyBus.Generated.GeneratedTinyBusManifest().Messages;
                return string.Join(";", messages.Select(message => string.Join("|",
                    message.Contract.Name,
                    message.Contract.Version,
                    message.Kind,
                    message.MessageType.FullName,
                    message.HandlerType.FullName,
                    message.ResponseType?.FullName)));
            }
        }
        """;

    [Fact]
    public void Composes_local_and_referenced_manifests_once_preserving_internal_types()
    {
        var payments = CompileLibrary("Payments", Payments);
        var orders = CompileLibrary("Orders", EventHandler("Orders"));
        var root = CreateRoot(new[] { payments, orders }, LocalHandler, DescribeManifest);

        var result = SourceGeneratorTestHost.Execute<string>(root, payments, orders);

        Assert.Equal(
            "root.command|1|Command|RootCommand|RootHandler|;" +
            "Orders.Changed|1|Event|Orders.Changed|Orders.Handler|;" +
            "payments.capture|1|Command|Payments.Capture|Payments.CaptureHandler|;" +
            "payments.status|4|Request|Payments.GetStatus|Payments.GetStatusHandler|Payments.Status",
            result);
    }

    [Fact]
    public void Composes_references_when_the_root_has_no_local_handlers()
    {
        var payments = CompileLibrary("Payments", Payments);
        var emptyLibrary = CompileLibrary("Empty", "public sealed class Empty;");
        var root = CreateRoot(new[] { emptyLibrary, payments }, DescribeManifest);

        var result = SourceGeneratorTestHost.Execute<string>(root, payments, emptyLibrary);

        Assert.Equal(
            "payments.capture|1|Command|Payments.Capture|Payments.CaptureHandler|;" +
            "payments.status|4|Request|Payments.GetStatus|Payments.GetStatusHandler|Payments.Status",
            result);
    }

    [Fact]
    public void Generates_and_executes_the_same_manifest_when_reference_and_source_order_change()
    {
        var payments = CompileLibrary("Payments", Payments);
        var orders = CompileLibrary("Orders", EventHandler("Orders"));
        var first = CreateRoot(new[] { payments, orders }, LocalHandler, DescribeManifest);
        var second = CreateRoot(new[] { orders, payments }, DescribeManifest, LocalHandler);

        Assert.Equal(ReadGeneratedSource(first), ReadGeneratedSource(second));
        Assert.Equal(
            SourceGeneratorTestHost.Execute<string>(first, payments, orders),
            SourceGeneratorTestHost.Execute<string>(second, orders, payments));
    }

    [Fact]
    public void Keeps_library_contributions_local_and_retains_shared_event_handlers()
    {
        var contracts = CompileLibrary("Contracts", "public sealed record OrderPlaced;");
        var accounting = CompileLibrary("Accounting", SharedEventHandler("Accounting"), contracts);
        var fulfilment = CompileLibrary(
            "Fulfilment", SharedEventHandler("Fulfilment"), contracts, accounting);
        var root = CreateRoot(new[] { fulfilment, accounting, contracts }, """
            using System;
            using System.Linq;
            using TinyBus;

            public static class Scenario
            {
                public static string Run()
                {
                    var messages = new TinyBus.Generated.GeneratedTinyBusManifest().Messages;
                    var contribution = typeof(Fulfilment.AssemblyMarker).Assembly
                        .GetCustomAttributes(typeof(BusMessageContributionAttribute), false)
                        .Cast<BusMessageContributionAttribute>()
                        .Single();
                    var local = (IBusManifest)Activator.CreateInstance(contribution.ManifestType)!;
                    var rootContributions = typeof(Scenario).Assembly
                        .GetCustomAttributes(typeof(BusMessageContributionAttribute), false);

                    return string.Join(",", messages.Select(message => message.HandlerType.FullName))
                        + $"|{local.Messages.Count}|{rootContributions.Length}";
                }
            }
            """);

        var result = SourceGeneratorTestHost.Execute<string>(root, contracts, accounting, fulfilment);

        Assert.Equal("Accounting.Handler,Fulfilment.Handler|1|0", result);
    }

    private static byte[] CompileLibrary(string assemblyName, string source, params byte[][] references)
    {
        var compilation = SourceGeneratorTestHost.CreateCompilationWithReferences(
            assemblyName,
            references.Select(image => MetadataReference.CreateFromImage(image)),
            source);

        return SourceGeneratorTestHost.CompileImage(compilation);
    }

    private static CSharpCompilation CreateRoot(byte[][] references, params string[] sources)
    {
        return SourceGeneratorTestHost.CreateCompilationWithReferences(
            "Root.Service",
            references.Select(image => MetadataReference.CreateFromImage(image)),
            sources);
    }

    private static string ReadGeneratedSource(CSharpCompilation compilation)
    {
        var run = SourceGeneratorTestHost.Run(compilation);
        var generated = SourceGeneratorTestHost.GetGeneratedSource(
            run,
            "TinyBus.Generated.Manifest.g.cs");

        return generated.SourceText.ToString();
    }

    private static string EventHandler(string assemblyName)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace {{assemblyName}};

            internal sealed record Changed;
            internal sealed class Handler : IEventHandler<Changed>
            {
                public ValueTask HandleAsync(Changed message, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """;
    }

    private static string SharedEventHandler(string assemblyName)
    {
        return $$"""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace {{assemblyName}};

            public sealed class AssemblyMarker;
            internal sealed class Handler : IEventHandler<OrderPlaced>
            {
                public ValueTask HandleAsync(OrderPlaced message, CancellationToken cancellationToken)
                    => ValueTask.CompletedTask;
            }
            """;
    }
}
