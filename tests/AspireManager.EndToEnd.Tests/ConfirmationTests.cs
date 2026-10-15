using AwesomeAssertions;
using Xunit;

namespace AspireManager.EndToEnd.Tests;

/// <summary>
/// Assertions are <c>Wait+Screen</c> lines inside the tape: playback fails at the first one that times out
/// and prints the screen it was looking at.
/// </summary>
public class ConfirmationTests(TestAppHost host)
{
    [Fact]
    public async Task CommandOutsideAllowlistRunsOnlyAfterTypingResourceName()
    {
        CliResult result = await host.PlayAsync("""
            Wait+Screen@30s /R H manager/
            Type "c"
            Wait+Screen@5s /Commands: noisy/
            Enter
            Wait+Screen@5s /Type the resource name/
            Type "nois"
            Enter
            Wait+Screen@5s /name does not match/
            Type "y"
            Enter
            Wait+Screen@10s /Successfully executed command 'wipe-everything'/
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }
}
