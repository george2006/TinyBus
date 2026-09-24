namespace TinyBus.PostgreSql.Tests;

public sealed class CommandRouteCacheTests
{
    [Fact]
    public void Rejects_lookup_before_initialization()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var cache = new CommandRouteCache();

        var failure = Assert.Throws<InvalidOperationException>(() => cache.TryResolve(capture, out _));

        Assert.Equal("Command routes have not been initialized.", failure.Message);
    }

    [Fact]
    public void Resolves_by_contract_name_and_version()
    {
        var firstVersion = new ContractIdentity("payments.capture", 1);
        var secondVersion = new ContractIdentity("payments.capture", 2);
        var unknownVersion = new ContractIdentity("payments.capture", 3);
        var payments = new ServiceIdentity("payments");
        var upgradedPayments = new ServiceIdentity("payments-v2");
        var firstRoute = new CommandRoute(firstVersion, payments);
        var secondRoute = new CommandRoute(secondVersion, upgradedPayments);
        var routes = new[] { firstRoute, secondRoute };
        var cache = new CommandRouteCache(routes);

        var foundFirst = cache.TryResolve(firstVersion, out var firstOwner);
        var foundSecond = cache.TryResolve(secondVersion, out var secondOwner);
        var foundUnknown = cache.TryResolve(unknownVersion, out _);

        Assert.True(foundFirst);
        Assert.Equal(payments, firstOwner);
        Assert.True(foundSecond);
        Assert.Equal(upgradedPayments, secondOwner);
        Assert.False(foundUnknown);
    }

    [Fact]
    public void Accepts_repeated_routes_to_the_same_owner()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var payments = new ServiceIdentity("payments");
        var route = new CommandRoute(capture, payments);
        var routes = new[] { route, route };

        var cache = new CommandRouteCache(routes);
        var found = cache.TryResolve(capture, out var owner);

        Assert.True(found);
        Assert.Equal(payments, owner);
    }

    [Theory]
    [InlineData("payments", "checkout")]
    [InlineData("checkout", "payments")]
    public void Rejects_conflicting_owners_before_exposing_a_route_cache(
        string firstService,
        string secondService)
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var firstOwner = new ServiceIdentity(firstService);
        var secondOwner = new ServiceIdentity(secondService);
        var firstRoute = new CommandRoute(capture, firstOwner);
        var secondRoute = new CommandRoute(capture, secondOwner);
        var routes = new[] { firstRoute, secondRoute };

        var failure = Assert.Throws<InvalidOperationException>(() =>
        {
            var cache = new CommandRouteCache(routes);
        });

        Assert.Equal("Command 'payments.capture' version 1 has conflicting owners 'checkout' and 'payments'.", failure.Message);
    }

    [Fact]
    public void Keeps_a_snapshot_when_the_source_changes()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var payments = new ServiceIdentity("payments");
        var checkout = new ServiceIdentity("checkout");
        var originalRoute = new CommandRoute(capture, payments);
        var replacementRoute = new CommandRoute(capture, checkout);
        var routes = new[] { originalRoute };
        var cache = new CommandRouteCache(routes);

        routes[0] = replacementRoute;
        var found = cache.TryResolve(capture, out var owner);

        Assert.True(found);
        Assert.Equal(payments, owner);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Conflicting_replacement_preserves_the_previous_state(bool initialized)
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var payments = new ServiceIdentity("payments");
        var checkout = new ServiceIdentity("checkout");
        var original = new CommandRoute(capture, payments);
        var conflicting = new CommandRoute(capture, checkout);
        var originalRoutes = new[] { original };
        var conflictingRoutes = new[] { original, conflicting };
        var cache = new CommandRouteCache();
        if (initialized)
        {
            cache.Replace(originalRoutes);
        }

        Assert.Throws<InvalidOperationException>(() => cache.Replace(conflictingRoutes));

        if (!initialized)
        {
            Assert.Throws<InvalidOperationException>(() => cache.TryResolve(capture, out _));
            return;
        }

        var found = cache.TryResolve(capture, out var owner);
        Assert.True(found);
        Assert.Equal(payments, owner);
    }

    [Fact]
    public void Cancelled_replacement_preserves_the_previous_snapshot()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var payments = new ServiceIdentity("payments");
        var original = new CommandRoute(capture, payments);
        var routes = new[] { original };
        var cache = new CommandRouteCache(routes);
        var replacement = Array.Empty<CommandRoute>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => cache.Replace(replacement, cancellation.Token));

        var found = cache.TryResolve(capture, out var owner);
        Assert.True(found);
        Assert.Equal(payments, owner);
    }

    [Fact]
    public async Task Readers_keep_the_old_snapshot_until_replacement_is_complete()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var refund = new ContractIdentity("payments.refund", 1);
        var payments = new ServiceIdentity("payments");
        var original = new CommandRoute(capture, payments);
        var added = new CommandRoute(refund, payments);
        var originalRoutes = new[] { original };
        var cache = new CommandRouteCache(originalRoutes);
        var constructionPaused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var continueConstruction = new ManualResetEventSlim();
        var replacement = PauseDuringEnumeration(added, original, constructionPaused, continueConstruction);
        var replacing = Task.Run(() => cache.Replace(replacement));
        var timeout = TimeSpan.FromSeconds(10);

        try
        {
            await constructionPaused.Task.WaitAsync(timeout);

            for (var index = 0; index < 1_000; index++)
            {
                var foundCapture = cache.TryResolve(capture, out var owner);
                var foundRefund = cache.TryResolve(refund, out _);
                Assert.True(foundCapture);
                Assert.Equal(payments, owner);
                Assert.False(foundRefund);
            }
        }
        finally
        {
            continueConstruction.Set();
            await replacing;
        }

        var foundReplacement = cache.TryResolve(refund, out var refundOwner);
        Assert.True(foundReplacement);
        Assert.Equal(payments, refundOwner);
    }

    [Fact]
    public void Warm_lookup_allocates_no_memory()
    {
        var capture = new ContractIdentity("payments.capture", 1);
        var payments = new ServiceIdentity("payments");
        var route = new CommandRoute(capture, payments);
        var routes = new[] { route };
        var cache = new CommandRouteCache(routes);

        for (var index = 0; index < 10_000; index++)
        {
            cache.TryResolve(capture, out _);
        }

        var foundCount = 0;
        var before = GC.GetAllocatedBytesForCurrentThread();

        for (var index = 0; index < 10_000; index++)
        {
            if (cache.TryResolve(capture, out var owner) && owner == payments)
            {
                foundCount++;
            }
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        var allocatedBytes = after - before;

        Assert.Equal(10_000, foundCount);
        Assert.Equal(0L, allocatedBytes);
    }

    private static IEnumerable<CommandRoute> PauseDuringEnumeration(
        CommandRoute first,
        CommandRoute second,
        TaskCompletionSource constructionPaused,
        ManualResetEventSlim continueConstruction)
    {
        yield return first;
        constructionPaused.SetResult();

        var timeout = TimeSpan.FromSeconds(10);
        if (!continueConstruction.Wait(timeout))
        {
            throw new TimeoutException("Snapshot construction was not released.");
        }

        yield return second;
    }
}
