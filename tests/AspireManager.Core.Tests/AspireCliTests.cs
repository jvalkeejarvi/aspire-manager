using AspireManager.Core;
using AwesomeAssertions;
using Xunit;

namespace AspireManager.Core.Tests;

/// <summary>
/// Drives the real process plumbing against a stand-in <c>aspire</c> script, so spawning, line reading,
/// exit codes and the reconnect loop are all exercised without an AppHost and without an interface that
/// exists only to be mocked.
/// </summary>
public class AspireCliTests : IDisposable
{
    /// <summary>The stand-in CLI is a shell script, so these cover the Unix path only.</summary>
    public static bool RunsShellScripts => !OperatingSystem.IsWindows();

    private readonly List<string> _scripts = [];

    private string FakeCli(string body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"fake-aspire-{Guid.NewGuid():N}.sh");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        _scripts.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (string script in _scripts)
        {
            File.Delete(script);
        }
    }

    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task ParsesAppHostListFromCliOutput()
    {
        string exe = FakeCli("""
            echo '[{"appHostPath":"/x/A.csproj","appHostPid":42,"status":"running"}]'
            """);

        IReadOnlyList<AppHost> hosts = await new AspireCli("/x/A.csproj", exe)
            .ListAppHostsAsync(TestContext.Current.CancellationToken);

        hosts.Should().HaveCount(1);
        hosts[0].AppHostPid.Should().Be(42);
    }

    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task StreamAnnouncesConnectionThenUpdatesThenDropWhenTheCliExits()
    {
        string exe = FakeCli("""
            echo '{"name":"ticker-abc","displayName":"ticker","resourceType":"Executable","state":"Running"}'
            """);

        List<ResourceEvent> events = [];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        await foreach (ResourceEvent e in new AspireCli("/x/A.csproj", exe).StreamResourcesAsync(cts.Token))
        {
            events.Add(e);
            if (events.Count == 3)
            {
                break;
            }
        }

        events[0].Should().BeOfType<StreamConnected>();
        events[1].Should().BeOfType<ResourceUpdated>()
            .Which.Resource.DisplayName.Should().Be("ticker");
        events[2].Should().BeOfType<StreamDropped>()
            .Which.RetryIn.Should().Be(TimeSpan.FromSeconds(1));
    }

    /// <summary>A CLI that exits before emitting anything must not be reported as connected.</summary>
    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task SilentCliExitIsADropNotAConnection()
    {
        string exe = FakeCli("exit 1");

        List<ResourceEvent> events = [];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        await foreach (ResourceEvent e in new AspireCli("/x/A.csproj", exe).StreamResourcesAsync(cts.Token))
        {
            events.Add(e);
            break;
        }

        events.Should().ContainSingle().Which.Should().BeOfType<StreamDropped>();
    }

    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task LogStreamYieldsParsedLines()
    {
        string exe = FakeCli("""
            echo '{"resourceName":"ticker","timestamp":"2026-08-31T11:23:47.219Z","content":"tick","isError":false}'
            """);

        List<LogLine> lines = [];
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        await foreach (LogLine line in new AspireCli("/x/A.csproj", exe).StreamLogsAsync(cts.Token))
        {
            lines.Add(line);
            break;
        }

        lines.Should().ContainSingle();
        lines[0].ResourceName.Should().Be("ticker");
        lines[0].Content.Should().Be("tick");
    }

    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task CommandSucceedsOnZeroExit()
    {
        string exe = FakeCli("echo 'stopped successfully'");

        CommandResult result = await new AspireCli("/x/A.csproj", exe)
            .RunCommandAsync("ticker", "stop", TestContext.Current.CancellationToken);

        result.Success.Should().BeTrue();
        result.Output.Should().Contain("stopped successfully");
    }

    /// <summary>On failure the caller needs stderr, not the empty stdout.</summary>
    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task CommandFailsOnNonZeroExitAndReportsStderr()
    {
        string exe = FakeCli("echo 'no such resource' >&2\nexit 1");

        CommandResult result = await new AspireCli("/x/A.csproj", exe)
            .RunCommandAsync("nope", "stop", TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
        result.Output.Should().Contain("no such resource");
    }

    /// <summary>A missing CLI must surface as a failed command, not an unhandled Win32Exception.</summary>
    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task MissingExecutableIsAFailedCommandNotAThrow()
    {
        CommandResult result = await new AspireCli("/x/A.csproj", "/nonexistent/aspire-xyz")
            .RunCommandAsync("ticker", "stop", TestContext.Current.CancellationToken);

        result.Success.Should().BeFalse();
    }

    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task MissingExecutableEndsTheStreamRatherThanThrowing()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(20));

        await foreach (ResourceEvent e in new AspireCli("/x/A.csproj", "/nonexistent/aspire-xyz")
                           .StreamResourcesAsync(cts.Token))
        {
            e.Should().BeOfType<StreamDropped>();
            break;
        }
    }

    /// <summary>Cancellation has to end the loop; a stream that outlives the TUI orphans the CLI.</summary>
    [Fact(Skip = "the stand-in CLI is a shell script", SkipUnless = nameof(RunsShellScripts))]
    public async Task CancellationEndsTheStream()
    {
        string exe = FakeCli("while true; do echo '{\"name\":\"a\",\"displayName\":\"a\",\"resourceType\":\"X\",\"state\":\"Running\"}'; sleep 1; done");
        using CancellationTokenSource cts = new();

        Task consume = Task.Run(
            async () =>
            {
                await foreach (ResourceEvent _ in new AspireCli("/x/A.csproj", exe).StreamResourcesAsync(cts.Token))
                {
                    cts.Cancel();
                }
            },
            TestContext.Current.CancellationToken);

        await consume.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        consume.IsCompletedSuccessfully.Should().BeTrue();
    }
}
