namespace TinyBus.Tests;

public sealed class TopologyAccumulatorTests
{
    [Fact]
    public void Accumulates_capabilities_and_loads_only_requested_command_routes()
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var rebuild = Command("orders.rebuild");
        var payments = Topology("payments", capture);
        var morePayments = Topology("payments", refund);
        var orders = Topology("orders", rebuild);
        var accumulator = new TopologyAccumulator();

        accumulator.Reconcile(payments);
        accumulator.Reconcile(morePayments);
        accumulator.Reconcile(orders);
        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var cache = accumulator.LoadCommandRoutes(requiredContracts);

        var foundCapture = cache.TryResolve(capture.Contract, out var captureOwner);
        var foundRefund = cache.TryResolve(refund.Contract, out var refundOwner);
        var foundUnrequested = cache.TryResolve(rebuild.Contract, out _);

        Assert.True(foundCapture);
        Assert.Equal(payments.Service, captureOwner);
        Assert.True(foundRefund);
        Assert.Equal(payments.Service, refundOwner);
        Assert.False(foundUnrequested);
    }

    [Fact]
    public void Repeated_replicas_do_not_duplicate_routes_or_event_subscriptions()
    {
        var capture = Command("payments.capture");
        var orderPlaced = Event("orders.placed");
        var firstReplica = Topology("payments", capture, orderPlaced);
        var secondReplica = Topology("payments", capture, orderPlaced);
        var accumulator = new TopologyAccumulator();

        accumulator.Reconcile(firstReplica);
        accumulator.Reconcile(secondReplica);
        accumulator.Reconcile(firstReplica);
        var requiredContracts = new[] { capture.Contract, capture.Contract };
        var cache = accumulator.LoadCommandRoutes(requiredContracts);
        var found = cache.TryResolve(capture.Contract, out var owner);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.True(found);
        Assert.Equal(firstReplica.Service, owner);
        var subscriber = Assert.Single(subscribers);
        Assert.Equal(firstReplica.Service, subscriber);
    }

    [Theory]
    [InlineData("payments", "checkout")]
    [InlineData("checkout", "payments")]
    public void Rejects_conflicting_owners_without_applying_any_of_the_contribution(
        string firstService,
        string secondService)
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var orderPlaced = Event("orders.placed");
        var original = Topology(firstService, capture);
        var conflicting = Topology(secondService, refund, orderPlaced, capture);
        var accumulator = new TopologyAccumulator();
        accumulator.Reconcile(original);

        var failure = Assert.Throws<InvalidOperationException>(() => accumulator.Reconcile(conflicting));

        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var cache = accumulator.LoadCommandRoutes(requiredContracts);
        var foundCapture = cache.TryResolve(capture.Contract, out var owner);
        var foundRefund = cache.TryResolve(refund.Contract, out _);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.Equal("Command 'payments.capture' version 1 has conflicting owners 'checkout' and 'payments'.", failure.Message);
        Assert.True(foundCapture);
        Assert.Equal(original.Service, owner);
        Assert.False(foundRefund);
        Assert.Empty(subscribers);
    }

    [Fact]
    public void Allows_multiple_services_to_subscribe_to_an_event()
    {
        var orderPlaced = Event("orders.placed");
        var payments = Topology("payments", orderPlaced);
        var notifications = Topology("notifications", orderPlaced);
        var accumulator = new TopologyAccumulator();

        accumulator.Reconcile(payments);
        accumulator.Reconcile(notifications);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);
        var requestedContracts = new[] { orderPlaced.Contract };
        var cache = accumulator.LoadCommandRoutes(requestedContracts);
        var foundCommand = cache.TryResolve(orderPlaced.Contract, out _);

        Assert.Equal(2, subscribers.Count);
        Assert.Contains(payments.Service, subscribers);
        Assert.Contains(notifications.Service, subscribers);
        Assert.False(foundCommand);
    }

    [Fact]
    public void New_routes_are_visible_only_after_explicit_cache_load()
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var accumulator = new TopologyAccumulator();
        var requiredContracts = new[] { capture.Contract };
        var originalCache = accumulator.LoadCommandRoutes(requiredContracts);

        accumulator.Reconcile(payments);

        var originalFound = originalCache.TryResolve(capture.Contract, out _);
        var refreshedCache = accumulator.LoadCommandRoutes(requiredContracts);
        var refreshedFound = refreshedCache.TryResolve(capture.Contract, out var owner);

        Assert.False(originalFound);
        Assert.True(refreshedFound);
        Assert.Equal(payments.Service, owner);
    }

    [Fact]
    public void An_older_replica_cannot_erase_newer_capabilities()
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var orderPlaced = Event("orders.placed");
        var olderReplica = Topology("payments", capture);
        var newerReplica = Topology("payments", capture, refund, orderPlaced);
        var accumulator = new TopologyAccumulator();

        accumulator.Reconcile(newerReplica);
        accumulator.Reconcile(olderReplica);
        var requiredContracts = new[] { refund.Contract };
        var cache = accumulator.LoadCommandRoutes(requiredContracts);
        var foundRefund = cache.TryResolve(refund.Contract, out var owner);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.True(foundRefund);
        Assert.Equal(newerReplica.Service, owner);
        var subscriber = Assert.Single(subscribers);
        Assert.Equal(newerReplica.Service, subscriber);
    }

    private static ServiceTopology Topology(string serviceName, params MessageDescriptor[] messages)
    {
        var service = new ServiceIdentity(serviceName);
        var topology = new ServiceTopology(service, messages);
        return topology;
    }

    private static MessageDescriptor Command(string name)
    {
        var contract = new ContractIdentity(name, 1);
        var descriptor = new MessageDescriptor(contract, typeof(TestMessage), typeof(TestHandler), MessageKind.Command);
        return descriptor;
    }

    private static MessageDescriptor Event(string name)
    {
        var contract = new ContractIdentity(name, 1);
        var descriptor = new MessageDescriptor(contract, typeof(TestMessage), typeof(TestHandler), MessageKind.Event);
        return descriptor;
    }

    private sealed record TestMessage;

    private sealed class TestHandler;
}
