using Skua.Core.Utils;
using Xunit;

namespace Skua.Core.Tests;

public sealed class TimeLimiterTests
{
    [Fact]
    public void RunsImmediatelyThenRespectsDelay()
    {
        TimeLimiter limiter = new();
        int runs = 0;

        Assert.True(limiter.LimitedRun("a", 60_000, () => runs++));
        Assert.False(limiter.LimitedRun("a", 60_000, () => runs++));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void SeparateNamesDoNotBlockEachOther()
    {
        TimeLimiter limiter = new();
        int runs = 0;

        Assert.True(limiter.LimitedRun("one", 60_000, () => runs++));
        Assert.True(limiter.LimitedRun("two", 60_000, () => runs++));
        Assert.Equal(2, runs);
    }
}
