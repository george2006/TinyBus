using System.Collections.Generic;
using System.ComponentModel;

namespace TinyBus;

[EditorBrowsable(EditorBrowsableState.Never)]
public interface IBusManifest
{
    IReadOnlyList<MessageDescriptor> Messages { get; }
}
