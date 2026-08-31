namespace AspireManager.Core;

/// <summary>
/// The current resource set, fed by <c>aspire describe --follow</c>, which re-emits a whole resource
/// object on every state change rather than a delta.
/// </summary>
public sealed class ResourceStore
{
    // ponytail: one lock for the whole store; the stream writes and the UI thread reads, and both are
    // far below contention. Split per-resource only if a redraw ever blocks on ingest.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, AspireResource> _byName = new(StringComparer.Ordinal);

    public void Upsert(AspireResource resource)
    {
        lock (_gate)
        {
            _byName[resource.Name] = resource;
        }
    }

    /// <summary>Drops everything. Switching AppHost must not leave the previous one's resources behind.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _byName.Clear();
        }
    }

    /// <summary>Ordered by display name so the list does not reshuffle as states change.</summary>
    public IReadOnlyList<AspireResource> Resources()
    {
        lock (_gate)
        {
            return [.. _byName.Values.OrderBy(static r => r.DisplayName, StringComparer.Ordinal)];
        }
    }

    /// <summary>
    /// True when another resource shares this one's display name. Logs are keyed by display name and
    /// carry nothing finer, so in that case the two resources' output is genuinely indistinguishable —
    /// the pane can only say so, not untangle it. Replicas are the way this happens.
    /// </summary>
    public bool HasAmbiguousLogs(AspireResource resource)
    {
        lock (_gate)
        {
            int matches = 0;
            foreach (AspireResource other in _byName.Values)
            {
                if (string.Equals(other.DisplayName, resource.DisplayName, StringComparison.Ordinal) && ++matches > 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
