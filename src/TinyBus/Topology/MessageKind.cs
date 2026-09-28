namespace TinyBus;

/// <summary>
/// Identifies the delivery semantics declared by a message handler.
/// </summary>
public enum MessageKind
{
    Command,
    Event,
    Request
}
