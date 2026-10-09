using Cogworks.Umbraco.FormsGuard.Processing;

namespace Cogworks.Umbraco.FormsGuard.Tests.Processing;

public class RetryBackoffTests
{
    private const int Base = 30;
    private const int Max = 1800;

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(10, 1800)]
    [InlineData(100, 1800)]
    [InlineData(int.MaxValue, 1800)]
    public void NoRetryAfter_DoublesUpToMax(int failures, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), RetryBackoff.NextDelay(failures, null, Base, Max));
    }

    [Fact]
    public void RetryAfter_IsUsedInsteadOfSchedule()
    {
        Assert.Equal(TimeSpan.FromSeconds(90), RetryBackoff.NextDelay(1, TimeSpan.FromSeconds(90), Base, Max));
    }

    [Fact]
    public void RetryAfter_OverMax_IsCapped()
    {
        Assert.Equal(TimeSpan.FromSeconds(1800), RetryBackoff.NextDelay(1, TimeSpan.FromHours(2), Base, Max));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void RetryAfter_ZeroOrNegative_IsOneSecond(int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(1), RetryBackoff.NextDelay(1, TimeSpan.FromSeconds(seconds), Base, Max));
    }
}
