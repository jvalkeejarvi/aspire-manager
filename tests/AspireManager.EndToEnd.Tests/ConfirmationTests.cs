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
    public async Task CommandOutsideAllowlistRunsOnlyAfterAnsweringYes()
    {
        CliResult result = await host.PlayAsync("""
            Wait+Screen@30s /R H manager/
            Type "c"
            Wait+Screen@5s /Commands: noisy/
            Enter
            Wait+Screen@5s /Run wipe-everything on noisy\?/
            Type "n"
            Wait+Screen@5s /wipe-everything cancelled/
            Type "c"
            Wait+Screen@5s /Commands: noisy/
            Enter
            Wait+Screen@5s /Run wipe-everything on noisy\?/
            Enter
            Type "y"
            Wait+Screen@10s /Successfully executed command 'wipe-everything'/
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }
}
