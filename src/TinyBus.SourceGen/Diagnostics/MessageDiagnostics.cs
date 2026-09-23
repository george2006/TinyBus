using Microsoft.CodeAnalysis;

namespace TinyBus.SourceGen.Diagnostics;

internal static class MessageDiagnostics
{
    public static readonly DiagnosticDescriptor InvalidContractName = new(
        "TBUS001",
        "Contract name is invalid",
        "Contract name for '{0}' must not be null, empty, or whitespace",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidContractVersion = new(
        "TBUS002",
        "Contract version is invalid",
        "Contract version for '{0}' must be at least 1",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
