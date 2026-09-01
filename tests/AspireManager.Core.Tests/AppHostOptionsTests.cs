using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class AppHostOptionsTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static AppHost Running(string path, int pid = 100) => new(path, pid, "running", null);

    private static RecentAppHost Recent(string path, TimeSpan ago) => new(path, _now - ago);

    private static IReadOnlyList<AppHostOption> Build(
        IReadOnlyList<AppHost>? running = null,
        IReadOnlyList<RecentAppHost>? recents = null,
        IReadOnlyList<AppHostCandidate>? candidates = null) =>
        AppHostOptions.Build(running ?? [], recents ?? [], candidates ?? [], _now);

    [Fact]
    public void RunningComesFirstThenRecentsThenTheWorkspace()
    {
        IReadOnlyList<AppHostOption> options = Build(
            running: [Running("/w/Up.AppHost.csproj")],
            recents: [Recent("/x/Old.AppHost.csproj", TimeSpan.FromDays(2)), Recent("/y/New.AppHost.csproj", TimeSpan.FromHours(1))],
            candidates: [new AppHostCandidate("/z/Here.AppHost.csproj")]);

        options.Select(static o => o.Name).Should().Equal("Up.AppHost", "New.AppHost", "Old.AppHost", "Here.AppHost");
    }

    [Fact]
    public void OnlyRunningOptionsAreMarkedRunning()
    {
        IReadOnlyList<AppHostOption> options = Build(
            running: [Running("/w/Up.AppHost.csproj", 4242)],
            recents: [Recent("/x/Old.AppHost.csproj", TimeSpan.FromDays(1))]);

        options[0].Running.Should().BeTrue();
        options[0].Detail.Should().Be("pid 4242");
        options[1].Running.Should().BeFalse();
    }

    /// <summary>The AppHost you are attached to is also the one you used last; it must not appear twice.</summary>
    [Fact]
    public void AnAppHostReachedTwoWaysAppearsOnce()
    {
        IReadOnlyList<AppHostOption> options = Build(
            running: [Running("/w/Same.AppHost.csproj")],
            recents: [Recent("/w/Same.AppHost.csproj", TimeSpan.FromMinutes(5))],
            candidates: [new AppHostCandidate("/w/Same.AppHost.csproj")]);

        options.Should().ContainSingle().Which.Running.Should().BeTrue();
    }

    /// <summary>`aspire ps` reports absolute paths; a recents entry may have been recorded relative.</summary>
    [Fact]
    public void DeduplicationNormalisesThePath()
    {
        string absolute = Path.Combine(Directory.GetCurrentDirectory(), "App.AppHost.csproj");

        Build(running: [Running(absolute)], recents: [Recent("App.AppHost.csproj", TimeSpan.FromMinutes(1))])
            .Should().ContainSingle();
    }

    /// <summary>`aspire ps` lists AppHosts that are starting or already stopped, which cannot be attached to.</summary>
    [Fact]
    public void OnlyRunningHostsCountAsRunning() =>
        AppHostOptions.Build([new AppHost("/w/Gone.AppHost.csproj", 1, "exited", null)], [], [], _now)
            .Should().BeEmpty();

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(60 * 5, "5m ago")]
    [InlineData(60 * 60 * 2, "2h ago")]
    [InlineData(60 * 60 * 24 * 3, "3d ago")]
    [InlineData(60 * 60 * 24 * 39, "over a week ago")]
    public void AgeReadsAsATimeAgo(int seconds, string expected) =>
        AppHostOptions.Age(TimeSpan.FromSeconds(seconds)).Should().Be(expected);

    [Fact]
    public void RowsAlignTheDetailsAndMarkTheCurrentOne()
    {
        IReadOnlyList<AppHostOption> options = Build(
            running: [Running("/w/Short.AppHost.csproj", 7)],
            recents: [Recent("/x/MuchLongerName.AppHost.csproj", TimeSpan.FromHours(3))]);

        AppHostOptions.Rows(options, "/w/Short.AppHost.csproj").Should().Equal(
            "* Short.AppHost            pid 7",
            "  MuchLongerName.AppHost   3h ago");
    }
}

public class RecentsTests
{
    private static readonly DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void RecordingPutsItFirst()
    {
        IReadOnlyList<RecentAppHost> after = Recents.Record(
            [new RecentAppHost("/a.csproj", _now.AddDays(-1))],
            "/b.csproj",
            _now);

        after.Select(static r => r.Path).Should().Equal("/b.csproj", "/a.csproj");
    }

    [Fact]
    public void RecordingTheSameOneAgainMovesItRatherThanDuplicating()
    {
        IReadOnlyList<RecentAppHost> after = Recents.Record(
            [new RecentAppHost("/a.csproj", _now.AddDays(-2)), new RecentAppHost("/b.csproj", _now.AddDays(-1))],
            "/a.csproj",
            _now);

        after.Select(static r => r.Path).Should().Equal("/a.csproj", "/b.csproj");
        after[0].LastUsedAt.Should().Be(_now);
    }

    [Fact]
    public void TheListIsCapped()
    {
        IReadOnlyList<RecentAppHost> entries =
            [.. Enumerable.Range(0, Recents.Capacity).Select(i => new RecentAppHost($"/{i}.csproj", _now.AddDays(-i)))];

        Recents.Record(entries, "/new.csproj", _now).Should().HaveCount(Recents.Capacity);
    }

    /// <summary>A project that has been deleted or renamed is not worth offering to start.</summary>
    [Fact]
    public void LoadDropsPathsThatNoLongerExist()
    {
        string file = Path.Combine(Path.GetTempPath(), $"recents-{Guid.NewGuid():N}.json");
        Recents.Save(file, [new RecentAppHost("/gone.csproj", _now), new RecentAppHost("/here.csproj", _now.AddDays(-1))]);

        try
        {
            Recents.Load(file, path => path == "/here.csproj")
                .Should().ContainSingle().Which.Path.Should().Be("/here.csproj");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void SavedEntriesComeBackNewestFirst()
    {
        string file = Path.Combine(Path.GetTempPath(), $"recents-{Guid.NewGuid():N}.json");
        Recents.Save(file, [new RecentAppHost("/old.csproj", _now.AddDays(-3)), new RecentAppHost("/new.csproj", _now)]);

        try
        {
            Recents.Load(file, static _ => true).Select(static r => r.Path).Should().Equal("/new.csproj", "/old.csproj");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AMissingFileIsAnEmptyList() =>
        Recents.Load(Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.json")).Should().BeEmpty();

    /// <summary>State we can rebuild: a corrupt file must not stop the tool from starting.</summary>
    [Fact]
    public void AnUnreadableFileIsAnEmptyList()
    {
        string file = Path.Combine(Path.GetTempPath(), $"recents-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{ not json");

        try
        {
            Recents.Load(file).Should().BeEmpty();
        }
        finally
        {
            File.Delete(file);
        }
    }
}
