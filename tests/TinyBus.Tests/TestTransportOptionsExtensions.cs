using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyBus.Tests;

// Exercises the extension point used by independently packaged transport providers.
internal static class TestTransportOptionsExtensions
{
    public static void UseTestTransport(this TinyBusOptions options)
    {
        var services = options.Services;
        services.TryAddSingleton<TopologyAccumulator>();

        Func<IServiceProvider, TopologyAccumulator> resolveAccumulator = ResolveAccumulator;
        services.TryAddSingleton<ITopologyReconciler>(resolveAccumulator);
        services.TryAddSingleton<ICommandRouteSource>(resolveAccumulator);
    }

    private static TopologyAccumulator ResolveAccumulator(IServiceProvider services)
    {
        return services.GetRequiredService<TopologyAccumulator>();
    }
}
