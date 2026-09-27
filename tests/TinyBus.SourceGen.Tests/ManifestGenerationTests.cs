namespace TinyBus.SourceGen.Tests;

public sealed class ManifestGenerationTests
{
    private const string Commands = """
        using System.Threading;
        using System.Threading.Tasks;
        using TinyBus;

        namespace Messages
        {
            [BusContract("alpha", Version = 2)]
            public sealed record AlphaCommand;

            public sealed class AlphaCommandHandler : ICommandHandler<AlphaCommand>
            {
                public ValueTask HandleAsync(
                    AlphaCommand command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
        }
        """;

    private const string EventsAndRequests = """
        using System.Threading;
        using System.Threading.Tasks;
        using TinyBus;

        namespace Messages
        {
            [BusContract("beta")]
            public sealed record BetaEvent;

            [BusContract("alpha")]
            public sealed record AlphaRequest;

            public sealed record AlphaResponse;

            public sealed class BetaEventHandler : IEventHandler<BetaEvent>
            {
                public ValueTask HandleAsync(
                    BetaEvent @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public sealed class AlphaRequestHandler : IRequestHandler<AlphaRequest, AlphaResponse>
            {
                public ValueTask<AlphaResponse> HandleAsync(
                    AlphaRequest request,
                    CancellationToken cancellationToken) =>
                    ValueTask.FromResult(new AlphaResponse());
            }
        }
        """;

    [Fact]
    public void Produces_the_same_manifest_when_source_file_order_changes()
    {
        var commandsFirst = ReadGeneratedManifest(Commands, EventsAndRequests);
        var eventsFirst = ReadGeneratedManifest(EventsAndRequests, Commands);

        Assert.Equal(commandsFirst, eventsFirst);
    }

    [Fact]
    public void Retains_multiple_handlers_for_the_same_event()
    {
        var handlers = SourceGeneratorTestHost.Execute<string>("""
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record OrderPlaced;

            public sealed class AccountingHandler : IEventHandler<OrderPlaced>
            {
                public ValueTask HandleAsync(
                    OrderPlaced @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public sealed class FulfilmentHandler : IEventHandler<OrderPlaced>
            {
                public ValueTask HandleAsync(
                    OrderPlaced @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var messages = new TinyBus.Generated.GeneratedTinyBusManifest().Messages;
                    return string.Join(",", messages.Select(message => message.HandlerType.Name));
                }
            }
            """);

        Assert.Equal("AccountingHandler,FulfilmentHandler", handlers);
    }

    [Fact]
    public void Generates_an_empty_manifest_when_the_assembly_has_no_handlers()
    {
        var messageCount = SourceGeneratorTestHost.Execute<int>("""
            public static class Scenario
            {
                public static int Run()
                {
                    return new TinyBus.Generated.GeneratedTinyBusManifest().Messages.Count;
                }
            }
            """);

        Assert.Equal(0, messageCount);
    }

    private static string ReadGeneratedManifest(params string[] sources)
    {
        var run = SourceGeneratorTestHost.Run(sources);
        var generated = SourceGeneratorTestHost.GetGeneratedSource(
            run,
            "TinyBus.Generated.Manifest.g.cs");

        return generated.SourceText.ToString();
    }
}
