using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class HelpTextTests
{
    private static IEnumerable<string> Keys(HelpContext context, bool filter = false, bool search = false) =>
        HelpText.Bindings(context, filter, search).Select(static b => b.Key).Where(static k => k.Length > 0);

    /// <summary>These work from anywhere, so they must appear whichever pane asked.</summary>
    [Theory]
    [InlineData(HelpContext.AppHost)]
    [InlineData(HelpContext.Resources)]
    [InlineData(HelpContext.Logs)]
    public void GlobalKeysAreListedEverywhere(HelpContext context) =>
        Keys(context).Should().Contain(["1 / 2 / 0", "tab", "^r", "?", "q"]);

    [Fact]
    public void ResourcePaneListsItsOwnCommands() =>
        Keys(HelpContext.Resources).Should().Contain(["r / s / b", "c", "o / O", "g", "- / =", "/"]);

    [Fact]
    public void AppHostPaneOffersTheDashboard()
    {
        Keys(HelpContext.AppHost).Should().Contain("o");
        Keys(HelpContext.AppHost).Should().NotContain("r / s / b", "there is no resource here");
    }

    [Fact]
    public void LogPaneListsSearchNotResourceCommands()
    {
        Keys(HelpContext.Logs).Should().Contain("/");
        Keys(HelpContext.Logs).Should().NotContain("c");
    }

    /// <summary>n/N only mean something once a search is running.</summary>
    [Fact]
    public void MatchNavigationAppearsOnlyWhileSearching()
    {
        Keys(HelpContext.Logs, search: false).Should().NotContain("n / N");
        Keys(HelpContext.Logs, search: true).Should().Contain("n / N");
    }

    /// <summary>Esc does different things depending on whether a search is up; say which.</summary>
    [Fact]
    public void EscapeIsDescribedAccordingToContext()
    {
        HelpText.Bindings(HelpContext.Logs, false, false)
            .Should().Contain(("esc / ^g", "back to resources"));

        HelpText.Bindings(HelpContext.Logs, false, true)
            .Should().Contain(("esc / ^g", "clear the search"));
    }

    [Fact]
    public void ClearingTheFilterIsOfferedOnlyWhenOneIsSet()
    {
        Keys(HelpContext.Resources, filter: false).Should().NotContain("esc / ^g");
        Keys(HelpContext.Resources, filter: true).Should().Contain("esc / ^g");
    }

    [Fact]
    public void RowsAlignTheDescriptions()
    {
        IReadOnlyList<(string Key, string Action)> bindings =
            [.. HelpText.Bindings(HelpContext.Resources, false, false).Where(static b => b.Key.Length > 0)];
        IReadOnlyList<string> rows =
            [.. HelpText.Rows(HelpContext.Resources, false, false).Where(static r => r.Length > 0)];

        // Where each description begins: the row length less the description itself.
        IEnumerable<int> columns = rows.Zip(bindings, static (row, binding) => row.Length - binding.Action.Length);

        columns.Distinct().Should().ContainSingle("every description starts in the same column");
        rows.Should().AllSatisfy(static r => r.Should().NotStartWith(" "));
    }

    [Fact]
    public void BlankSeparatorSurvivesFormatting() =>
        HelpText.Rows(HelpContext.AppHost, false, false).Should().Contain("");

    [Theory]
    [InlineData(HelpContext.AppHost, "Keys: AppHost")]
    [InlineData(HelpContext.Resources, "Keys: resources")]
    [InlineData(HelpContext.Logs, "Keys: logs")]
    public void TitleNamesTheContext(HelpContext context, string expected) =>
        HelpText.Title(context).Should().Be(expected);
}
