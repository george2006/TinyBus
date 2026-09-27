namespace TinyBus.SourceGen.Tests;

public sealed class PortableContributionGenerationTests
{
    [Fact]
    public void Publishes_complete_request_contribution_metadata()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using System;
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments
            {
                [BusContract("payments.status", Version = 3)]
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
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var contribution = typeof(Payments.GetPaymentStatusHandler).Assembly
                        .GetCustomAttributes(typeof(BusMessageContributionAttribute), false)
                        .Cast<BusMessageContributionAttribute>()
                        .Single();
                    var manifest = (IBusManifest)Activator.CreateInstance(contribution.ManifestType)!;

                    return string.Join("|",
                        contribution.ContractName,
                        contribution.ContractVersion,
                        contribution.MessageType.FullName,
                        contribution.HandlerType.FullName,
                        contribution.Kind,
                        contribution.ResponseType!.FullName,
                        contribution.ManifestType.IsPublic,
                        manifest.Messages.Count);
                }
            }
            """);

        Assert.Equal(
            "payments.status|3|Payments.GetPaymentStatus|Payments.GetPaymentStatusHandler|Request|Payments.PaymentStatus|True|1",
            result);
    }

    [Fact]
    public void Points_every_contribution_to_the_same_assembly_manifest()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            public sealed record StartOrder;
            public sealed record OrderStarted;

            public sealed class StartOrderHandler : ICommandHandler<StartOrder>
            {
                public ValueTask HandleAsync(
                    StartOrder command,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public sealed class OrderStartedHandler : IEventHandler<OrderStarted>
            {
                public ValueTask HandleAsync(
                    OrderStarted @event,
                    CancellationToken cancellationToken) => ValueTask.CompletedTask;
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var contributions = typeof(StartOrderHandler).Assembly
                        .GetCustomAttributes(typeof(BusMessageContributionAttribute), false)
                        .Cast<BusMessageContributionAttribute>()
                        .ToArray();

                    return $"{contributions.Length}|{contributions.Select(value => value.ManifestType).Distinct().Count()}";
                }
            }
            """);

        Assert.Equal("2|1", result);
    }

    [Fact]
    public void Generates_distinct_manifest_names_for_similar_assembly_names()
    {
        var dottedName = ReadPublicManifestName("Orders.Service");
        var dashedName = ReadPublicManifestName("Orders-Service");

        Assert.StartsWith("TinyBusManifest_Orders_Service_", dottedName);
        Assert.StartsWith("TinyBusManifest_Orders_Service_", dashedName);
        Assert.NotEqual(dottedName, dashedName);
    }

    private static string ReadPublicManifestName(string assemblyName)
    {
        var run = SourceGeneratorTestHost.RunForAssembly(
            assemblyName,
            "public sealed class EmptyConsumer;");
        var generated = SourceGeneratorTestHost.GetGeneratedSource(
            run,
            "TinyBus.Generated.Manifest.g.cs");
        var source = generated.SourceText.ToString();
        const string declaration = "    public sealed partial class ";
        var line = source.Split('\n').Single(value =>
            value.StartsWith(declaration)
            && value.Contains(": global::TinyBus.IBusManifest"));

        return line.Substring(declaration.Length).Split(' ')[0];
    }
}
