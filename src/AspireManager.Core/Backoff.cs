namespace AspireManager.Core;

/// <summary>
/// Doubling retry delay, capped. Separate from the process plumbing so the schedule is testable without
/// spawning anything or waiting real time.
/// </summary>
public sealed class Backoff(TimeSpan initial, TimeSpan max)
{
    private readonly TimeSpan _initial = initial;

    public static Backoff Default() => new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));

    public TimeSpan Current { get; private set; } = initial;

    /// <summary>Returns the delay to wait now, then doubles it for next time.</summary>
    public TimeSpan Next()
    {
        TimeSpan current = Current;
        long doubled = Current.Ticks * 2;
        Current = doubled >= max.Ticks ? max : TimeSpan.FromTicks(doubled);
        return current;
    }

    /// <summary>Call once the stream produces data again, so a later drop retries promptly.</summary>
    public void Reset() => Current = _initial;
}
