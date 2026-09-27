using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyBus.Tests;

public sealed class TinyBusRegistrationTests
{
    [Fact]
    public void Provider_defaults_preserve_an_existing_capability_override()
    {
        var services = new ServiceCollection();
        var customTransport = new NativeTestTransport();
        services.AddSingleton(customTransport);
        services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.Service("payments");
            bus.UseTestTransport();
        });
        using var provider = services.BuildServiceProvider();

        var transport = provider.GetRequiredService<NativeTestTransport>();

        Assert.Same(customTransport, transport);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_configuration_does_not_apply_provider_registrations(bool callbackThrows)
    {
        var services = new ServiceCollection();
        services.AddSingleton<NativeTestTransport>();
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
        builder.Services.AddSingleton<IIncomingMessagePipeline, NoOpIncomingMessagePipeline>();
        builder.Services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.Service("payments");
            bus.MaximumConcurrentMessages = 12;
            bus.UseTestTransport();
        });
        using var host = builder.Build();
        var transport = host.Services.GetRequiredService<NativeTestTransport>();
        transport.Availability = available.Task;
        var timeout = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource(timeout);

        var starting = host.StartAsync(cancellation.Token);

        Assert.False(starting.IsCompleted);
        available.SetResult();
        await starting;

        var topology = host.Services.GetRequiredService<ServiceTopology>();
        var bus = host.Services.GetRequiredService<IBus>();
        var sameBus = host.Services.GetRequiredService<IBus>();
        var expectedService = new ServiceIdentity("payments");
        var runtimeSettings = host.Services.GetRequiredService<TinyBusRuntimeSettings>();
        Assert.Equal(expectedService, topology.Service);
        Assert.Equal(12, runtimeSettings.MaximumConcurrentMessages);
        Assert.Empty(topology.Messages);
        Assert.Same(topology, transport.InitializedTopology);
        Assert.Same(bus, sameBus);
        var workers = host.Services.GetServices<IHostedService>();
        Assert.Single(workers);
        await host.StopAsync();
    }

    [Fact]
    public async Task Missing_transport_fails_host_startup()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddTinyBus<EmptyManifest>(bus => bus.Service("payments"));
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("TinyBus requires exactly one transport provider.", failure.Message);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    [Fact]
    public async Task Multiple_transports_fail_host_startup()
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        builder.Services.AddTinyBus<EmptyManifest>(bus =>
        {
            bus.Service("payments");
            bus.UseTestTransport();
            bus.UseTestTransport();
        });
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Equal("TinyBus requires exactly one transport provider.", failure.Message);
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

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Invalid_message_concurrency_leaves_the_service_collection_unchanged(
        int maximumConcurrentMessages)
    {
        var services = new ServiceCollection();

        void RegisterTinyBus()
        {
            services.AddTinyBus<EmptyManifest>(bus =>
            {
                bus.Service("payments");
                bus.MaximumConcurrentMessages = maximumConcurrentMessages;
            });
        }

        Assert.Throws<ArgumentOutOfRangeException>(RegisterTinyBus);
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

}
