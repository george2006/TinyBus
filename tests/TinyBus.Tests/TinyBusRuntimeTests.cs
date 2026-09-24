using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyBus.Tests;

public sealed class TinyBusRuntimeTests
{
    [Fact]
    public async Task Transport_failure_prevents_host_readiness()
    {
        var providerError = new InvalidOperationException("Transport unavailable.");
        var transport = new NativeTestTransport();
        transport.Availability = Task.FromException(providerError);
        using var host = CreateHost(transport);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync());

        Assert.Same(providerError, failure);
        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    [Fact]
    public async Task Transport_cancellation_prevents_host_readiness()
    {
        var available = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new NativeTestTransport { Availability = available.Task };
        using var host = CreateHost(transport);
        using var cancellation = new CancellationTokenSource();

        var starting = host.StartAsync(cancellation.Token);
        Assert.False(starting.IsCompleted);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);

        var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
        Assert.False(lifetime.ApplicationStarted.IsCancellationRequested);
    }

    private static IHost CreateHost(ITransport transport)
    {
        var settings = new HostApplicationBuilderSettings { DisableDefaults = true };
        var builder = new HostApplicationBuilder(settings);
        builder.Logging.ClearProviders();
        var service = new ServiceIdentity("payments");
        var messages = Array.Empty<MessageDescriptor>();
        var topology = new ServiceTopology(service, messages);
        var runtime = new TinyBusRuntime(transport, topology);
        builder.Services.AddSingleton<IHostedService>(runtime);

        return builder.Build();
    }
}
