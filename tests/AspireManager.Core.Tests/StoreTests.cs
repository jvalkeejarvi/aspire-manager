using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class ResourceStoreTests
{
    private static AspireResource Resource(string name, string displayName, string state = "Running") =>
        new(name, displayName, "Project", state, "Healthy", null, null);

    [Fact]
    public void UpsertReplacesByNameRatherThanAppending()
    {
        ResourceStore store = new();
        store.Upsert(Resource("ticker-abc", "ticker"));
        store.Upsert(Resource("ticker-abc", "ticker", "Finished"));

        store.Resources().Should().HaveCount(1);
        store.Resources()[0].State.Should().Be("Finished");
    }

    [Fact]
    public void ResourcesAreOrderedByDisplayNameSoTheListDoesNotReshuffle()
    {
        ResourceStore store = new();
        store.Upsert(Resource("webui-z", "webui"));
        store.Upsert(Resource("azurite-a", "azurite"));
        store.Upsert(Resource("recipes-r", "recipes-api"));

        store.Resources().Select(static r => r.DisplayName)
            .Should().ContainInOrder("azurite", "recipes-api", "webui");
    }

    [Fact]
    public void DistinctDisplayNamesAreNotAmbiguous()
    {
        ResourceStore store = new();
        AspireResource ticker = Resource("ticker-abc", "ticker");
        store.Upsert(ticker);
        store.Upsert(Resource("noisy-def", "noisy"));

        store.HasAmbiguousLogs(ticker).Should().BeFalse();
    }

    /// <summary>Replicas share a display name, and logs carry nothing finer to tell them apart.</summary>
    [Fact]
    public void SharedDisplayNameIsReportedAsAmbiguous()
    {
        ResourceStore store = new();
        AspireResource first = Resource("api-abc", "api");
        store.Upsert(first);
        store.Upsert(Resource("api-def", "api"));

        store.HasAmbiguousLogs(first).Should().BeTrue();
    }
}

public class LogStoreTests
{
    private static LogLine Line(string resource, string content) =>
        new(resource, DateTimeOffset.UnixEpoch, content, false);

    [Fact]
    public void UnknownResourceHasNoLines() =>
        new LogStore().For("never-seen").Should().BeEmpty();

    [Fact]
    public void KeepsLinesOldestFirst()
    {
        LogStore store = new(capacityPerResource: 10);
        store.Add(Line("ticker", "one"));
        store.Add(Line("ticker", "two"));

        store.For("ticker").Select(static l => l.Content).Should().ContainInOrder("one", "two");
    }

    [Fact]
    public void LinesAreKeptPerResource()
    {
        LogStore store = new();
        store.Add(Line("ticker", "tick"));
        store.Add(Line("noisy", "work"));

        store.For("ticker").Should().HaveCount(1);
        store.For("noisy").Should().HaveCount(1);
    }

    [Fact]
    public void EvictsOldestOnceCapacityIsReachedAndKeepsOrder()
    {
        LogStore store = new(capacityPerResource: 3);
        foreach (int i in Enumerable.Range(1, 5))
        {
            store.Add(Line("ticker", $"line{i}"));
        }

        store.For("ticker").Select(static l => l.Content)
            .Should().ContainInOrder("line3", "line4", "line5").And.HaveCount(3);
    }

    /// <summary>The stream that feeds this never stops, so the bound is the point.</summary>
    [Fact]
    public void StaysBoundedUnderSustainedIngest()
    {
        LogStore store = new(capacityPerResource: 50);
        foreach (int i in Enumerable.Range(1, 10_000))
        {
            store.Add(Line("noisy", $"line{i}"));
        }

        store.For("noisy").Should().HaveCount(50);
        store.For("noisy")[^1].Content.Should().Be("line10000");
    }
}

public class CommandPolicyTests
{
    private static AspireCommand Command(string state = "Enabled", IReadOnlyList<AspireCommandInput>? inputs = null) =>
        new("Display", null, state, 1, inputs);

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    [InlineData("rebuild")]
    public void EverydayCommandsFireOnOneKeypress(string name) =>
        CommandPolicy.Classify(name, Command()).Should().Be(CommandAvailability.Instant);

    /// <summary>Nothing in the metadata marks these destructive, so the allowlist has to catch them.</summary>
    [Theory]
    [InlineData("delete-azure-resources")]
    [InlineData("reprovision-all")]
    [InlineData("reset-provisioning-state")]
    public void DestructiveCommandsRequireConfirmation(string name) =>
        CommandPolicy.Classify(name, Command()).Should().Be(CommandAvailability.NeedsConfirmation);

    /// <summary>The allowlist has to fail safe, or a new integration's command fires unguarded.</summary>
    [Fact]
    public void UnknownFutureCommandRequiresConfirmation() =>
        CommandPolicy.Classify("nuke-everything", Command())
            .Should().Be(CommandAvailability.NeedsConfirmation);

    [Fact]
    public void DisabledCommandIsNotOffered() =>
        CommandPolicy.Classify("restart", Command(state: "Disabled"))
            .Should().Be(CommandAvailability.Unavailable);

    [Fact]
    public void CommandNeedingArgumentsIsNotOffered() =>
        CommandPolicy.Classify("set-parameter", Command(inputs: [new AspireCommandInput("Value")]))
            .Should().Be(CommandAvailability.Unavailable);

    /// <summary>A disabled command that also takes arguments must not slip through on the second check.</summary>
    [Fact]
    public void DisabledOutranksEverythingElse() =>
        CommandPolicy.Classify("stop", Command(state: "Disabled", inputs: [new AspireCommandInput("Value")]))
            .Should().Be(CommandAvailability.Unavailable);
}

public class LogDeduplicationTests
{
    private static LogLine Line(string content, int second, bool isError = false) =>
        new("ticker", DateTimeOffset.UnixEpoch.AddSeconds(second), content, isError);

    /// <summary>
    /// Regression: every reconnect restarts `aspire logs --follow`, which replays the whole history, so
    /// the pane showed four copies of it after four backoff retries.
    /// </summary>
    [Fact]
    public void ReplayedHistoryIsNotAppendedTwice()
    {
        LogStore store = new();
        LogLine[] history = [Line("one", 1), Line("two", 2), Line("three", 3)];

        foreach (LogLine line in history)
        {
            store.Add(line);
        }

        // The reconnect replays exactly the same lines.
        foreach (LogLine line in history)
        {
            store.Add(line);
        }

        store.For("ticker").Select(static l => l.Content).Should().Equal("one", "two", "three");
    }

    [Fact]
    public void NewLinesAfterAReplayStillArrive()
    {
        LogStore store = new();
        store.Add(Line("one", 1));
        store.Add(Line("one", 1));
        store.Add(Line("two", 2));

        store.For("ticker").Select(static l => l.Content).Should().Equal("one", "two");
    }

    /// <summary>The same text at a different moment is a different line, not a replay.</summary>
    [Fact]
    public void IdenticalTextAtDifferentTimesIsKept()
    {
        LogStore store = new();
        store.Add(Line("working", 1));
        store.Add(Line("working", 2));

        store.For("ticker").Should().HaveCount(2);
    }

    /// <summary>stdout and stderr can carry the same text; they are still two lines.</summary>
    [Fact]
    public void SameTextOnStdoutAndStderrIsKept()
    {
        LogStore store = new();
        store.Add(Line("boom", 1));
        store.Add(Line("boom", 1, isError: true));

        store.For("ticker").Should().HaveCount(2);
    }

    /// <summary>A line evicted from the ring is forgotten, so it can legitimately reappear later.</summary>
    [Fact]
    public void EvictedLinesAreNoLongerSuppressed()
    {
        LogStore store = new(capacityPerResource: 3);
        store.Add(Line("one", 1));
        store.Add(Line("two", 2));
        store.Add(Line("three", 3));
        store.Add(Line("four", 4));

        // "one" has been evicted, so it is new again rather than a suppressed duplicate.
        store.Add(Line("one", 1));

        store.For("ticker").Select(static l => l.Content).Should().Equal("three", "four", "one");
    }

    [Fact]
    public void DeduplicationIsPerResource()
    {
        LogStore store = new();
        store.Add(new LogLine("a", DateTimeOffset.UnixEpoch, "same", false));
        store.Add(new LogLine("b", DateTimeOffset.UnixEpoch, "same", false));

        store.For("a").Should().ContainSingle();
        store.For("b").Should().ContainSingle();
    }

    [Fact]
    public void RingStaysBoundedWithDeduplicationOn()
    {
        LogStore store = new(capacityPerResource: 50);
        foreach (int i in Enumerable.Range(1, 10_000))
        {
            store.Add(Line($"line{i}", i));
        }

        store.For("ticker").Should().HaveCount(50);
        store.For("ticker")[^1].Content.Should().Be("line10000");
    }
}
