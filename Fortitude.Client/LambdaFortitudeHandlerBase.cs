using System.Collections.Concurrent;

namespace Fortitude.Client;
public class LambdaFortitudeHandlerBase : FortitudeHandlerBase
{
    private readonly HashSet<string> _methods;
    private readonly string? _route;
    private readonly Dictionary<string, string> _headers;
    private readonly Dictionary<string, string> _queryParams;
    private readonly Func<byte[]?, bool>? _bodyPredicate;
    private readonly Func<FortitudeRequest, FortitudeResponse, Task> _asyncResponder;

    // Track received requests
    private readonly ConcurrentQueue<FortitudeRequest> _receivedRequests = new();
    private readonly List<TaskCompletionSource<FortitudeRequest>> _waiters = new();

    public LambdaFortitudeHandlerBase(IEnumerable<string> methods,
        string? route,
        Dictionary<string, string> headers,
        Dictionary<string, string> queryParams,
        Func<byte[]?, bool>? bodyPredicate,
        Func<FortitudeRequest, FortitudeResponse, Task> asyncResponder)
    {
        _route = route;
        _headers = headers;
        _queryParams = queryParams;
        _bodyPredicate = bodyPredicate;
        _asyncResponder = asyncResponder;
        _methods = new HashSet<string>(methods, StringComparer.OrdinalIgnoreCase);
    }

    public LambdaFortitudeHandlerBase(IEnumerable<string> methods,
        string? route,
        Dictionary<string, string> headers,
        Dictionary<string, string> queryParams,
        Func<byte[]?, bool>? bodyPredicate,
        Action<FortitudeRequest, FortitudeResponse> responder)
        : this(methods, route, headers, queryParams, bodyPredicate, (req, res) =>
        {
            responder(req, res);
            return Task.CompletedTask;
        })
    { }

    public override bool Matches(FortitudeRequest req)
    {
        if (_methods.Any() && !_methods.Contains(req.Method))
            return false;

        if (_route != null && !_route.Equals(req.Route, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var header in _headers)
        {
            if (!req.Headers.TryGetValue(header.Key, out var values) || !values.Contains(header.Value))
                return false;
        }

        foreach (var qp in _queryParams)
        {
            if (!req.Query.TryGetValue(qp.Key, out var values) || !values.Contains(qp.Value))
                return false;
        }

        if (_bodyPredicate != null && !_bodyPredicate(req.Body))
            return false;

        return true;
    }

    public override async Task<FortitudeResponse> BuildResponse(FortitudeRequest req)
    {
        // Track the request
        _receivedRequests.Enqueue(req);

        // Notify any waiters
        lock (_waiters)
        {
            foreach (var waiter in _waiters.ToList())
            {
                if (!waiter.Task.IsCompleted && Matches(req))
                {
                    waiter.SetResult(req);
                    _waiters.Remove(waiter);
                }
            }
        }

        var response = new FortitudeResponse(req.RequestId);
        await _asyncResponder(req, response);
        return response;
    }

    // -----------------------------
    // New tracking / assertion helpers
    // -----------------------------

    public override IReadOnlyList<FortitudeRequest> ReceivedRequests => _receivedRequests.ToArray();

    /// <summary>
    /// Wait asynchronously for a matching request to be received.
    /// </summary>
    public override Task<FortitudeRequest?> WaitForRequestAsync(int timeoutMs = 5000, Func<FortitudeRequest, bool>? predicate = null)
    {
        var tcs = new TaskCompletionSource<FortitudeRequest?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Check if we already have a matching request
        var existing = _receivedRequests.FirstOrDefault(r => predicate?.Invoke(r) ?? true);
        if (existing != null)
        {
            tcs.SetResult(existing);
            return tcs.Task;
        }

        // Otherwise wait
        lock (_waiters)
        {
            _waiters.Add(tcs);
        }

        // Setup timeout
        var cts = new CancellationTokenSource(timeoutMs);
        cts.Token.Register(() => tcs.TrySetResult(null));

        return tcs.Task;
    }

    /// <summary>
    /// Simple assertion helper to check if at least one matching request was received.
    /// </summary>
    public bool HasReceived(Func<FortitudeRequest, bool>? predicate = null) =>
        _receivedRequests.Any(r => predicate?.Invoke(r) ?? true);
}