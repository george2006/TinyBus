namespace TinyBus;

internal sealed class TinyBusRuntimeSettings
{
    internal TinyBusRuntimeSettings(int maximumConcurrentMessages)
    {
        MaximumConcurrentMessages = maximumConcurrentMessages;
    }

    internal int MaximumConcurrentMessages { get; }
}
