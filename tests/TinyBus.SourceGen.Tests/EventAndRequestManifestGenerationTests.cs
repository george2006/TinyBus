namespace TinyBus.SourceGen.Tests;

public sealed class EventAndRequestManifestGenerationTests
{
    [Fact]
    public void Generates_an_event_descriptor()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Orders
            {
                public sealed record OrderPlaced(System.Guid OrderId);

                public sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
                {
                    public ValueTask HandleAsync(
                        OrderPlaced @event,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var descriptor = new TinyBus.Generated.GeneratedTinyBusManifest().Messages[0];
                    return $"{descriptor.Contract.Name}|{descriptor.MessageType.FullName}|{descriptor.HandlerType.FullName}|{descriptor.Kind}|{descriptor.ResponseType}";
                }
            }
            """);

        Assert.Equal(
            "Orders.OrderPlaced|Orders.OrderPlaced|Orders.OrderPlacedHandler|Event|",
            result);
    }

    [Fact]
    public void Generates_a_request_descriptor_with_its_response_type()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Payments
            {
                public sealed record GetPaymentStatus(System.Guid PaymentId);
                public sealed record PaymentStatus(System.Guid PaymentId, string Status);

                public sealed class GetPaymentStatusHandler
                    : IRequestHandler<GetPaymentStatus, PaymentStatus>
                {
                    public ValueTask<PaymentStatus> HandleAsync(
                        GetPaymentStatus request,
                        CancellationToken cancellationToken) =>
                        ValueTask.FromResult(new PaymentStatus(request.PaymentId, "Captured"));
                }
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var descriptor = new TinyBus.Generated.GeneratedTinyBusManifest().Messages[0];
                    return $"{descriptor.Contract.Name}|{descriptor.Kind}|{descriptor.ResponseType!.FullName}";
                }
            }
            """);

        Assert.Equal("Payments.GetPaymentStatus|Request|Payments.PaymentStatus", result);
    }

    [Fact]
    public void Generates_every_contract_implemented_by_one_handler_class()
    {
        var result = SourceGeneratorTestHost.Execute<string>("""
            using System.Linq;
            using System.Threading;
            using System.Threading.Tasks;
            using TinyBus;

            namespace Billing
            {
                public sealed record CapturePayment;
                public sealed record PaymentCaptured;

                public sealed class PaymentHandler
                    : ICommandHandler<CapturePayment>, IEventHandler<PaymentCaptured>
                {
                    public ValueTask HandleAsync(
                        CapturePayment command,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;

                    public ValueTask HandleAsync(
                        PaymentCaptured @event,
                        CancellationToken cancellationToken) => ValueTask.CompletedTask;
                }
            }

            public static class Scenario
            {
                public static string Run()
                {
                    var messages = new TinyBus.Generated.GeneratedTinyBusManifest().Messages;
                    return string.Join(",", messages.Select(message => $"{message.Contract.Name}:{message.Kind}"));
                }
            }
            """);

        Assert.Equal(
            "Billing.CapturePayment:Command,Billing.PaymentCaptured:Event",
            result);
    }
}
