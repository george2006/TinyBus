using System;
using System.Threading;
using System.Threading.Tasks;
using TinyBus.Sample.Contracts;

namespace TinyBus.Sample.Orders;

internal sealed class OrderPlacedHandler : IEventHandler<OrderPlaced>
{
    public ValueTask HandleAsync(
        OrderPlaced @event,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Orders handled OrderPlaced: {@event.OrderId}");
        return ValueTask.CompletedTask;
    }
}
