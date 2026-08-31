using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class ShellModelTests
{
    private static AspireCommand Command(
        string state = "Enabled",
        int sortOrder = 0,
        IReadOnlyList<AspireCommandInput>? inputs = null) =>
        new("Display", null, state, sortOrder, inputs);

    private static AspireResource Resource(
        string displayName = "recipes-api",
        string state = "Running",
        string? health = "Healthy",
        Dictionary<string, AspireCommand>? commands = null) =>
        new($"{displayName}-abc", displayName, "Project", state, health, null, commands);

    [Theory]
    [InlineData('r', "restart")]
    [InlineData('s', "stop")]
    [InlineData('b', "rebuild")]
    [InlineData('R', "restart")]
    public void KeysMapToCommands(char key, string expected) =>
        ShellModel.CommandForKey(key).Should().Be(expected);

    [Theory]
    [InlineData('q')]
    [InlineData('x')]
    [InlineData('\t')]
    public void OtherKeysMapToNothing(char key) =>
        ShellModel.CommandForKey(key).Should().BeNull();

    [Fact]
    public void RowShowsStateThenHealthThenName() =>
        ShellModel.Row(Resource()).Should().Be("R H recipes-api");

    [Theory]
    [InlineData("Unhealthy", "R U recipes-api")]
    [InlineData("Degraded", "R D recipes-api")]
    public void RowCarriesTheHealthInitial(string health, string expected) =>
        ShellModel.Row(Resource(health: health)).Should().Be(expected);

    /// <summary>No health check is "not measured", which is not the same as unhealthy.</summary>
    [Fact]
    public void RowShowsADashWhenThereIsNoHealthCheck() =>
        ShellModel.Row(Resource(health: null)).Should().Be("R - recipes-api");

    /// <summary>Databases arrive with an empty state string on some AppHost versions.</summary>
    [Fact]
    public void RowSurvivesEmptyState() =>
        ShellModel.Row(Resource(state: "")).Should().Be("? H recipes-api");

    [Fact]
    public void InstantCommandRunsDirectly()
    {
        CommandDecision decision = ShellModel.Decide(
            Resource(commands: new() { ["restart"] = Command() }),
            "restart");

        decision.Should().BeOfType<RunCommand>()
            .Which.Should().BeEquivalentTo(new RunCommand("recipes-api", "restart"));
    }

    /// <summary>The guard is typing the resource name back, so that name is what must be echoed.</summary>
    [Fact]
    public void DestructiveCommandDemandsTypedConfirmation()
    {
        CommandDecision decision = ShellModel.Decide(
            Resource("azure-environment", commands: new() { ["delete-azure-resources"] = Command() }),
            "delete-azure-resources");

        ConfirmCommand confirm = decision.Should().BeOfType<ConfirmCommand>().Subject;
        confirm.Command.Should().Be("delete-azure-resources");
        confirm.Expected.Should().Be("azure-environment");
    }

    [Fact]
    public void MissingCommandIsRefused() =>
        ShellModel.Decide(Resource(commands: []), "rebuild")
            .Should().BeOfType<RefuseCommand>()
            .Which.Reason.Should().Contain("has no rebuild");

    [Fact]
    public void CommandNeedingArgumentsIsRefused() =>
        ShellModel.Decide(
                Resource("sqlPass", commands: new() { ["set-parameter"] = Command(inputs: [new AspireCommandInput("Value")]) }),
                "set-parameter")
            .Should().BeOfType<RefuseCommand>();

    [Fact]
    public void DisabledCommandIsRefused() =>
        ShellModel.Decide(
                Resource(commands: new() { ["restart"] = Command(state: "Disabled") }),
                "restart")
            .Should().BeOfType<RefuseCommand>();

    /// <summary>The AppHost's own sortOrder is what the dashboard shows, so match it.</summary>
    [Fact]
    public void AvailableCommandsFollowSortOrder()
    {
        AspireResource resource = Resource(commands: new()
        {
            ["rebuild"] = Command(sortOrder: 3),
            ["stop"] = Command(sortOrder: 1),
            ["restart"] = Command(sortOrder: 2),
        });

        ShellModel.AvailableCommands(resource).Should().ContainInOrder("stop", "restart", "rebuild");
    }

    [Fact]
    public void AvailableCommandsHidesUnusableOnes()
    {
        AspireResource resource = Resource(commands: new()
        {
            ["restart"] = Command(),
            ["set-parameter"] = Command(inputs: [new AspireCommandInput("Value")]),
            ["disabled-thing"] = Command(state: "Disabled"),
        });

        ShellModel.AvailableCommands(resource).Should().ContainSingle().Which.Should().Be("restart");
    }

    [Fact]
    public void ResourceWithNoCommandsHasNone() =>
        ShellModel.AvailableCommands(Resource(commands: null)).Should().BeEmpty();

    [Fact]
    public void ConfirmationAcceptsTheExactNameIgnoringSurroundingSpace()
    {
        ShellModel.ConfirmationMatches("azure-environment", "azure-environment").Should().BeTrue();
        ShellModel.ConfirmationMatches("azure-environment", "  azure-environment  ").Should().BeTrue();
    }

    /// <summary>A guard that can be satisfied by accident is not a guard.</summary>
    [Theory]
    [InlineData("Azure-Environment")]
    [InlineData("azure")]
    [InlineData("azure-environment-x")]
    [InlineData("")]
    [InlineData("y")]
    public void ConfirmationRejectsAnythingElse(string typed) =>
        ShellModel.ConfirmationMatches("azure-environment", typed).Should().BeFalse();
}

public class AppHostSelectionTests
{
    private static AppHost Host(string path, string status = "running", int pid = 1) =>
        new(path, pid, status, null);

    [Fact]
    public void ExplicitPathWinsOverDiscovery() =>
        AppHostSelection.Select([Host("/a/A.csproj"), Host("/b/B.csproj")], "/c/C.csproj")
            .Should().BeOfType<UseAppHost>()
            .Which.Path.Should().Be("/c/C.csproj");

    [Fact]
    public void ExplicitPathWorksWithNothingRunning() =>
        AppHostSelection.Select([], "/c/C.csproj").Should().BeOfType<UseAppHost>();

    [Fact]
    public void NothingRunningIsReported() =>
        AppHostSelection.Select([], null).Should().BeOfType<NoAppHost>();

    [Fact]
    public void SingleRunningHostIsAttachedWithoutAsking() =>
        AppHostSelection.Select([Host("/a/A.csproj")], null)
            .Should().BeOfType<UseAppHost>()
            .Which.Path.Should().Be("/a/A.csproj");

    /// <summary>Guessing here is what makes the CLI act on the wrong AppHost.</summary>
    [Fact]
    public void SeveralRunningHostsAsk() =>
        AppHostSelection.Select([Host("/a/A.csproj"), Host("/b/B.csproj")], null)
            .Should().BeOfType<ChooseAppHost>()
            .Which.Candidates.Should().HaveCount(2);

    /// <summary>`aspire ps` lists stopped and starting hosts too; neither can be attached to.</summary>
    [Fact]
    public void NonRunningHostsAreIgnored()
    {
        AppHostChoice choice = AppHostSelection.Select(
            [Host("/a/A.csproj", status: "stopped"), Host("/b/B.csproj")],
            null);

        choice.Should().BeOfType<UseAppHost>().Which.Path.Should().Be("/b/B.csproj");
    }

    [Fact]
    public void OnlyStoppedHostsMeansNoneAvailable() =>
        AppHostSelection.Select([Host("/a/A.csproj", status: "stopped")], null)
            .Should().BeOfType<NoAppHost>();

    [Fact]
    public void LabelNamesTheProjectAndPid() =>
        AppHostSelection.Label(Host("/x/y/Shop.AppHost.csproj", pid: 45548))
            .Should().Be("Shop.AppHost  (pid 45548)");
}
