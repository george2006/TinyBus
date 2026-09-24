namespace TinyBus.Tests;

public sealed class TopologyAccumulatorTests
{
    [Fact]
    public async Task Accumulates_capabilities_and_loads_only_requested_command_routes()
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var rebuild = Command("orders.rebuild");
        var payments = Topology("payments", capture);
        var morePayments = Topology("payments", refund);
        var orders = Topology("orders", rebuild);
        var accumulator = new TopologyAccumulator();

        await accumulator.ReconcileAsync(payments);
        await accumulator.ReconcileAsync(morePayments);
        await accumulator.ReconcileAsync(orders);
        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var cache = new CommandRouteCache(routes);

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
    public async Task Repeated_replicas_do_not_duplicate_routes_or_event_subscriptions()
    {
        var capture = Command("payments.capture");
        var orderPlaced = Event("orders.placed");
        var firstReplica = Topology("payments", capture, orderPlaced);
        var secondReplica = Topology("payments", capture, orderPlaced);
        var accumulator = new TopologyAccumulator();

        await accumulator.ReconcileAsync(firstReplica);
        await accumulator.ReconcileAsync(secondReplica);
        await accumulator.ReconcileAsync(firstReplica);
        var requiredContracts = new[] { capture.Contract, capture.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var cache = new CommandRouteCache(routes);
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
    public async Task Rejects_conflicting_owners_without_applying_any_of_the_contribution(
        string firstService,
        string secondService)
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var orderPlaced = Event("orders.placed");
        var original = Topology(firstService, capture);
        var conflicting = Topology(secondService, refund, orderPlaced, capture);
        var accumulator = new TopologyAccumulator();
        await accumulator.ReconcileAsync(original);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await accumulator.ReconcileAsync(conflicting));

        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var cache = new CommandRouteCache(routes);
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
    public async Task Allows_multiple_services_to_subscribe_to_an_event()
    {
        var orderPlaced = Event("orders.placed");
        var payments = Topology("payments", orderPlaced);
        var notifications = Topology("notifications", orderPlaced);
        var accumulator = new TopologyAccumulator();

        await accumulator.ReconcileAsync(payments);
        await accumulator.ReconcileAsync(notifications);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);
        var requestedContracts = new[] { orderPlaced.Contract };
        var routes = await accumulator.LoadAsync(requestedContracts);
        var cache = new CommandRouteCache(routes);
        var foundCommand = cache.TryResolve(orderPlaced.Contract, out _);

        Assert.Equal(2, subscribers.Count);
        Assert.Contains(payments.Service, subscribers);
        Assert.Contains(notifications.Service, subscribers);
        Assert.False(foundCommand);
    }

    [Fact]
    public async Task New_routes_are_visible_only_after_explicit_cache_load()
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var accumulator = new TopologyAccumulator();
        var requiredContracts = new[] { capture.Contract };
        var originalRoutes = await accumulator.LoadAsync(requiredContracts);
        var originalCache = new CommandRouteCache(originalRoutes);

        await accumulator.ReconcileAsync(payments);

        var originalFound = originalCache.TryResolve(capture.Contract, out _);
        var refreshedRoutes = await accumulator.LoadAsync(requiredContracts);
        var refreshedCache = new CommandRouteCache(refreshedRoutes);
        var refreshedFound = refreshedCache.TryResolve(capture.Contract, out var owner);

        Assert.False(originalFound);
        Assert.Empty(originalRoutes);
        Assert.Single(refreshedRoutes);
        Assert.True(refreshedFound);
        Assert.Equal(payments.Service, owner);
    }

    [Fact]
    public async Task An_older_replica_cannot_erase_newer_capabilities()
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var orderPlaced = Event("orders.placed");
        var olderReplica = Topology("payments", capture);
        var newerReplica = Topology("payments", capture, refund, orderPlaced);
        var accumulator = new TopologyAccumulator();

        await accumulator.ReconcileAsync(newerReplica);
        await accumulator.ReconcileAsync(olderReplica);
        var requiredContracts = new[] { refund.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var cache = new CommandRouteCache(routes);
        var foundRefund = cache.TryResolve(refund.Contract, out var owner);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.True(foundRefund);
        Assert.Equal(newerReplica.Service, owner);
        var subscriber = Assert.Single(subscribers);
        Assert.Equal(newerReplica.Service, subscriber);
    }

    [Fact]
    public async Task Reconciliation_can_complete_asynchronously()
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accumulator = new TopologyAccumulator { Availability = availability.Task };
        ITopologyReconciler reconciler = accumulator;
        ICommandRouteSource source = accumulator;

        var reconciliation = reconciler.ReconcileAsync(payments);

        Assert.False(reconciliation.IsCompleted);
        availability.SetResult();
        await reconciliation;

        var requiredContracts = new[] { capture.Contract };
        var routes = await source.LoadAsync(requiredContracts);
        var route = Assert.Single(routes);
        Assert.Equal(capture.Contract, route.Contract);
        Assert.Equal(payments.Service, route.Service);
    }

    [Fact]
    public async Task Route_loading_can_complete_asynchronously()
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var accumulator = new TopologyAccumulator();
        await accumulator.ReconcileAsync(payments);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accumulator.Availability = availability.Task;
        ICommandRouteSource source = accumulator;
        var requiredContracts = new[] { capture.Contract };

        var loading = source.LoadAsync(requiredContracts);

        Assert.False(loading.IsCompleted);
        availability.SetResult();
        var routes = await loading;
        var cache = new CommandRouteCache(routes);
        var found = cache.TryResolve(capture.Contract, out var owner);

        Assert.True(found);
        Assert.Equal(payments.Service, owner);
    }

    [Fact]
    public async Task Failed_reconciliation_does_not_register_capabilities()
    {
        var capture = Command("payments.capture");
        var orderPlaced = Event("orders.placed");
        var payments = Topology("payments", capture, orderPlaced);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accumulator = new TopologyAccumulator { Availability = availability.Task };
        ITopologyReconciler reconciler = accumulator;
        var failure = new InvalidOperationException("Shared topology is unavailable.");

        var reconciliation = reconciler.ReconcileAsync(payments);
        availability.SetException(failure);
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(async () => await reconciliation);

        accumulator.Availability = Task.CompletedTask;
        var requiredContracts = new[] { capture.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.Same(failure, observed);
        Assert.Empty(routes);
        Assert.Empty(subscribers);
    }

    [Fact]
    public async Task Failed_route_loading_does_not_return_an_empty_snapshot()
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var accumulator = new TopologyAccumulator();
        await accumulator.ReconcileAsync(payments);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accumulator.Availability = availability.Task;
        ICommandRouteSource source = accumulator;
        var failure = new InvalidOperationException("Shared topology is unavailable.");
        var requiredContracts = new[] { capture.Contract };

        var loading = source.LoadAsync(requiredContracts);
        availability.SetException(failure);
        var observed = await Assert.ThrowsAsync<InvalidOperationException>(async () => await loading);

        Assert.Same(failure, observed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_reconciliation_does_not_register_capabilities(bool cancelBeforeCall)
    {
        var capture = Command("payments.capture");
        var orderPlaced = Event("orders.placed");
        var payments = Topology("payments", capture, orderPlaced);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accumulator = new TopologyAccumulator();
        ITopologyReconciler reconciler = accumulator;
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        if (cancelBeforeCall)
        {
            cancellation.Cancel();
        }
        else
        {
            accumulator.Availability = availability.Task;
        }

        var reconciliation = reconciler.ReconcileAsync(payments, token);

        if (!cancelBeforeCall)
        {
            Assert.False(reconciliation.IsCompleted);
            cancellation.Cancel();
        }

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reconciliation);
        accumulator.Availability = Task.CompletedTask;
        var requiredContracts = new[] { capture.Contract };
        var routes = await accumulator.LoadAsync(requiredContracts);
        var subscribers = accumulator.GetEventSubscribers(orderPlaced.Contract);

        Assert.Equal(token, observed.CancellationToken);
        Assert.Empty(routes);
        Assert.Empty(subscribers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_route_loading_does_not_return_an_empty_snapshot(bool cancelBeforeCall)
    {
        var capture = Command("payments.capture");
        var payments = Topology("payments", capture);
        var accumulator = new TopologyAccumulator();
        await accumulator.ReconcileAsync(payments);
        var availability = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ICommandRouteSource source = accumulator;
        var requiredContracts = new[] { capture.Contract };
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        if (cancelBeforeCall)
        {
            cancellation.Cancel();
        }
        else
        {
            accumulator.Availability = availability.Task;
        }

        var loading = source.LoadAsync(requiredContracts, token);

        if (!cancelBeforeCall)
        {
            Assert.False(loading.IsCompleted);
            cancellation.Cancel();
        }

        var observed = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await loading);

        Assert.Equal(token, observed.CancellationToken);
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
