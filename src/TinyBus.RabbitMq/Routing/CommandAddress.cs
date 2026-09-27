using System;
using System.Globalization;
using System.Text;
using TinyBus;

namespace TinyBus.RabbitMq;

internal readonly record struct CommandAddress(
    string Exchange,
    string RoutingKey)
{
    internal const string ExchangeName = "tinybus.commands";
    private const int MaximumRoutingKeyLength = 255;

    public static CommandAddress From(ContractIdentity contract)
    {
        Validate(contract);

        var version = contract.Version.ToString(CultureInfo.InvariantCulture);
        var routingKey = contract.Name + ".v" + version;
        ValidateRoutingKey(routingKey);

        var address = new CommandAddress(ExchangeName, routingKey);

        return address;
    }

    private static void Validate(ContractIdentity contract)
    {
        if (string.IsNullOrWhiteSpace(contract.Name))
        {
            throw new ArgumentException("A command contract requires a name.", nameof(contract));
        }

        if (contract.Version < 1)
        {
            throw new ArgumentException("A command contract version must be at least one.", nameof(contract));
        }
    }

    private static void ValidateRoutingKey(string routingKey)
    {
        var length = Encoding.UTF8.GetByteCount(routingKey);
        if (length > MaximumRoutingKeyLength)
        {
            throw new ArgumentException(
                "A RabbitMQ command address cannot exceed 255 UTF-8 bytes.",
                nameof(routingKey));
        }
    }
}
