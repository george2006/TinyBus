using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TinyBus;

namespace TinyBus.RabbitMq;

internal sealed record TopologyDeclaration(
    [property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("service")] string Service,
    [property: JsonPropertyName("commands")] CommandDeclaration[] Commands)
{
    public const int CurrentFormatVersion = 1;

    public static TopologyDeclaration Create(ServiceTopology topology)
    {
        var commands = ReadCommands(topology.Messages);
        var declarationId = Guid.NewGuid();
        var declaration = new TopologyDeclaration(
            CurrentFormatVersion,
            declarationId,
            topology.Service.Value,
            commands);

        return declaration;
    }

    private static CommandDeclaration[] ReadCommands(IReadOnlyList<MessageDescriptor> messages)
    {
        return messages
            .Where(message => message.Kind == MessageKind.Command)
            .Select(message => message.Contract)
            .Distinct()
            .OrderBy(contract => contract.Name, StringComparer.Ordinal)
            .ThenBy(contract => contract.Version)
            .Select(CommandDeclaration.Create)
            .ToArray();
    }
}
