using System.Threading;
using System.Threading.Tasks;
using TinyBus.Sample.Contracts;

namespace TinyBus.Sample.Payments;

internal sealed class CapturePaymentHandler : ICommandHandler<CapturePayment>
{
    public ValueTask HandleAsync(
        CapturePayment command,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

internal sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced @event,
        CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}

internal sealed class GetPaymentStatusHandler
    : IRequestHandler<GetPaymentStatus, PaymentStatus>
{
    public ValueTask<PaymentStatus> HandleAsync(
        GetPaymentStatus request,
        CancellationToken cancellationToken)
    {
        var response = new PaymentStatus(request.PaymentId, "Unknown");
        return ValueTask.FromResult(response);
    }
}
