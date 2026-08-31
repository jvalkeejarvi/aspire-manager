namespace AspireManager.Core;

/// <summary>
/// Per-resource log history fed by a single <c>aspire logs --follow</c> stream, which interleaves every
/// resource and tags each line with a display name. Bounded, because that one stream never stops.
///
/// Exact repeats are dropped. Every reconnect starts a fresh <c>aspire logs --follow</c>, which replays the
/// whole history from the beginning — four reconnects meant four copies of it in the pane. Dropping the
/// repeat rather than asking for <c>--tail 0</c> keeps the lines that were emitted while disconnected,
/// which is precisely when a resource is restarting and its logs matter most.
/// </summary>
public sealed class LogStore(int capacityPerResource = 500)
{
    // ponytail: one lock for every ring; ingest is one line at a time off a single stream. Per-ring
    // locks only if a chatty resource ever starves a redraw.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Ring> _byResource = new(StringComparer.Ordinal);

    public void Add(LogLine line)
    {
        lock (_gate)
        {
            if (!_byResource.TryGetValue(line.ResourceName, out Ring? ring))
            {
                ring = new Ring(capacityPerResource);
                _byResource[line.ResourceName] = ring;
            }

            ring.Add(line);
        }
    }

    /// <summary>Drops every buffer. Switching AppHost must not leave the previous one's logs behind.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _byResource.Clear();
        }
    }

    /// <summary>Oldest to newest. Empty when nothing has been seen for that display name yet.</summary>
    public IReadOnlyList<LogLine> For(string displayName)
    {
        lock (_gate)
        {
            return _byResource.TryGetValue(displayName, out Ring? ring) ? ring.Snapshot() : [];
        }
    }

    private sealed class Ring(int capacity)
    {
        private readonly LogLine[] _items = new LogLine[capacity];

        // Keys of the lines currently held, so a replay can be recognised in O(1).
        private readonly HashSet<(DateTimeOffset Timestamp, string Content, bool IsError)> _keys = [];
        private int _next;
        private int _count;

        public void Add(LogLine line)
        {
            (DateTimeOffset, string, bool) key = (line.Timestamp, line.Content, line.IsError);
            if (!_keys.Add(key))
            {
                return;
            }

            // Evicting: drop the departing line's key too, or the set grows past the ring.
            if (_count == _items.Length)
            {
                LogLine evicted = _items[_next];
                _keys.Remove((evicted.Timestamp, evicted.Content, evicted.IsError));
            }

            _items[_next] = line;
            _next = (_next + 1) % _items.Length;
            _count = Math.Min(_count + 1, _items.Length);
        }

        public IReadOnlyList<LogLine> Snapshot()
        {
            LogLine[] result = new LogLine[_count];
            int oldest = _count < _items.Length ? 0 : _next;
            for (int i = 0; i < _count; i++)
            {
                result[i] = _items[(oldest + i) % _items.Length];
            }

            return result;
        }
    }
}
