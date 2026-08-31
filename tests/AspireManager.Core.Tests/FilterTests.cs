using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class FilterTests
{
    private static AspireResource Resource(string displayName, string type) =>
        new($"{displayName}-abc", displayName, type, "Running", "Healthy", null, null);

    private static IReadOnlyList<AspireResource> Sample() =>
    [
        Resource("recipes-api", "Project"),
        Resource("recipes-api-dapr-cli", "Executable"),
        Resource("recipesdb", "SqlServerDatabaseResource"),
        Resource("webui", "Project"),
        Resource("sqlPass", "Parameter"),
    ];

    private static IEnumerable<string> Names(IReadOnlyList<ResourceRow> rows) =>
        rows.OfType<ResourceItem>().Select(static i => i.Resource.DisplayName);

    [Fact]
    public void NoFilterShowsEverything() =>
        Names(ShellModel.Rows(Sample())).Should().HaveCount(5);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankFilterShowsEverything(string? filter) =>
        Names(ShellModel.Rows(Sample(), null, filter)).Should().HaveCount(5);

    [Fact]
    public void FilterMatchesAnywhereInTheName() =>
        Names(ShellModel.Rows(Sample(), null, "recipes"))
            .Should().BeEquivalentTo(["recipes-api", "recipes-api-dapr-cli", "recipesdb"]);

    [Fact]
    public void FilterIsCaseInsensitive() =>
        Names(ShellModel.Rows(Sample(), null, "WEBUI")).Should().ContainSingle().Which.Should().Be("webui");

    [Fact]
    public void SurroundingSpaceIsIgnored() =>
        Names(ShellModel.Rows(Sample(), null, "  webui  ")).Should().ContainSingle();

    /// <summary>
    /// The whole point of "names, not groups": "sql" must find sqlPass, not drag in every member of
    /// SqlServerDatabaseResource.
    /// </summary>
    [Fact]
    public void FilterDoesNotMatchTypeNames()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample(), null, "sql");

        Names(rows).Should().ContainSingle().Which.Should().Be("sqlPass");
        rows.OfType<TypeHeader>().Select(static h => h.ResourceType)
            .Should().ContainSingle().Which.Should().Be("Parameter");
    }

    /// <summary>Groups with no surviving members disappear, headings included.</summary>
    [Fact]
    public void EmptyGroupsAreDropped()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample(), null, "webui");

        rows.OfType<TypeHeader>().Should().ContainSingle().Which.ResourceType.Should().Be("Project");
        rows.Should().HaveCount(2);
    }

    [Fact]
    public void HeaderCountReflectsWhatSurvivedTheFilter() =>
        ShellModel.Rows(Sample(), null, "recipes").OfType<TypeHeader>()
            .Should().AllSatisfy(static h => h.Count.Should().Be(1));

    [Fact]
    public void NoMatchesMeansNoRows() =>
        ShellModel.Rows(Sample(), null, "nothing-matches-this").Should().BeEmpty();

    /// <summary>A folded group would otherwise hide the very matches the search found.</summary>
    [Fact]
    public void FilteringOverridesFolding()
    {
        HashSet<string> folded = ["Project", "Executable", "SqlServerDatabaseResource"];

        Names(ShellModel.Rows(Sample(), folded, "recipes")).Should().HaveCount(3);

        // Same folds, no filter: those three are hidden (sqlPass survives, its type is not folded).
        Names(ShellModel.Rows(Sample(), folded))
            .Should().NotContain("recipes-api").And.NotContain("recipesdb").And.Contain("sqlPass");
    }

    [Fact]
    public void FoldingStillAppliesOnceTheFilterIsCleared() =>
        Names(ShellModel.Rows(Sample(), new HashSet<string> { "Project" }, ""))
            .Should().NotContain("webui").And.Contain("sqlPass");
}

public class GroupingToggleTests
{
    private static AspireResource Resource(string displayName, string type) =>
        new($"{displayName}-abc", displayName, type, "Running", "Healthy", null, null);

    private static IReadOnlyList<AspireResource> Sample() =>
    [
        Resource("webui", "Project"),
        Resource("azurite", "AzureStorageResource"),
        Resource("recipes-api", "Project"),
        Resource("ticker", "Executable"),
    ];

    [Fact]
    public void UngroupedHasNoHeadings() =>
        ShellModel.Rows(Sample(), grouped: false).Should().AllBeOfType<ResourceItem>();

    /// <summary>Flat means one alphabetical run, not each type's members kept together.</summary>
    [Fact]
    public void UngroupedIsOneAlphabeticalList() =>
        ShellModel.Rows(Sample(), grouped: false)
            .OfType<ResourceItem>().Select(static i => i.Resource.DisplayName)
            .Should().ContainInOrder("azurite", "recipes-api", "ticker", "webui");

    [Fact]
    public void UngroupedKeepsEveryResource() =>
        ShellModel.Rows(Sample(), grouped: false).Should().HaveCount(4);

    [Fact]
    public void UngroupedStillHonoursTheFilter() =>
        ShellModel.Rows(Sample(), null, "api", grouped: false)
            .OfType<ResourceItem>().Select(static i => i.Resource.DisplayName)
            .Should().ContainSingle().Which.Should().Be("recipes-api");

    /// <summary>There is nothing to fold without headings, so folds must not hide anything.</summary>
    [Fact]
    public void FoldsAreIgnoredWhenUngrouped() =>
        ShellModel.Rows(Sample(), new HashSet<string> { "Project", "Executable" }, null, grouped: false)
            .Should().HaveCount(4);

    [Fact]
    public void GroupingIsTheDefault() =>
        ShellModel.Rows(Sample()).OfType<TypeHeader>().Should().NotBeEmpty();

    [Fact]
    public void UngroupedEmptyStaysEmpty() =>
        ShellModel.Rows([], grouped: false).Should().BeEmpty();
}

public class IndentTests
{
    private static AspireResource Resource(string displayName, string type) =>
        new($"{displayName}-abc", displayName, type, "Running", "Healthy", null, null);

    [Fact]
    public void GroupedRowsAreIndentedUnderTheirHeading() =>
        ShellModel.RowText(ShellModel.Rows([Resource("webui", "Project")])[1])
            .Should().Be("    R H webui");

    /// <summary>No heading to sit under means the indent is only lost width.</summary>
    [Fact]
    public void UngroupedRowsAreNotIndented() =>
        ShellModel.RowText(ShellModel.Rows([Resource("webui", "Project")], grouped: false)[0])
            .Should().Be(" R H webui");
}

public class NextIndexTests
{
    /// <summary>
    /// Regression: pressing j on an empty list threw ArgumentException from ListView.SelectedItem.
    /// An empty list happens whenever a filter matches nothing or a resource has no logs yet.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(5)]
    public void EmptyListHasNowhereToMove(int? current) =>
        ShellModel.NextIndex(0, current, 1).Should().BeNull();

    [Fact]
    public void NegativeCountIsAlsoEmpty() =>
        ShellModel.NextIndex(-1, 0, 1).Should().BeNull();

    /// <summary>Nothing selected yet: the first press selects the first row, not the second.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void FirstMoveSelectsTheFirstRow(int delta) =>
        ShellModel.NextIndex(5, null, delta).Should().Be(0);

    [Fact]
    public void MovesOneRow()
    {
        ShellModel.NextIndex(5, 2, 1).Should().Be(3);
        ShellModel.NextIndex(5, 2, -1).Should().Be(1);
    }

    /// <summary>Holding j at the bottom must not wrap round to the top.</summary>
    [Fact]
    public void ClampsAtBothEnds()
    {
        ShellModel.NextIndex(5, 4, 1).Should().Be(4);
        ShellModel.NextIndex(5, 0, -1).Should().Be(0);
    }

    /// <summary>A rebuild can leave an index pointing past the end of a now-shorter list.</summary>
    [Fact]
    public void StaleIndexBeyondTheEndIsBroughtBack()
    {
        ShellModel.NextIndex(3, 99, 1).Should().Be(2);
        ShellModel.NextIndex(3, 99, -1).Should().Be(1);
    }

    [Fact]
    public void SingleRowListStaysPut()
    {
        ShellModel.NextIndex(1, 0, 1).Should().Be(0);
        ShellModel.NextIndex(1, 0, -1).Should().Be(0);
    }
}
