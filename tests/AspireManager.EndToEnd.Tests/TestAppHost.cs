using System.Diagnostics;
using Xunit;

[assembly: AssemblyFixture(typeof(AspireManager.EndToEnd.Tests.TestAppHost))]

// One TUI process serves every test, so tests must not interleave keystrokes into it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace AspireManager.EndToEnd.Tests;

/// <summary>
/// Runs <c>testapphost</c>, whose <c>manager</c> resource is this repository's TUI in an Aspire-hosted
/// terminal pointed back at that same AppHost. Tests drive it with VHS tapes through
/// <c>aspire terminal tape play</c>, which needs a CLI and AppHost of the same Aspire version. The AppHost
/// adds <c>manager</c> only when started with <c>ASPIRE_MANAGER_END_TO_END=1</c>, which <c>aspire start</c>
/// passes through to it.
/// </summary>
public sealed class TestAppHost : IAsyncLifetime
{
    private static readonly string Project = Path.Combine(
        RepositoryRoot(),
        "testapphost",
        "AspireManager.TestAppHost",
        "AspireManager.TestAppHost.csproj");

    public async ValueTask InitializeAsync()
    {
        CliResult result = await Aspire(TimeSpan.FromMinutes(5), "start", "--apphost", Project);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"aspire start failed:\n{result.Output}");
        }
    }

    public async ValueTask DisposeAsync() =>
        await Aspire(TimeSpan.FromMinutes(1), "stop", "--apphost", Project);

    /// <summary>
    /// Restarts the TUI and plays <paramref name="tape"/> against it. Keystrokes from an earlier tape stay
    /// in the process — an open dialog included — so every tape starts from a fresh one.
    /// </summary>
    public async Task<CliResult> PlayAsync(string tape)
    {
        CliResult restart = await Aspire(TimeSpan.FromMinutes(1), "resource", "manager", "restart", "--apphost", Project);

        if (restart.ExitCode != 0)
        {
            throw new InvalidOperationException($"restarting manager failed:\n{restart.Output}");
        }

        string path = Path.Combine(Path.GetTempPath(), $"aspire-manager-{Guid.NewGuid():N}.tape");
        await File.WriteAllTextAsync(path, tape);

        try
        {
            return await Aspire(
                TimeSpan.FromMinutes(2),
                "terminal", "tape", "play", "manager", "--apphost", Project, "--tape-file", path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<CliResult> Aspire(TimeSpan timeout, params string[] args)
    {
        ProcessStartInfo start = new("aspire")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Environment = { ["ASPIRE_MANAGER_END_TO_END"] = "1" },
        };

        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        start.ArgumentList.Add("--non-interactive");
        start.ArgumentList.Add("--nologo");

        using Process process = Process.Start(start)!;
        using CancellationTokenSource cts = new(timeout);

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(cts.Token);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"aspire {string.Join(' ', args)} ran past {timeout}");
        }

        return new CliResult(process.ExitCode, await stdout + await stderr);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AspireManager.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("not inside the aspire-manager repository");
    }
}

/// <summary>A failed tape's output carries the screen it gave up on, so it is the assertion message.</summary>
public sealed record CliResult(int ExitCode, string Output);
