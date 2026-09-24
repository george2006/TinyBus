using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyBus.Tests;

public sealed class TinyBusRegistrationTests
{
    [Fact]
    public async Task Selected_transport_supplies_both_topology_capabilities()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddTinyBus<CommandManifest>(bus =>
        {
            bus.Service("payments");
            bus.UseTestTransport();
        });
        using var host = builder.Build();

        await host.StartAsync();

        var accumulator = host.Services.GetRequiredService<TopologyAccumulator>();
        var reconciler = host.Services.GetRequiredService<ITopologyReconciler>();
        var source = host.Services.GetRequiredService<ICommandRouteSource>();
        Assert.Same(accumulator, reconciler);
        Assert.Same(accumulator, source);

        var contract = new ContractIdentity("payments.capture", 1);
        var requiredContracts = new[] { contract };
        var routes = await source.LoadAsync(requiredContracts);
        var route = Assert.Single(routes);
        var expectedOwner = new ServiceIdentity("payments");
        Assert.Equal(contract, route.Contract);
        Assert.Equal(expectedOwner, route.Service);
        await host.StopAsync();
    }

    [Fact]
    public void Provider_defaults_preserve_an_existing_capability_override()
    {
        var services = new ServiceCollection();
        var customReconciler = new TopologyAccumulator();
        services.AddSingleton<ITopologyReconciler>(customReconciler);
        services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.Service("payments");
            bus.UseTestTransport();
        });
        using var provider = services.BuildServiceProvider();

        var reconciler = provider.GetRequiredService<ITopologyReconciler>();
        var source = provider.GetRequiredService<ICommandRouteSource>();
        var defaultProvider = provider.GetRequiredService<TopologyAccumulator>();

        Assert.Same(customReconciler, reconciler);
        Assert.Same(defaultProvider, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_configuration_does_not_apply_provider_registrations(bool callbackThrows)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TopologyAccumulator>();
        var originalRegistrations = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.UseTestTransport();
            if (callbackThrows)
            {
                throw new InvalidOperationException("Invalid provider configuration.");
            }
        }));

        Assert.Equal(originalRegistrations, services);
    }

    [Fact]
    public async Task Registers_a_single_runtime_and_gates_the_host_on_its_providers()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.Services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.Service("payments");
            bus.UseTestTransport();
        });
        using var host = builder.Build();
        var accumulator = host.Services.GetRequiredService<TopologyAccumulator>();
        accumulator.Availability = available.Task;
        var timeout = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource(timeout);

        var starting = host.StartAsync(cancellation.Token);

        Assert.False(starting.IsCompleted);
        var cache = host.Services.GetRequiredService<CommandRouteCache>();
        var command = new ContractIdentity("payments.capture", 1);
        Assert.Throws<InvalidOperationException>(() => cache.TryResolve(command, out _));

        available.SetResult();
        await starting;

        var topology = host.Services.GetRequiredService<ServiceTopology>();
        var expectedService = new ServiceIdentity("payments");
        Assert.Equal(expectedService, topology.Service);
        Assert.Empty(topology.Messages);
        var found = cache.TryResolve(command, out _);
        Assert.False(found);
        var workers = host.Services.GetServices<IHostedService>();
        Assert.Single(workers);
        await host.StopAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_provider_fails_host_startup(bool missingReconciler)
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        var accumulator = new TopologyAccumulator();
        if (missingReconciler)
        {
            builder.Services.AddSingleton<ICommandRouteSource>(accumulator);
        }
        else
        {
            builder.Services.AddSingleton<ITopologyReconciler>(accumulator);
        }

        builder.Services.AddTinyBus<EmptyManifest>(bus => bus.Service("payments"));
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        var missingProvider = missingReconciler ? nameof(ITopologyReconciler) : nameof(ICommandRouteSource);
        Assert.Contains(missingProvider, failure.Message);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    [Fact]
    public void Missing_service_identity_leaves_the_service_collection_unchanged()
    {
        var services = new ServiceCollection();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddTinyBus<EmptyManifest>(bus => { }));

        Assert.Contains("bus.Service(name)", failure.Message);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Invalid_service_identity_leaves_the_service_collection_unchanged(string? name)
    {
        var services = new ServiceCollection();

        Assert.ThrowsAny<ArgumentException>(() =>
            services.AddTinyBus<EmptyManifest>(bus => bus.Service(name!)));

        Assert.Empty(services);
    }

    [Fact]
    public void Repeated_bus_registration_preserves_the_original_runtime()
    {
        var services = new ServiceCollection();
        services.AddTinyBus<EmptyManifest>(bus => bus.Service("payments"));
        var originalRegistrations = services.ToArray();

        var failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddTinyBus<EmptyManifest>(bus => bus.Service("orders")));

        Assert.Equal("TinyBus is already registered in this service collection.", failure.Message);
        Assert.Equal(originalRegistrations, services);
    }

    public sealed class EmptyManifest : IBusManifest
    {
        public IReadOnlyList<MessageDescriptor> Messages { get; } = Array.Empty<MessageDescriptor>();
    }

    public sealed class CommandManifest : IBusManifest
    {
        public CommandManifest()
        {
            var contract = new ContractIdentity("payments.capture", 1);
            var message = new MessageDescriptor(contract, typeof(object), typeof(object), MessageKind.Command);
            Messages = new[] { message };
        }

        public IReadOnlyList<MessageDescriptor> Messages { get; }
    }
}
