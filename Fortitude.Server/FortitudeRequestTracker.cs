using Fortitude.Client;

namespace Fortitude.Server;

/// <summary>
///     Keeps a bounded, in-memory history of requests and their responses for the live UI.
/// </summary>
/// <remarks>
///     This is a diagnostic buffer, not an audit log: only the most recent
///     <see cref="Capacity" /> exchanges are retained. A response is discarded together with
///     the request it belongs to, so a long-running server holds a fixed amount of memory.
/// </remarks>
public class RequestTracker
{
    /// <summary>How many recent request/response pairs are retained.</summary>
    public const int Capacity = 1000;

    private readonly object _lock = new();
    private readonly Queue<FortitudeRequest> _requests = new();
    private readonly Dictionary<Guid, FortitudeResponse> _responses = new();

    private long _totalRequests;

    /// <summary>
    ///     Snapshot of the retained requests, oldest first.
    /// </summary>
    public IReadOnlyList<FortitudeRequest> Requests
    {
        get
        {
            lock (_lock)
            {
                return _requests.ToArray();
            }
        }
    }

    /// <summary>
    ///     Every request seen since start, including ones since evicted from the buffer.
    /// </summary>
    public long TotalRequests => Interlocked.Read(ref _totalRequests);

    /// <summary>
    ///     How many requests are currently retained, at most <see cref="Capacity" />.
    /// </summary>
    public int RetainedRequests
    {
        get
        {
            lock (_lock)
            {
                return _requests.Count;
            }
        }
    }

    /// <summary>Raised whenever a request or response is recorded.</summary>
    public event Action? OnUpdate;

    /// <summary>Records an incoming request, evicting the oldest if the buffer is full.</summary>
    public void Add(FortitudeRequest req)
    {
        if (req == null) return;

        Interlocked.Increment(ref _totalRequests);

        lock (_lock)
        {
            _requests.Enqueue(req);

            // Drop the response alongside its request; keeping it would grow without bound.
            while (_requests.Count > Capacity)
            {
                var evicted = _requests.Dequeue();
                _responses.Remove(evicted.RequestId);
            }
        }

        OnUpdate?.Invoke();
    }

    /// <summary>Records the response to a previously tracked request.</summary>
    public void Add(FortitudeResponse response)
    {
        if (response == null) return;

        lock (_lock)
        {
            // If the request has already been evicted there is nothing to attach this to,
            // and storing it would reintroduce the unbounded growth.
            if (!_requests.Any(r => r.RequestId == response.RequestId))
                return;

            _responses[response.RequestId] = response;
        }

        OnUpdate?.Invoke();
    }

    /// <summary>The response for a request, or null if none has been recorded yet.</summary>
    public FortitudeResponse? GetResponse(Guid requestId)
    {
        lock (_lock)
        {
            return _responses.GetValueOrDefault(requestId);
        }
    }

    /// <summary>Clears the buffer. Does not reset <see cref="TotalRequests" />.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _requests.Clear();
            _responses.Clear();
        }

        OnUpdate?.Invoke();
    }
}
