using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyBus;

public static class TinyBusServiceCollectionExtensions
{
    /// <summary>
    /// Registers the runtime for a compile-time manifest. The generated AddTinyBus overload
    /// selects the application's composed manifest and registers its handlers.
    /// </summary>
    public static IServiceCollection AddTinyBus<TManifest>(
        this IServiceCollection services,
        Action<TinyBusOptions> configure)
        where TManifest : IBusManifest, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        ValidateSingleRuntime(services);

        var options = new TinyBusOptions();
        configure(options);
        options.Validate();

        var manifest = new TManifest();
        var topology = new ServiceTopology(options.ServiceIdentity, manifest.Messages);
        // This registration slice configures inbound capabilities; outbound declarations follow separately.
        var requiredCommands = Array.Empty<ContractIdentity>();
        var cache = new CommandRouteCache();

        services.AddSingleton(topology);
        services.AddSingleton(cache);
        RegisterTopologyWorker(services, topology, requiredCommands, cache);

        return services;
    }

    private static void RegisterTopologyWorker(
        IServiceCollection services,
        ServiceTopology topology,
        IReadOnlyCollection<ContractIdentity> requiredCommands,
        CommandRouteCache cache)
    {
        Func<IServiceProvider, IHostedService> createWorker = provider =>
        {
            var reconciler = provider.GetRequiredService<ITopologyReconciler>();
            var source = provider.GetRequiredService<ICommandRouteSource>();
            return new TopologyWorker(reconciler, source, topology, requiredCommands, cache);
        };

        services.AddSingleton<IHostedService>(createWorker);
    }

    private static void ValidateSingleRuntime(IServiceCollection services)
    {
        foreach (var registration in services)
        {
            if (registration.ServiceType == typeof(CommandRouteCache))
            {
                throw new InvalidOperationException("TinyBus is already registered in this service collection.");
            }
        }
    }
}
