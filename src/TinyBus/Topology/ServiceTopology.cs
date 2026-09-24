using System.Collections.Generic;

namespace TinyBus;

public sealed record ServiceTopology(
    ServiceIdentity Service,
    IReadOnlyList<MessageDescriptor> Messages);
