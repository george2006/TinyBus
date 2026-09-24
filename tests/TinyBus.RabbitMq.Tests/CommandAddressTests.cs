using System.Globalization;

namespace TinyBus.RabbitMq.Tests;

public sealed class CommandAddressTests
{
    [Fact]
    public void Same_contract_always_maps_to_the_same_address()
    {
        var contract = new ContractIdentity("payments.capture", 1);

        var firstAddress = CommandAddress.From(contract);
        var secondAddress = CommandAddress.From(contract);

        Assert.Equal(firstAddress, secondAddress);
        Assert.Equal("tinybus.commands", firstAddress.Exchange);
        Assert.Equal("payments.capture.v1", firstAddress.RoutingKey);
    }

    [Fact]
    public void Contract_name_and_version_both_define_the_address()
    {
        var captureV1 = new ContractIdentity("payments.capture", 1);
        var captureV2 = new ContractIdentity("payments.capture", 2);
        var refundV1 = new ContractIdentity("payments.refund", 1);

        var firstVersion = CommandAddress.From(captureV1);
        var secondVersion = CommandAddress.From(captureV2);
        var otherCommand = CommandAddress.From(refundV1);

        Assert.NotEqual(firstVersion, secondVersion);
        Assert.NotEqual(firstVersion, otherCommand);
    }

    [Fact]
    public void Address_is_stable_across_cultures()
    {
        var contract = new ContractIdentity("payments.capture", 12);
        var originalCulture = CultureInfo.CurrentCulture;
        var arabicCulture = CultureInfo.GetCultureInfo("ar-SA");
        var frenchCulture = CultureInfo.GetCultureInfo("fr-FR");

        try
        {
            CultureInfo.CurrentCulture = arabicCulture;
            var arabicAddress = CommandAddress.From(contract);

            CultureInfo.CurrentCulture = frenchCulture;
            var frenchAddress = CommandAddress.From(contract);

            Assert.Equal(arabicAddress, frenchAddress);
            Assert.Equal("payments.capture.v12", arabicAddress.RoutingKey);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void Accepts_the_longest_routing_key_supported_by_RabbitMq()
    {
        var contractName = new string('a', 252);
        var contract = new ContractIdentity(contractName, 1);

        var address = CommandAddress.From(contract);
        var routingKeyBytes = System.Text.Encoding.ASCII.GetBytes(address.RoutingKey);

        Assert.Equal(255, routingKeyBytes.Length);
    }

    [Fact]
    public void Rejects_a_routing_key_longer_than_RabbitMq_supports()
    {
        var contractName = new string('a', 253);
        var contract = new ContractIdentity(contractName, 1);

        var error = Assert.Throws<ArgumentException>(() => CommandAddress.From(contract));

        Assert.Contains("255 UTF-8 bytes", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("payments.capture", 0)]
    public void Rejects_invalid_contract_identities(string? name, int version)
    {
        var contract = new ContractIdentity(name!, version);

        Assert.Throws<ArgumentException>(() => CommandAddress.From(contract));
    }
}
