using NSFinance.Api.Persistence;

namespace NSFinance.Api.Tests.Unit;

public sealed class SlowDbCommandInterceptorTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(120, false)]
    [InlineData(499, false)]
    [InlineData(500, true)]
    [InlineData(4000, true)]
    public void IsSlow_FlagsOnlyCommandsAtOrAboveThreshold(int elapsedMilliseconds, bool expected)
    {
        Assert.Equal(
            expected,
            SlowDbCommandInterceptor.IsSlow(TimeSpan.FromMilliseconds(elapsedMilliseconds)));
    }
}
