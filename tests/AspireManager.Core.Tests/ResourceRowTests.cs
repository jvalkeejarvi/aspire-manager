using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class ResourceRowTests
{
    private static AspireResource Resource(string displayName, string type, string state = "Running") =>
        new($"{displayName}-abc", displayName, type, state, "Healthy", null, null);

    private static IReadOnlyList<AspireResource> Sample() =>
    [
        Resource("webui", "Project"),
        Resource("azurite", "AzureStorageResource"),
        Resource("recipes-api", "Project"),
        Resource("ticker", "Executable"),
    ];

    [Fact]
    public void GroupsByTypeWithTypesAlphabetical() =>
        ShellModel.Rows(Sample()).Select(ShellModel.RowText).Should().ContainInOrder(
            "▾ AzureStorageResource (1)",
            "    R H azurite",
            "▾ Executable (1)",
            "    R H ticker",
            "▾ Project (2)",
            "    R H recipes-api",
            "    R H webui");

    [Fact]
    public void HeaderCarriesItsMemberCount() =>
        ShellModel.Rows(Sample()).OfType<TypeHeader>().Select(static h => h.Count)
            .Should().ContainInOrder(1, 1, 2);

    [Fact]
    public void NoResourcesMeansNoRows() =>
        ShellModel.Rows([]).Should().BeEmpty();

    /// <summary>A folded group keeps its heading and the count, so you can see what is hidden.</summary>
    [Fact]
    public void CollapsedTypeHidesItsMembersButKeepsTheHeading() =>
        ShellModel.Rows(Sample(), new HashSet<string> { "Project" })
            .Select(ShellModel.RowText).Should().ContainInOrder(
                "▾ AzureStorageResource (1)",
                "    R H azurite",
                "▾ Executable (1)",
                "    R H ticker",
                "▸ Project (2)")
            .And.HaveCount(5);

    [Fact]
    public void CollapsedHeaderIsMarkedCollapsed()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample(), new HashSet<string> { "Executable" });

        rows.OfType<TypeHeader>().Should().SatisfyRespectively(
            azure => azure.Collapsed.Should().BeFalse(),
            executable => executable.Collapsed.Should().BeTrue(),
            project => project.Collapsed.Should().BeFalse());
    }

    [Fact]
    public void EveryTypeCollapsedLeavesOnlyHeadings() =>
        ShellModel.Rows(Sample(), new HashSet<string> { "Project", "Executable", "AzureStorageResource" })
            .Should().HaveCount(3).And.AllBeOfType<TypeHeader>();

    [Fact]
    public void CollapsingAnUnknownTypeChangesNothing() =>
        ShellModel.Rows(Sample(), new HashSet<string> { "NoSuchType" })
            .Should().HaveCount(ShellModel.Rows(Sample()).Count);

    [Fact]
    public void HeadingsAndResourcesHaveDistinctStableKeys()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample());

        ShellModel.RowKey(rows[0]).Should().Be("type:AzureStorageResource");
        ShellModel.RowKey(rows[1]).Should().Be("res:azurite-abc");
        ShellModel.RowKey(rows[0]).Should().Be(ShellModel.TypeKey("AzureStorageResource"));
    }

    [Fact]
    public void IndexOfKeyFindsHeadingsAndResources()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample());

        ShellModel.IndexOfKey(rows, "res:ticker-abc").Should().Be(3);
        ShellModel.IndexOfKey(rows, ShellModel.TypeKey("Project")).Should().Be(4);
    }

    [Fact]
    public void IndexOfKeyReturnsMinusOneForMissingOrNull()
    {
        IReadOnlyList<ResourceRow> rows = ShellModel.Rows(Sample());

        ShellModel.IndexOfKey(rows, "res:gone-xyz").Should().Be(-1);
        ShellModel.IndexOfKey(rows, null).Should().Be(-1);
    }

    /// <summary>
    /// Folding a group above the selection shifts it, which is why the window restores by key. A hidden
    /// resource's key disappears entirely, so the caller falls back to its heading.
    /// </summary>
    [Fact]
    public void FoldingAGroupHidesItsResourceKeysAndShiftsLaterRows()
    {
        IReadOnlyList<ResourceRow> folded = ShellModel.Rows(Sample(), new HashSet<string> { "AzureStorageResource" });

        ShellModel.IndexOfKey(folded, "res:azurite-abc").Should().Be(-1);
        ShellModel.IndexOfKey(folded, "res:ticker-abc").Should().Be(2);
        ShellModel.IndexOfKey(folded, ShellModel.TypeKey("AzureStorageResource")).Should().Be(0);
    }

    [Fact]
    public void FirstSelectableIsTheFirstResourceNotTheHeading() =>
        ShellModel.FirstSelectable(ShellModel.Rows(Sample())).Should().Be(1);

    /// <summary>With everything folded there is no resource to land on, so the first heading will do.</summary>
    [Fact]
    public void FirstSelectableFallsBackToAHeadingWhenAllAreFolded() =>
        ShellModel.FirstSelectable(
                ShellModel.Rows(Sample(), new HashSet<string> { "Project", "Executable", "AzureStorageResource" }))
            .Should().Be(0);

    [Fact]
    public void FirstSelectableIsMinusOneWhenThereAreNoRows() =>
        ShellModel.FirstSelectable([]).Should().Be(-1);
}
