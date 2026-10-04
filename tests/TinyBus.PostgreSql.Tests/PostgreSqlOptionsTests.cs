namespace TinyBus.PostgreSql.Tests;

public sealed class PostgreSqlOptionsTests
{
    [Fact]
    public void Command_lease_duration_defaults_to_five_minutes()
    {
        var options = new PostgreSqlOptions();

        Assert.Equal(TimeSpan.FromMinutes(5), options.CommandLeaseDuration);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Command_lease_duration_must_be_positive(int milliseconds)
    {
        var options = new PostgreSqlOptions();
        var commandLeaseDuration = TimeSpan.FromMilliseconds(milliseconds);

        void SetCommandLeaseDuration()
        {
            options.CommandLeaseDuration = commandLeaseDuration;
        }

        Assert.Throws<ArgumentOutOfRangeException>(SetCommandLeaseDuration);
    }
}
