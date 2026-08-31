using AspireManager.Core;
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
