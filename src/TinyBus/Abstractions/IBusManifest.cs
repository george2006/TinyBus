using System.Collections.Generic;

namespace TinyBus;

public interface IBusManifest
{
    IReadOnlyList<MessageDescriptor> Messages { get; }
}
