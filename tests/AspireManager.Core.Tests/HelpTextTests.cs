using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class HelpTextTests
{
    private static readonly (string, string)[] _sample =
    [
        ("j / k", "move up and down"),
        ("q", "quit"),
        ("", ""),
        ("^r", "switch AppHost"),
    ];

    [Fact]
    public void DescriptionsAllStartInTheSameColumn()
    {
        IReadOnlyList<string> rows = HelpText.Align(_sample);
        IEnumerable<int> columns = rows
            .Where(static r => r.Length > 0)
            .Zip(_sample.Where(static b => b.Item1.Length > 0), static (row, b) => row.Length - b.Item2.Length);

        columns.Distinct().Should().ContainSingle();
    }

    [Fact]
    public void KeyComesFirstAndIsNotIndented() =>
        HelpText.Align(_sample)[0].Should().StartWith("j / k").And.EndWith("move up and down");

    /// <summary>An empty pair is a separator, not a row of padding.</summary>
    [Fact]
    public void EmptyPairRendersAsABlankLine() =>
        HelpText.Align(_sample)[2].Should().BeEmpty();

    [Fact]
    public void ColumnIsWideEnoughForTheLongestKey() =>
        HelpText.Align(_sample).Where(static r => r.Length > 0)
            .Should().AllSatisfy(static r => r.Should().Contain("   "));

    [Fact]
    public void NothingToAlignIsEmpty() =>
        HelpText.Align([]).Should().BeEmpty();
}
