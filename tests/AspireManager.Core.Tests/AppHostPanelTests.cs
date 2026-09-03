using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class AppHostPanelTests
{
    private const string _appHostPath = "/Users/dev/src/shop/src/host/Shop.AppHost/Shop.AppHost.csproj";

    [Fact]
    public void NameIsTheProjectFileName() =>
        AppHostSelection.Name(_appHostPath).Should().Be("Shop.AppHost");

    [Fact]
    public void HomeBecomesTilde() =>
        AppHostSelection.PathKeepingTail(_appHostPath, "/Users/dev", 200)
            .Should().Be("~/src/shop/src/host/Shop.AppHost/Shop.AppHost.csproj");

    [Fact]
    public void PathOutsideHomeIsLeftAlone() =>
        AppHostSelection.PathKeepingTail("/opt/app/A.csproj", "/Users/dev", 200).Should().Be("/opt/app/A.csproj");

    [Fact]
    public void NoHomeIsHandled() =>
        AppHostSelection.PathKeepingTail(_appHostPath, null, 200).Should().Be(_appHostPath);

    /// <summary>The tail identifies the AppHost; the leading directories do not.</summary>
    [Fact]
    public void OverlongPathKeepsItsTail()
    {
        string shortened = AppHostSelection.PathKeepingTail(_appHostPath, "/Users/dev", 30);

        shortened.Should().HaveLength(30);
        shortened.Should().StartWith("…").And.EndWith("Shop.AppHost.csproj");
    }

    [Fact]
    public void ShorteningNeverExceedsTheWidth() =>
        Enumerable.Range(1, 60)
            .Should().AllSatisfy(w =>
                AppHostSelection.PathKeepingTail(_appHostPath, "/Users/dev", w).Length.Should().BeLessThanOrEqualTo(w));

    [Fact]
    public void ZeroWidthIsNotAnError() =>
        AppHostSelection.PathKeepingTail(_appHostPath, "/Users/dev", 0).Should().NotBeNull();

    /// <summary>The picker prints the name beside the path, so there the leading directories are the news.</summary>
    [Fact]
    public void OverlongPickerPathKeepsItsHead()
    {
        string shortened = AppHostSelection.PathKeepingHead(_appHostPath, "/Users/dev", 30);

        shortened.Should().HaveLength(30);
        shortened.Should().StartWith("~/src/shop").And.EndWith("\u2026");
    }

    [Fact]
    public void KeepingTheHeadNeverExceedsTheWidth() =>
        Enumerable.Range(1, 60)
            .Should().AllSatisfy(w =>
                AppHostSelection.PathKeepingHead(_appHostPath, "/Users/dev", w).Length.Should().BeLessThanOrEqualTo(w));

    [Fact]
    public void DirectoryDropsTheProjectFile() =>
        AppHostSelection.Directory(_appHostPath).Should().Be("/Users/dev/src/shop/src/host/Shop.AppHost");

    [Fact]
    public void DirectoryOfABareFileNameIsThePathItself() =>
        AppHostSelection.Directory("A.csproj").Should().Be("A.csproj");
}

public class StoreClearTests
{
    [Fact]
    public void ClearingResourcesLeavesNothingBehind()
    {
        ResourceStore store = new();
        store.Upsert(new AspireResource("a-1", "a", "Project", "Running", null, null, null));

        store.Clear();

        store.Resources().Should().BeEmpty();
    }

    /// <summary>Switching AppHost must not show the previous one's logs against a same-named resource.</summary>
    [Fact]
    public void ClearingLogsLeavesNothingBehind()
    {
        LogStore logs = new();
        logs.Add(new LogLine("ticker", DateTimeOffset.UnixEpoch, "old", false));

        logs.Clear();

        logs.For("ticker").Should().BeEmpty();
    }

    [Fact]
    public void StoresStayUsableAfterClearing()
    {
        ResourceStore store = new();
        LogStore logs = new();
        store.Clear();
        logs.Clear();

        store.Upsert(new AspireResource("b-1", "b", "Project", "Running", null, null, null));
        logs.Add(new LogLine("b", DateTimeOffset.UnixEpoch, "new", false));

        store.Resources().Should().ContainSingle();
        logs.For("b").Should().ContainSingle();
    }
}

public class SamePathTests
{
    /// <summary>`aspire ps` gives absolute paths; the command line may have given a relative one.</summary>
    [Fact]
    public void RelativeAndAbsolutePathsToTheSameFileMatch()
    {
        string absolute = Path.Combine(Directory.GetCurrentDirectory(), "sub", "A.csproj");

        AppHostSelection.SamePath("sub/A.csproj", absolute).Should().BeTrue();
    }

    [Fact]
    public void IdenticalPathsMatch() =>
        AppHostSelection.SamePath("/x/A.csproj", "/x/A.csproj").Should().BeTrue();

    [Fact]
    public void RedundantSegmentsAreResolved() =>
        AppHostSelection.SamePath("/x/y/../A.csproj", "/x/A.csproj").Should().BeTrue();

    [Fact]
    public void DifferentPathsDoNotMatch() =>
        AppHostSelection.SamePath("/x/A.csproj", "/x/B.csproj").Should().BeFalse();

    [Theory]
    [InlineData(null, "/x/A.csproj")]
    [InlineData("/x/A.csproj", null)]
    [InlineData(null, null)]
    public void NullIsNeverAMatch(string? left, string? right) =>
        AppHostSelection.SamePath(left, right).Should().BeFalse();
}

public class AppHostSortingTests
{
    private static AppHost Host(string path, int pid = 1) => new(path, pid, "running", null);

    [Fact]
    public void SortedAlphabeticallyByProjectName() =>
        AppHostSelection.Sorted([Host("/x/Zebra.csproj"), Host("/y/Apple.csproj"), Host("/z/Mango.csproj")])
            .Select(static h => AppHostSelection.Name(h.AppHostPath))
            .Should().ContainInOrder("Apple", "Mango", "Zebra");

    /// <summary>Order must not depend on where the project happens to live.</summary>
    [Fact]
    public void PathDoesNotDecideTheOrder() =>
        AppHostSelection.Sorted([Host("/aaa/Zebra.csproj"), Host("/zzz/Apple.csproj")])
            .Select(static h => AppHostSelection.Name(h.AppHostPath))
            .Should().ContainInOrder("Apple", "Zebra");

    [Fact]
    public void SortingIsCaseInsensitive() =>
        AppHostSelection.Sorted([Host("/x/beta.csproj"), Host("/y/Alpha.csproj")])
            .Select(static h => AppHostSelection.Name(h.AppHostPath))
            .Should().ContainInOrder("Alpha", "beta");

    /// <summary>Two projects with the same name still need a stable order.</summary>
    [Fact]
    public void SameNameFallsBackToPath() =>
        AppHostSelection.Sorted([Host("/z/A.csproj"), Host("/a/A.csproj")])
            .Select(static h => h.AppHostPath)
            .Should().ContainInOrder("/a/A.csproj", "/z/A.csproj");

    [Fact]
    public void EmptyListSortsToEmpty() =>
        AppHostSelection.Sorted([]).Should().BeEmpty();

    [Fact]
    public void StartupPickerIsSortedToo() =>
        AppHostSelection.Select([Host("/x/Zebra.csproj"), Host("/y/Apple.csproj")], null)
            .Should().BeOfType<ChooseAppHost>()
            .Which.Candidates.Select(static h => AppHostSelection.Name(h.AppHostPath))
            .Should().ContainInOrder("Apple", "Zebra");
}
