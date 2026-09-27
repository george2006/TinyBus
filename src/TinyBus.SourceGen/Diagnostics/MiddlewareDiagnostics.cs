using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Diagnostics;

internal static class MiddlewareDiagnostics
{
    public static readonly DiagnosticDescriptor InvalidMiddleware = new(
        "TBUS007",
        "Incoming middleware declaration is invalid",
        "Incoming middleware '{0}' must be a concrete class implementing IIncomingMessageMiddleware",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateOrder = new(
        "TBUS008",
        "Incoming middleware order is duplicated",
        "Incoming middleware order {0} is used by multiple middleware types: {1}",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
