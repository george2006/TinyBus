using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace TinyBus.Tests;

// Exercises the extension point used by independently packaged transport providers.
internal static class TestTransportOptionsExtensions
{
    public static void UseTestTransport(this TinyBusOptions options)
    {
        var services = options.Services;
        services.TryAddSingleton<NativeTestTransport>();

        Func<IServiceProvider, ITransport> resolveTransport = ResolveNativeTransport;
        services.AddSingleton(resolveTransport);
    }

    private static ITransport ResolveNativeTransport(IServiceProvider services)
    {
        return services.GetRequiredService<NativeTestTransport>();
    }
}
