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

    public static readonly DiagnosticDescriptor DuplicateCommandHandler = new(
        "TBUS003",
        "Command has multiple handlers",
        "Command '{0}' may have only one handler in a service{1}",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateRequestHandler = new(
        "TBUS004",
        "Request has multiple handlers",
        "Request '{0}' may have only one handler in a service{1}",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConflictingMessageSemantics = new(
        "TBUS005",
        "Message has conflicting semantics",
        "Message '{0}' cannot be handled as more than one of command, event, or request in a service{1}",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AmbiguousContractIdentity = new(
        "TBUS006",
        "Contract identity is ambiguous",
        "Contract {0} identifies multiple message types{1}",
        "TinyBus",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
