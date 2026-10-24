using System.Collections.Concurrent;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;

namespace Fortitude.Client;

/// <summary>
///     A robust Fortitude request handler that matches requests, tracks received requests,
///     and supports synchronous or asynchronous responders.
/// </summary>
public class FortitudeHandler
{
    private readonly Func<FortitudeRequest, FortitudeResponse, Task> _asyncResponder;
    private readonly Func<byte[]?, bool>? _bodyPredicate;

    private readonly Dictionary<string, string> _headers;
    private readonly HashSet<string> _methods;
    private readonly Dictionary<string, string> _queryParams;

    private readonly ConcurrentQueue<FortitudeRequest> _receivedRequests = new();
    private readonly string? _route;
    private readonly RouteTemplate? _routeTemplate;

    private readonly List<Waiter> _waiters = new();
    private readonly object _waitersLock = new();
    private readonly Func<FortitudeRequest, bool>? _requestPredicate;

    /// <summary>
    ///     Initializes a new instance of <see cref="FortitudeHandler" /> with an asynchronous responder.
    /// </summary>
    public FortitudeHandler(IEnumerable<string>? methods,
        string? route,
        Dictionary<string, string>? headers,
        Dictionary<string, string>? queryParams,
        Func<byte[]?, bool>? bodyPredicate,
        Func<FortitudeRequest, bool>? requestPredicate,
        Func<FortitudeRequest, FortitudeResponse, Task> asyncResponder)
    {
        _methods = new HashSet<string>(methods ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _route = route;
        _headers = headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _queryParams = queryParams ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _bodyPredicate = bodyPredicate;
        _requestPredicate = requestPredicate;
        _routeTemplate = route != null ? TemplateParser.Parse(route) : null;
        _asyncResponder = asyncResponder ?? throw new ArgumentNullException(nameof(asyncResponder));
    }

    /// <summary>
    ///     Initializes a new instance of <see cref="FortitudeHandler" /> with a synchronous responder.
    /// </summary>
    public FortitudeHandler(IEnumerable<string>? methods,
        string? route,
        Dictionary<string, string>? headers,
        Dictionary<string, string>? queryParams,
        Func<byte[]?, bool>? bodyPredicate,
        Func<FortitudeRequest, bool>? requestPredicate,
        Action<FortitudeRequest, FortitudeResponse> responder)
        : this(methods, route, headers, queryParams, bodyPredicate, requestPredicate,
            (req, res) =>
            {
                responder?.Invoke(req, res);
                return Task.CompletedTask;
            })
    {
    }

    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Thread-safe list of received requests.
    /// </summary>
    public IReadOnlyList<FortitudeRequest> ReceivedRequests => _receivedRequests.ToArray();

    /// <summary>
    ///     Checks if this handler matches the given request.
    /// </summary>
    public bool Matches(FortitudeRequest req)
    {
        if (!Enabled) return false;
        if (_methods.Any() && !_methods.Contains(req.Method)) return false;

        if (_routeTemplate != null)
        {
            // Define the template
            var matcher = new TemplateMatcher(_routeTemplate, new RouteValueDictionary());
            var dict = new RouteValueDictionary();
            if (!matcher.TryMatch(req.Route, dict)) return false;
        }

        foreach (var header in _headers)
            if (!req.Headers.TryGetValue(header.Key, out var values) || !values.Contains(header.Value))
                return false;

        foreach (var qp in _queryParams)
            if (!req.Query.TryGetValue(qp.Key, out var values) || !values.Contains(qp.Value))
                return false;

        if (_bodyPredicate != null && !_bodyPredicate(req.Body)) return false;
        if (_requestPredicate != null && !_requestPredicate(req)) return false;

        return true;
    }

    /// <summary>
    ///     Handles the request, invokes the responder, tracks it, and notifies waiters.
    /// </summary>
    public async Task<FortitudeResponse> HandleRequestAsync(FortitudeRequest req)
    {
        if (req == null) throw new ArgumentNullException(nameof(req));

        _receivedRequests.Enqueue(req);
        NotifyWaiters(req);

        var response = new FortitudeResponse(req.RequestId);

        try
        {
            await _asyncResponder(req, response).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Error executing responder for request {req.RequestId}", ex);
        }

        return response;
    }

    /// <summary>
    ///     Returns true if at least one matching request has been received.
    /// </summary>
    public bool HasReceived(Func<FortitudeRequest, bool>? predicate = null)
    {
        return _receivedRequests.Any(r => predicate?.Invoke(r) ?? true);
    }

    /// <summary>
    ///     Waits asynchronously for a matching request or until the timeout expires.
    /// </summary>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <param name="predicate">Optional predicate to filter requests.</param>
    /// <returns>The matching request, or null if timeout expires.</returns>
    public Task<FortitudeRequest?> WaitForRequestAsync(int timeoutMs = 5000,
        Func<FortitudeRequest, bool>? predicate = null)
    {
        if (timeoutMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), "Timeout must be greater than zero.");

        var existing = _receivedRequests.FirstOrDefault(r => predicate?.Invoke(r) ?? true);
        if (existing != null)
            return Task.FromResult(existing)!;

        var waiter = new Waiter(predicate);

        lock (_waitersLock)
        {
            _waiters.Add(waiter);
        }

        waiter.StartTimeout(timeoutMs, () => Remove(waiter));

        return waiter.Task;
    }

    /// <summary>
    ///     Notifies waiting tasks of a newly received request.
    /// </summary>
    /// <remarks>
    ///     Only waiters whose own predicate accepts the request are completed. The request has
    ///     already satisfied this handler's match criteria by the time it gets here, so
    ///     re-testing those would ignore what the caller actually asked to wait for.
    /// </remarks>
    private void NotifyWaiters(FortitudeRequest req)
    {
        List<Waiter> satisfied;

        lock (_waitersLock)
        {
            satisfied = _waiters.Where(w => w.Accepts(req)).ToList();

            foreach (var waiter in satisfied)
                _waiters.Remove(waiter);
        }

        // Complete outside the lock: continuations may run inline and call back in.
        foreach (var waiter in satisfied)
            waiter.Complete(req);
    }

    private void Remove(Waiter waiter)
    {
        lock (_waitersLock)
        {
            _waiters.Remove(waiter);
        }
    }

    /// <summary>
    ///     One pending <see cref="WaitForRequestAsync" /> call: the task to complete, the
    ///     caller's filter, and the timeout that abandons it.
    /// </summary>
    private sealed class Waiter(Func<FortitudeRequest, bool>? predicate)
    {
        private readonly TaskCompletionSource<FortitudeRequest?> _tcs =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private CancellationTokenSource? _cts;
        private CancellationTokenRegistration _registration;

        public Task<FortitudeRequest?> Task => _tcs.Task;

        public bool Accepts(FortitudeRequest req) =>
            !_tcs.Task.IsCompleted && (predicate?.Invoke(req) ?? true);

        public void StartTimeout(int timeoutMs, Action onTimeout)
        {
            _cts = new CancellationTokenSource(timeoutMs);

            _registration = _cts.Token.Register(() =>
            {
                if (_tcs.TrySetResult(null))
                    onTimeout();

                Cleanup();
            });
        }

        public void Complete(FortitudeRequest req)
        {
            _tcs.TrySetResult(req);
            Cleanup();
        }

        /// <summary>
        ///     Releases the timer backing the timeout. Without this every wait leaks a
        ///     <see cref="CancellationTokenSource" /> and its registration for the life of
        ///     the handler.
        /// </summary>
        private void Cleanup()
        {
            _registration.Dispose();
            _cts?.Dispose();
            _cts = null;
        }
    }

    public override string ToString()
    {
        var parts = new List<string>();

        // Methods
        if (_methods.Any())
            parts.Add($"[{string.Join(", ", _methods)}]");
        else
            parts.Add("[ANY]");

        // Route
        parts.Add(_route ?? "*");

        // Query parameters
        if (_queryParams.Any())
        {
            var qp = string.Join("&",
                _queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            parts[^1] += $"?{qp}";
        }

        // Headers
        if (_headers.Any())
        {
            var hdr = string.Join(", ",
                _headers.Select(kvp => $"{kvp.Key}={kvp.Value}"));
            parts.Add($"(Headers: {hdr})");
        }

        // Body predicate indicator
        if (_bodyPredicate != null)
            parts.Add("(Body: predicate)");

        return string.Join(" ", parts);
    }

    /// <summary>
    ///     Starts building a handler.
    /// </summary>
    /// <returns>A <see cref="FortitudeHandlerBuilder" /> for fluent configuration.</returns>
    public static FortitudeHandlerBuilder Accepts()
    {
        return new FortitudeHandlerBuilder(null);
    }
}