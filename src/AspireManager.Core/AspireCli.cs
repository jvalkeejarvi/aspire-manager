using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AspireManager.Core;

/// <summary>What the resource stream reports. Connection loss is routine — the AppHost stopping is a
/// normal thing to do with the TUI open — so it travels the same channel as the updates.</summary>
public abstract record ResourceEvent;

public sealed record ResourceUpdated(AspireResource Resource) : ResourceEvent;

public sealed record StreamConnected : ResourceEvent;

public sealed record StreamDropped(TimeSpan RetryIn) : ResourceEvent;

public sealed record CommandResult(bool Success, string Output);

/// <summary>
/// Drives the <c>aspire</c> CLI. Every call passes <c>--apphost</c> explicitly: with more than one
/// AppHost running the CLI resolves it silently and would act on the wrong one.
/// </summary>
public sealed class AspireCli(string appHostPath, string executable = "aspire")
{
    /// <summary>Running AppHosts. Takes no <c>--apphost</c> — discovery is the point.</summary>
    public async Task<IReadOnlyList<AppHost>> ListAppHostsAsync(CancellationToken ct)
    {
        CommandResult result = await RunToCompletionAsync(
            ["ps", "--format", "Json", "--nologo", "--non-interactive"],
            ct);

        return result.Success ? AspireJson.ParseAppHosts(result.Output) : [];
    }

    /// <summary>
    /// Resource updates, reconnecting with backoff when the AppHost goes away. Runs until cancelled.
    /// </summary>
    public async IAsyncEnumerable<ResourceEvent> StreamResourcesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        Backoff backoff = Backoff.Default();

        while (!ct.IsCancellationRequested)
        {
            bool announced = false;

            await foreach (string line in StreamLinesAsync(
                ["describe", "--follow", "--format", "Json", "--nologo", "--non-interactive", "--apphost", appHostPath],
                ct))
            {
                if (AspireJson.ParseResource(line) is not { } resource)
                {
                    continue;
                }

                if (!announced)
                {
                    announced = true;
                    backoff.Reset();
                    yield return new StreamConnected();
                }

                yield return new ResourceUpdated(resource);
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            TimeSpan retryIn = backoff.Next();
            yield return new StreamDropped(retryIn);
            await SafeDelayAsync(retryIn, ct);
        }
    }

    /// <summary>
    /// Every resource's logs from one process, tagged by display name. Reconnects silently — the resource
    /// stream already reports connectivity, and two sources of the same news would fight.
    /// </summary>
    public async IAsyncEnumerable<LogLine> StreamLogsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Backoff backoff = Backoff.Default();

        while (!ct.IsCancellationRequested)
        {
            await foreach (string line in StreamLinesAsync(
                ["logs", "--follow", "--format", "Json", "--timestamps", "--nologo", "--non-interactive", "--apphost", appHostPath],
                ct))
            {
                if (AspireJson.ParseLogLine(line) is { } log)
                {
                    backoff.Reset();
                    yield return log;
                }
            }

            if (ct.IsCancellationRequested)
            {
                break;
            }

            await SafeDelayAsync(backoff.Next(), ct);
        }
    }

    /// <summary>
    /// The whole log for one resource, fetched once rather than followed. Used for "open everything in an
    /// editor", where the 500-line pane buffer is not enough.
    /// </summary>
    public async Task<IReadOnlyList<LogLine>> FetchLogsAsync(string displayName, CancellationToken ct)
    {
        CommandResult result = await RunToCompletionAsync(
            ["logs", displayName, "--format", "Json", "--timestamps", "--nologo", "--non-interactive", "--apphost", appHostPath],
            ct);

        if (!result.Success)
        {
            return [];
        }

        // Without --follow the CLI emits one pretty-printed {"logs":[...]} document, not NDJSON.
        return AspireJson.ParseLogDocument(result.Output);
    }

    /// <summary><paramref name="displayName"/>, not the suffixed name — the CLI accepts both, but the
    /// suffixed one is not what the UI is holding.</summary>
    public Task<CommandResult> RunCommandAsync(string displayName, string command, CancellationToken ct) =>
        RunToCompletionAsync(
            ["resource", displayName, command, "--nologo", "--non-interactive", "--apphost", appHostPath],
            ct);

    /// <summary>Yields stdout lines until the process exits. Never throws on a failed spawn — a missing
    /// or crashed CLI is a dropped connection, which the caller already handles.</summary>
    private async IAsyncEnumerable<string> StreamLinesAsync(
        string[] args,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Process? process = TryStart(args);
        if (process is null)
        {
            yield break;
        }

        try
        {
            // Unread stderr fills its pipe buffer and wedges the child; drain and discard it.
            _ = process.StandardError.ReadToEndAsync(ct);

            while (await ReadLineOrNullAsync(process, ct) is { } line)
            {
                yield return line;
            }
        }
        finally
        {
            KillTree(process);
            process.Dispose();
        }
    }

    private async Task<CommandResult> RunToCompletionAsync(string[] args, CancellationToken ct)
    {
        Process? process = TryStart(args);
        if (process is null)
        {
            return new CommandResult(false, $"could not start '{executable}'");
        }

        try
        {
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            string output = await stdout;
            string error = await stderr;
            return new CommandResult(process.ExitCode == 0, process.ExitCode == 0 ? output : error);
        }
        catch (OperationCanceledException)
        {
            return new CommandResult(false, "cancelled");
        }
        finally
        {
            KillTree(process);
            process.Dispose();
        }
    }

    private Process? TryStart(string[] args)
    {
        ProcessStartInfo info = new(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // ArgumentList quotes each element itself; a single joined string would break on spaces in paths.
        foreach (string arg in args)
        {
            info.ArgumentList.Add(arg);
        }

        try
        {
            return Process.Start(info);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string?> ReadLineOrNullAsync(Process process, CancellationToken ct)
    {
        try
        {
            return await process.StandardOutput.ReadLineAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The whole tree, not just the child: `aspire` spawns its own children, and killing only the parent
    /// leaves them following the AppHost forever.
    /// </summary>
    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException)
        {
            // Already gone, which is the outcome we wanted.
        }
    }

    private static async Task SafeDelayAsync(TimeSpan delay, CancellationToken ct)
    {
        try
        {
            await Task.Delay(delay, ct);
        }
        catch (OperationCanceledException)
        {
            // Shutting down mid-wait is normal.
        }
    }
}
