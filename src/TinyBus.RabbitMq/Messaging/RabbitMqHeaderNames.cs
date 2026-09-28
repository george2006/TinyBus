namespace TinyBus.RabbitMq;

internal static class RabbitMqHeaderNames
{
    internal const string CausationId = "tinybus-causation-id";
    internal const string ContractVersion = "tinybus-contract-version";
    internal const string ExceptionDetails = "tinybus-exception-details";
    internal const string ExceptionMessage = "tinybus-exception-message";
    internal const string ExceptionType = "tinybus-exception-type";
    internal const string FailedAttempt = "tinybus-failed-attempt";
    internal const string FailedQueue = "tinybus-failed-queue";
    internal const string MessageHeaders = "tinybus-headers";
}
