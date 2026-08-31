using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

public class RowToneTests
{
    private static ResourceRow Item(string state, string? health = "Healthy") =>
        new ResourceItem(new AspireResource("x-abc", "x", "Project", state, health, null, null));

    [Fact]
    public void HeadingsAreTonedAsHeadings() =>
        ShellModel.Tone(new TypeHeader("Project", 2, false)).Should().Be(RowTone.Heading);

    [Fact]
    public void RunningAndHealthyIsHealthy() =>
        ShellModel.Tone(Item("Running")).Should().Be(RowTone.Healthy);

    /// <summary>Databases and containers report no health check at all; that is not a warning.</summary>
    [Fact]
    public void RunningWithNoHealthCheckIsStillHealthy() =>
        ShellModel.Tone(Item("Running", health: null)).Should().Be(RowTone.Healthy);

    [Theory]
    [InlineData("Unhealthy")]
    [InlineData("Degraded")]
    public void RunningButUnhealthyWarns(string health) =>
        ShellModel.Tone(Item("Running", health)).Should().Be(RowTone.Warning);

    [Theory]
    [InlineData("NotStarted")]
    [InlineData("Finished")]
    [InlineData("Exited")]
    [InlineData("Starting")]
    public void NotRunningIsInactive(string state) =>
        ShellModel.Tone(Item(state)).Should().Be(RowTone.Inactive);

    /// <summary>A failure has to outrank "not running", or it reads as an ordinary stop.</summary>
    [Theory]
    [InlineData("FailedToStart")]
    [InlineData("failed")]
    public void FailureStatesAreFailed(string state) =>
        ShellModel.Tone(Item(state)).Should().Be(RowTone.Failed);

    /// <summary>An unhealthy resource that has also failed reads as failed, not merely a warning.</summary>
    [Fact]
    public void FailureOutranksHealth() =>
        ShellModel.Tone(Item("FailedToStart", "Unhealthy")).Should().Be(RowTone.Failed);
}

public class HealthMarkTests
{
    private static AspireResource Resource(string? health) =>
        new("x", "x", "Project", "Running", health, null, null);

    [Theory]
    [InlineData("Healthy", "H")]
    [InlineData("Unhealthy", "U")]
    [InlineData("Degraded", "D")]
    public void HealthMarkIsTheInitial(string health, string expected) =>
        ShellModel.HealthMark(Resource(health)).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoHealthCheckShowsADash(string? health) =>
        ShellModel.HealthMark(Resource(health)).Should().Be("-");

    [Fact]
    public void HealthyIsGreen() =>
        ShellModel.HealthTone(Resource("Healthy")).Should().Be(RowTone.Healthy);

    [Fact]
    public void UnhealthyIsTheLoudestTone() =>
        ShellModel.HealthTone(Resource("Unhealthy")).Should().Be(RowTone.Failed);

    /// <summary>Degraded is a warning, not a failure.</summary>
    [Fact]
    public void DegradedWarns() =>
        ShellModel.HealthTone(Resource("Degraded")).Should().Be(RowTone.Warning);

    /// <summary>Unmeasured health must not read as a problem.</summary>
    [Fact]
    public void NoHealthCheckIsMuted() =>
        ShellModel.HealthTone(Resource(null)).Should().Be(RowTone.Inactive);
}

public class StateMarkTests
{
    [Fact]
    public void StateMarkIsTheFirstLetter() =>
        ShellModel.StateMark(new AspireResource("x", "x", "Project", "Running", null, null, null))
            .Should().Be("R");

    /// <summary>Some resource types arrive with an empty state.</summary>
    [Fact]
    public void StateMarkFallsBackWhenStateIsEmpty() =>
        ShellModel.StateMark(new AspireResource("x", "x", "Project", "", null, null, null))
            .Should().Be("?");
}

public class ConnectionTests
{
    [Fact]
    public void ConnectedReadsAsHealthy()
    {
        ShellModel.ConnectionText(ConnectionState.Connected, TimeSpan.Zero).Should().Be("connected");
        ShellModel.ConnectionTone(ConnectionState.Connected).Should().Be(RowTone.Healthy);
    }

    /// <summary>A dropped AppHost is the loudest thing the panel can say.</summary>
    [Fact]
    public void ReconnectingShowsTheCountdownAndReadsAsFailed()
    {
        ShellModel.ConnectionText(ConnectionState.Reconnecting, TimeSpan.FromSeconds(4))
            .Should().Be("reconnecting in 4s");
        ShellModel.ConnectionTone(ConnectionState.Reconnecting).Should().Be(RowTone.Failed);
    }

    [Fact]
    public void ConnectingIsAWarningNotAFailure()
    {
        ShellModel.ConnectionText(ConnectionState.Connecting, TimeSpan.Zero).Should().Be("connecting");
        ShellModel.ConnectionTone(ConnectionState.Connecting).Should().Be(RowTone.Warning);
    }
}

/// <summary>
/// Aspire is free to add states, health values and command states we have never seen. None of them may
/// crash, and each has to fall somewhere sensible — these pin down where, so the choice is deliberate
/// rather than accidental. Nothing here is bound to an enum, precisely so an unknown value cannot throw.
/// </summary>
public class UnknownValueTests
{
    private static AspireResource Resource(string state, string? health = null) =>
        new("x-1", "x", "Project", state, health, null, null);

    /// <summary>A state we do not know reads as inactive: grey, not a false alarm.</summary>
    [Theory]
    [InlineData("Starting")]
    [InlineData("RuntimeUnhealthy")]
    [InlineData("SomethingAspireAddsIn2027")]
    public void UnknownStateIsInactiveNotFailed(string state) =>
        ShellModel.Tone(new ResourceItem(Resource(state))).Should().Be(RowTone.Inactive);

    /// <summary>Anything containing "Fail" is treated as a failure, whatever the rest of it says.</summary>
    [Theory]
    [InlineData("FailedToStart")]
    [InlineData("RuntimeFailure")]
    public void AnythingFailingReadsAsFailed(string state) =>
        ShellModel.Tone(new ResourceItem(Resource(state))).Should().Be(RowTone.Failed);

    /// <summary>
    /// An unrecognised health value warns rather than passing silently — better a yellow letter that
    /// prompts a look than a green one that lies.
    /// </summary>
    [Fact]
    public void UnknownHealthWarns() =>
        ShellModel.HealthTone(Resource("Running", "Recovering")).Should().Be(RowTone.Warning);

    /// <summary>Commands fail closed: a state that is not exactly Enabled is not offered.</summary>
    [Theory]
    [InlineData("Hidden")]
    [InlineData("SomethingNew")]
    [InlineData(null)]
    public void UnknownCommandStateIsNotOffered(string? state) =>
        CommandPolicy.Classify("restart", new AspireCommand("Restart", null, state!, 1, null))
            .Should().Be(CommandAvailability.Unavailable);

    /// <summary>
    /// The one place an unknown value hides something: only "running" counts as attachable, so an AppHost
    /// reporting anything else is not offered. Conservative, but worth knowing.
    /// </summary>
    [Fact]
    public void AppHostWithAnUnknownStatusIsNotOffered() =>
        AppHostSelection.Select([new AppHost("/x/A.csproj", 1, "starting", null)], null)
            .Should().BeOfType<NoAppHost>();

    /// <summary>Every unknown value still renders a row rather than throwing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("Whatever")]
    public void UnknownValuesStillRender(string state) =>
        ShellModel.Row(Resource(state, "Weird")).Should().NotBeNullOrEmpty();
}
