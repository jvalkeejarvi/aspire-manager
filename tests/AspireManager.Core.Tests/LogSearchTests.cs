using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class LogSearchTests
{
    private static readonly string[] Lines =
    [
        "12:00:01 starting up",
        "12:00:02 assigned tag to recipe",
        "12:00:03 nothing here",
        "12:00:04 reassigned and assigned again",
        "12:00:05 done",
    ];

    [Fact]
    public void FindsEveryOccurrenceInALine() =>
        LogSearch.Matches("assigned and assigned", "assigned")
            .Should().BeEquivalentTo([(0, 8), (13, 8)]);

    [Fact]
    public void MatchingIsCaseInsensitive() =>
        LogSearch.Matches("Assigned ASSIGNED", "assigned").Should().HaveCount(2);

    /// <summary>Overlapping occurrences of a repeating query must not be double-counted.</summary>
    [Fact]
    public void MatchesDoNotOverlap() =>
        LogSearch.Matches("aaaa", "aa").Should().BeEquivalentTo([(0, 2), (2, 2)]);

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void NoQueryMeansNoMatches(string? query) =>
        LogSearch.Matches("anything", query).Should().BeEmpty();

    [Fact]
    public void EmptyLineHasNoMatches() =>
        LogSearch.Matches("", "x").Should().BeEmpty();

    [Fact]
    public void QueryLongerThanTheLineDoesNotThrow() =>
        LogSearch.Matches("ab", "abcdef").Should().BeEmpty();

    [Fact]
    public void FindsTheLinesContainingTheQuery() =>
        LogSearch.MatchingLines(Lines, "assigned").Should().Equal(1, 3);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankQueryMatchesNoLines(string? query) =>
        LogSearch.MatchingLines(Lines, query).Should().BeEmpty();

    [Fact]
    public void UnmatchedQueryFindsNothing() =>
        LogSearch.MatchingLines(Lines, "zzz").Should().BeEmpty();

    /// <summary>n at the last match returns to the first, as in every editor.</summary>
    [Fact]
    public void AdvanceWrapsForward()
    {
        LogSearch.Advance(3, 0, 1).Should().Be(1);
        LogSearch.Advance(3, 2, 1).Should().Be(0);
    }

    [Fact]
    public void AdvanceWrapsBackward()
    {
        LogSearch.Advance(3, 2, -1).Should().Be(1);
        LogSearch.Advance(3, 0, -1).Should().Be(2);
    }

    [Fact]
    public void AdvanceWithNoMatchesStaysAtZero() =>
        LogSearch.Advance(0, 0, 1).Should().Be(0);

    [Fact]
    public void LineForPositionMapsToTheLineIndex() =>
        LogSearch.LineForPosition([1, 3], 1).Should().Be(3);

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void LineForPositionOutOfRangeIsNull(int position) =>
        LogSearch.LineForPosition([1, 3], position).Should().BeNull();

    [Fact]
    public void LineForPositionWithNoMatchesIsNull() =>
        LogSearch.LineForPosition([], 0).Should().BeNull();

    [Fact]
    public void SummaryCountsFromOneForHumans() =>
        LogSearch.Summary("assi", 4, 2).Should().Be("matches for 'assi' (3 of 4)");

    [Fact]
    public void SummarySaysSoWhenNothingMatched() =>
        LogSearch.Summary("zzz", 0, 0).Should().Be("no matches for 'zzz'");
}
