using AspireManager.Core;

namespace AspireManager.Tui;

/// <summary>
/// One attachment to one AppHost: the CLI, the two follow streams, and the tasks pumping them into the
/// stores. Switching AppHost is stopping one of these and starting another, which is why the pumps do not
/// live in Program.
/// </summary>
internal sealed class AppHostSession
{
    private readonly ResourceStore _resources;
    private readonly LogStore _logs;
    private readonly Action<ConnectionState, TimeSpan> _onConnection;
    private CancellationTokenSource? _cts;
    private Task? _logPump;
    private Task? _resourcePump;

    public AppHostSession(
        string path,
        ResourceStore resources,
        LogStore logs,
        Action<ConnectionState, TimeSpan> onConnection)
    {
        Path = path;
        Cli = new AspireCli(path);
        _resources = resources;
        _logs = logs;
        _onConnection = onConnection;
    }

    public string Path { get; }

    public AspireCli Cli { get; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _logPump = Task.Run(
            async () =>
            {
                await foreach (LogLine line in Cli.StreamLogsAsync(token))
                {
                    // Strip once here rather than on every redraw: a chatty resource re-renders constantly.
                    _logs.Add(line with { Content = AnsiText.Strip(line.Content) });
                }
            },
            token);

        _resourcePump = Task.Run(
            async () =>
            {
                await foreach (ResourceEvent update in Cli.StreamResourcesAsync(token))
                {
                    switch (update)
                    {
                        case StreamConnected:
                            _onConnection(ConnectionState.Connected, TimeSpan.Zero);
                            break;

                        case StreamDropped dropped:
                            _onConnection(ConnectionState.Reconnecting, dropped.RetryIn);
                            break;

                        case ResourceUpdated(AspireResource resource):
                            _resources.Upsert(resource);
                            break;
                    }
                }
            },
            token);
    }

    /// <summary>
    /// Cancels both streams and waits for their CLI children to be killed. Bounded: a stream that will not
    /// stop must not wedge a switch, and its children are killed by the cancellation regardless.
    /// </summary>
    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();

        foreach (Task? pump in new[] { _logPump, _resourcePump })
        {
            if (pump is null)
            {
                continue;
            }

            try
            {
                await pump.WaitAsync(TimeSpan.FromSeconds(3), CancellationToken.None);
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
                // Expected on shutdown.
            }
        }

        _cts.Dispose();
        _cts = null;
        _logPump = null;
        _resourcePump = null;
    }
}
