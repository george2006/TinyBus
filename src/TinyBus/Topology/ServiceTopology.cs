using System.Collections.Generic;

namespace TinyBus;

/// <summary>
/// Describes the logical service and the message handlers contributed by its composed manifest.
/// Transport providers reconcile this topology during startup.
/// </summary>
public sealed record ServiceTopology(
    ServiceIdentity Service,
    IReadOnlyList<MessageDescriptor> Messages);
