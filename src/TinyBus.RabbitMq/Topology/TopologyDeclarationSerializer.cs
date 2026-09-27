using System;
using System.Text.Json;

namespace TinyBus.RabbitMq;

internal static class TopologyDeclarationSerializer
{
    public static byte[] Serialize(TopologyDeclaration declaration)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(declaration);
        return content;
    }

    public static TopologyDeclaration Deserialize(ReadOnlySpan<byte> source)
    {
        var declaration = JsonSerializer.Deserialize<TopologyDeclaration>(source);
        if (declaration is null)
        {
            throw new InvalidOperationException("RabbitMQ returned an empty topology declaration.");
        }

        Validate(declaration);

        return declaration;
    }

    private static void Validate(TopologyDeclaration declaration)
    {
        if (declaration.FormatVersion != TopologyDeclaration.CurrentFormatVersion)
        {
            throw new InvalidOperationException(
                $"RabbitMQ topology declaration format {declaration.FormatVersion} is not supported.");
        }

        if (declaration.Id == Guid.Empty)
        {
            throw new InvalidOperationException("RabbitMQ returned a topology declaration without an identity.");
        }

        if (string.IsNullOrWhiteSpace(declaration.Service))
        {
            throw new InvalidOperationException("RabbitMQ returned a topology declaration without a service identity.");
        }

        if (declaration.Commands is null)
        {
            throw new InvalidOperationException("RabbitMQ returned a topology declaration without commands.");
        }

        foreach (var command in declaration.Commands)
        {
            if (command is null)
            {
                throw new InvalidOperationException("RabbitMQ returned an empty command declaration.");
            }

            Validate(command);
        }
    }

    private static void Validate(CommandDeclaration command)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
        {
            throw new InvalidOperationException("RabbitMQ returned a command declaration without a name.");
        }

        if (command.Version < 1)
        {
            throw new InvalidOperationException(
                $"RabbitMQ returned command '{command.Name}' with invalid version {command.Version}.");
        }
    }
}
