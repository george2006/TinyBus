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

        var registrations = CopyRegistrations(services);
        var options = new TinyBusOptions(registrations);
        configure(options);
        options.Validate();

        var manifest = new TManifest();
        var topology = new ServiceTopology(options.ServiceIdentity, manifest.Messages);
        registrations.AddSingleton(topology);
        registrations.AddSingleton<IBus>(CreateBus);
        registrations.AddSingleton<IHostedService>(CreateRuntime);

        ApplyRegistrations(services, registrations);

        return services;
    }

    private static IServiceCollection CopyRegistrations(IServiceCollection services)
    {
        IServiceCollection registrations = new ServiceCollection();
        foreach (var registration in services)
        {
            registrations.Add(registration);
        }

        return registrations;
    }

    private static void ApplyRegistrations(IServiceCollection services, IServiceCollection registrations)
    {
        services.Clear();
        foreach (var registration in registrations)
        {
            services.Add(registration);
        }
    }

    private static IHostedService CreateRuntime(IServiceProvider services)
    {
        var transport = ResolveTransport(services);
        var topology = services.GetRequiredService<ServiceTopology>();
        var runtime = new TinyBusRuntime(transport, topology);

        return runtime;
    }

    private static IBus CreateBus(IServiceProvider services)
    {
        var transport = ResolveTransport(services);
        var bus = new Bus(transport);

        return bus;
    }

    private static ITransport ResolveTransport(IServiceProvider services)
    {
        var transports = services.GetServices<ITransport>();
        var registeredTransports = new List<ITransport>(transports);

        if (registeredTransports.Count != 1)
        {
            throw new InvalidOperationException("TinyBus requires exactly one transport provider.");
        }

        var transport = registeredTransports[0];

        return transport;
    }

    private static void ValidateSingleRuntime(IServiceCollection services)
    {
        foreach (var registration in services)
        {
            if (registration.ServiceType == typeof(ServiceTopology))
            {
                throw new InvalidOperationException("TinyBus is already registered in this service collection.");
            }
        }
    }
}
