namespace TinyBus.SourceGen.Tests;

public sealed class ContractIdentityGenerationTests
{
    [Fact]
    public void Uses_the_fully_qualified_clr_name_by_convention()
    {
        var identity = ReadIdentity("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Orders.Contracts
            {
                public sealed record OrderPlaced;

                public sealed class Handler : IEventHandler<OrderPlaced>
                {
                    public ValueTask HandleAsync(
                        OrderPlaced @event,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;
                }
            }
            """);

        Assert.Equal("Orders.Contracts.OrderPlaced|1", identity);
    }

    [Fact]
    public void Uses_the_explicit_contract_name()
    {
        var identity = ReadIdentity("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Orders
            {
                [BusContract("orders.order-placed")]
                public sealed record OrderPlaced;

                public sealed class Handler : IEventHandler<OrderPlaced>
                {
                    public ValueTask HandleAsync(
                        OrderPlaced @event,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;
                }
            }
            """);

        Assert.Equal("orders.order-placed|1", identity);
    }

    [Fact]
    public void Uses_the_explicit_contract_version()
    {
        var identity = ReadIdentity("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Orders
            {
                [BusContract("orders.order-placed", Version = 2)]
                public sealed record OrderPlaced;

                public sealed class Handler : IEventHandler<OrderPlaced>
                {
                    public ValueTask HandleAsync(
                        OrderPlaced @event,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;
                }
            }
            """);

        Assert.Equal("orders.order-placed|2", identity);
    }

    [Fact]
    public void Escapes_explicit_contract_names_in_generated_source()
    {
        var identity = ReadIdentity("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            [BusContract("orders.\"placed\"\\next\0\a\b\f\n\r\t\v\u0085\u2028\u2029")]
            public sealed record OrderPlaced;

            public sealed class Handler : IEventHandler<OrderPlaced>
            {
                public ValueTask HandleAsync(
                    OrderPlaced @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }
            """);

        Assert.Equal("orders.\"placed\"\\next\0\a\b\f\n\r\t\v\u0085\u2028\u2029|1", identity);
    }

    private static string ReadIdentity(string declaration)
    {
        var source = declaration + """

            public static class Scenario
            {
                public static string Run()
                {
                    var manifest = new TinyBus.Generated.GeneratedTinyBusManifest();
                    var contract = manifest.Messages[0].Contract;
                    return $"{contract.Name}|{contract.Version}";
                }
            }
            """;

        return SourceGeneratorTestHost.Execute<string>(source);
    }
}
