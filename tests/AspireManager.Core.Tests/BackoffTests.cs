using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class BackoffTests
{
    private static Backoff Small() => new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));

    [Fact]
    public void FirstRetryWaitsTheInitialDelay() =>
        Small().Next().Should().Be(TimeSpan.FromSeconds(1));

    [Fact]
    public void DelayDoubles()
    {
        Backoff backoff = Small();

        TimeSpan[] delays = [backoff.Next(), backoff.Next(), backoff.Next(), backoff.Next()];

        delays.Should().Equal(
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4),
            TimeSpan.FromSeconds(8));
    }

    /// <summary>An AppHost left stopped overnight must not schedule a retry days out.</summary>
    [Fact]
    public void DelayIsCapped()
    {
        Backoff backoff = Small();
        foreach (int _ in Enumerable.Range(0, 50))
        {
            backoff.Next();
        }

        backoff.Next().Should().Be(TimeSpan.FromSeconds(8));
    }

    /// <summary>Without this, a reconnect after a long outage leaves the next drop retrying at the cap.</summary>
    [Fact]
    public void ResetReturnsToTheInitialDelay()
    {
        Backoff backoff = Small();
        backoff.Next();
        backoff.Next();
        backoff.Next();

        backoff.Reset();

        backoff.Next().Should().Be(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void DefaultStartsAtOneSecond() =>
        Backoff.Default().Next().Should().Be(TimeSpan.FromSeconds(1));
}
