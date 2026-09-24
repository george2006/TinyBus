using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlRuntimeTests
{
    [Fact]
    public async Task Starts_only_after_reconciling_and_publishing_required_routes()
    {
        var command = Command("payments.capture");
        var topology = Topology("payments", command);
        var requiredContracts = new[] { command.Contract };
        var accumulator = new TopologyAccumulator();
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(accumulator, accumulator, requiredContracts, cache);
        using var host = CreateHost(transport, topology);

        await host.StartAsync();

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        var found = cache.TryResolve(command.Contract, out var owner);
        Assert.True(lifetime.ApplicationStarted.IsCancellationRequested);
        Assert.True(found);
        Assert.Equal(topology.Service, owner);

        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Startup_waits_for_reconciliation_and_loading(bool delayReconciliation)
    {
        var command = Command("payments.capture");
        var topology = Topology("payments", command);
        var requiredContracts = new[] { command.Contract };
        var reconciler = new TopologyAccumulator();
        var source = new TopologyAccumulator();
        await source.ReconcileAsync(topology);

        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedProvider = delayReconciliation ? reconciler : source;
        delayedProvider.Availability = available.Task;
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(reconciler, source, requiredContracts, cache);
        using var host = CreateHost(transport, topology);
        var startupTimeout = TimeSpan.FromSeconds(10);
        using var timeout = new CancellationTokenSource(startupTimeout);

        var starting = host.StartAsync(timeout.Token);

        Assert.False(starting.IsCompleted);
        AssertNotReady(host, cache, command.Contract);

        available.SetResult();
        await starting;

        var found = cache.TryResolve(command.Contract, out var owner);
        Assert.True(found);
        Assert.Equal(topology.Service, owner);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initial_provider_failure_fails_startup(bool failReconciliation)
    {
        var command = Command("payments.capture");
        var topology = Topology("payments", command);
        var requiredContracts = new[] { command.Contract };
        var reconciler = new TopologyAccumulator();
        var source = new TopologyAccumulator();
        await source.ReconcileAsync(topology);

        var providerError = new InvalidOperationException("Topology unavailable.");
        var failedProvider = failReconciliation ? reconciler : source;
        failedProvider.Availability = Task.FromException(providerError);
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(reconciler, source, requiredContracts, cache);
        using var host = CreateHost(transport, topology);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Same(providerError, failure);
        AssertNotReady(host, cache, command.Contract);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initial_cancellation_fails_startup(bool cancelReconciliation)
    {
        var command = Command("payments.capture");
        var topology = Topology("payments", command);
        var requiredContracts = new[] { command.Contract };
        var reconciler = new TopologyAccumulator();
        var source = new TopologyAccumulator();
        await source.ReconcileAsync(topology);

        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedProvider = cancelReconciliation ? reconciler : source;
        delayedProvider.Availability = available.Task;
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(reconciler, source, requiredContracts, cache);
        using var host = CreateHost(transport, topology);
        using var cancellation = new CancellationTokenSource();

        var starting = host.StartAsync(cancellation.Token);
        Assert.False(starting.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        AssertNotReady(host, cache, command.Contract);
    }

    [Fact]
    public async Task Missing_required_owner_fails_startup_without_publishing_partial_routes()
    {
        var capture = Command("payments.capture");
        var refund = Command("payments.refund");
        var topology = Topology("payments", capture);
        var requiredContracts = new[] { capture.Contract, refund.Contract };
        var accumulator = new TopologyAccumulator();
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(accumulator, accumulator, requiredContracts, cache);
        using var host = CreateHost(transport, topology);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("Required command 'payments.refund' version 1 has no owner.", failure.Message);
        AssertNotReady(host, cache, capture.Contract);
        var registeredRoutes = await accumulator.LoadAsync(requiredContracts);
        var retainedRoute = Assert.Single(registeredRoutes);
        Assert.Equal(capture.Contract, retainedRoute.Contract);
    }

    [Fact]
    public async Task Conflicting_ownership_fails_startup()
    {
        var command = Command("payments.capture");
        var original = Topology("payments", command);
        var conflicting = Topology("checkout", command);
        var requiredContracts = new[] { command.Contract };
        var accumulator = new TopologyAccumulator();
        await accumulator.ReconcileAsync(original);
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(accumulator, accumulator, requiredContracts, cache);
        using var host = CreateHost(transport, conflicting);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("Command 'payments.capture' version 1 has conflicting owners 'checkout' and 'payments'.", failure.Message);
        AssertNotReady(host, cache, command.Contract);
    }

    [Fact]
    public async Task No_required_commands_allows_an_initialized_empty_snapshot()
    {
        var topology = Topology("notifications");
        var requiredContracts = Array.Empty<ContractIdentity>();
        var accumulator = new TopologyAccumulator();
        var cache = new CommandRouteCache();
        var transport = new RouteTestTransport(accumulator, accumulator, requiredContracts, cache);
        using var host = CreateHost(transport, topology);

        await host.StartAsync();

        var unknown = new ContractIdentity("payments.capture", 1);
        var found = cache.TryResolve(unknown, out _);
        Assert.False(found);
        await host.StopAsync();
    }

    private static IHost CreateHost(ITransport transport, ServiceTopology topology)
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        var runtime = new TinyBusRuntime(transport, topology);
        builder.Services.AddSingleton<IHostedService>(runtime);
        return builder.Build();
    }

    private static void AssertNotReady(IHost host, CommandRouteCache cache, ContractIdentity contract)
    {
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
        var failure = Assert.Throws<InvalidOperationException>(() => cache.TryResolve(contract, out _));
        Assert.Equal("Command routes have not been initialized.", failure.Message);
    }

    private static MessageDescriptor Command(string name)
    {
        var contract = new ContractIdentity(name, 1);
        return new MessageDescriptor(contract, typeof(object), typeof(object), MessageKind.Command);
    }

    private static ServiceTopology Topology(string name, params MessageDescriptor[] messages)
    {
        var service = new ServiceIdentity(name);
        return new ServiceTopology(service, messages);
    }
}
